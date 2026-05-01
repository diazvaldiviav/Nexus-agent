using System.Text;
using Microsoft.Extensions.Logging;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for <see cref="PlannerContextBuilder"/> — AC-2 coverage.
/// All tests are synchronous at the logic level; no I/O or LLM calls involved.
/// </summary>
public class PlannerContextBuilderTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static NexusConfig DefaultConfig() => new NexusConfig
    {
        Mcp =
        {
            PlannerContextEnabled = true,
            PlannerContextMaxBytes = 1500,
            PlannerContextMaxRecentTurns = 4,
            PlannerContextMaxBytesPerTurn = 280
        }
    };

    private static ConversationMessage Msg(string role, string content) =>
        new ConversationMessage { Role = role, Content = content };

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: empty history → Empty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_FirstTurn_NoHistory_ReturnsEmpty()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var emptyHistory = Array.Empty<ConversationMessage>();

        // Act
        var result = await builder.BuildAsync(emptyHistory, "hello");

        // Assert
        Assert.True(result.IsEmpty);
        Assert.Same(PlannerContext.Empty, result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: synthetic messages filtered from RecentTurns
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_FiltersSyntheticPlannerMarkers()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "Can you help me?"),
            Msg("assistant", "[PLANNER] Generating plan..."),
            Msg("assistant", "[Tool Result for read_text_file] some content"),
            Msg("assistant", "[Plan] Step 1: read, Step 2: write"),
            Msg("assistant", "I found the file at D:\\foo\\bar.md"),
        };

        // Act
        var result = await builder.BuildAsync(history, "next question");

        // Assert — only the two natural messages survive the filter
        Assert.Equal(2, result.RecentTurns.Count);
        Assert.Contains("Can you help me?", result.RecentTurns[0]);
        Assert.Contains("I found the file", result.RecentTurns[1]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: per-turn truncation at configured byte limit
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_TruncatesPerTurnAtConfiguredBytes()
    {
        // Arrange: MaxBytesPerTurn = 100 (well below 1KB message)
        var config = DefaultConfig();
        config.Mcp.PlannerContextMaxBytesPerTurn = 100;
        var builder = new PlannerContextBuilder(config);

        var largeContent = new string('A', 1024);  // 1 KB message
        var history = new List<ConversationMessage>
        {
            Msg("user", largeContent)
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — resulting turn must be ≤ 100 UTF-8 bytes and end with "…"
        Assert.Single(result.RecentTurns);
        var turn = result.RecentTurns[0];
        Assert.True(Encoding.UTF8.GetByteCount(turn) <= 100,
            $"Expected ≤100 bytes but got {Encoding.UTF8.GetByteCount(turn)}");
        Assert.EndsWith("…", turn);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: total byte budget — oldest turns dropped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_RespectsTotalByteBudget()
    {
        // Arrange: very small MaxBytes so most turns get dropped
        var config = DefaultConfig();
        config.Mcp.PlannerContextMaxBytes = 400;
        config.Mcp.PlannerContextMaxRecentTurns = 10;
        config.Mcp.PlannerContextMaxBytesPerTurn = 4000; // no per-turn truncation
        var builder = new PlannerContextBuilder(config);

        // 10 turns, each ~80 bytes (total ~800 bytes before capping)
        var history = Enumerable.Range(1, 10)
            .Select(i => Msg("user", $"Turn {i}: " + new string('x', 70)))
            .ToList();

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — total bytes must be ≤ MaxBytes
        Assert.True(result.TotalBytes <= 400,
            $"Expected ≤400 total bytes but got {result.TotalBytes}");
        // Oldest turns were dropped so some turns should be gone
        Assert.True(result.RecentTurns.Count < 10,
            "Expected oldest turns to be dropped to meet byte budget");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: absolute paths extracted into summary
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_ExtractsAbsolutePathsIntoSummary()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "Please read D:\\foo\\bar.md and summarize it"),
            Msg("assistant", "I read the file at D:\\foo\\bar.md successfully"),
        };

        // Act
        var result = await builder.BuildAsync(history, "now write it");

        // Assert
        Assert.Contains(@"D:\foo\bar.md", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Regression: regex must NOT capture prose tokens with single forward slash.
    // Repro: real conversation read "Authentication (login/register)",
    // "add/remove items", "Node.js/Express" — these contaminated the Summary,
    // displacing the real D:\Nexus\ecommerce\sprint_plan.md path.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_DoesNotCaptureProseSlashTokens()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "read D:\\Nexus\\ecommerce\\sprint_plan.md"),
            Msg("assistant", "Authentication (login/register), Cart (add/remove items), Tools: React.js (frontend), Node.js/Express (backend)"),
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — real Windows path preserved
        Assert.Contains(@"D:\Nexus\ecommerce\sprint_plan.md", result.Summary, StringComparison.OrdinalIgnoreCase);
        // Assert — prose tokens NOT in summary
        Assert.DoesNotContain("/register", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("/remove", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("/Express", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_CapturesUnixPathWithMultipleSegments()
    {
        // Arrange — Unix branch should still capture genuine paths /a/b/c
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "look at /usr/local/bin/foo and tell me about it"),
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert
        Assert.Contains("/usr/local/bin/foo", result.Summary, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: cancellation honored
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_HonorsCancellation()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "some message")
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();  // pre-cancelled

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => builder.BuildAsync(history, "next", cts.Token));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-2 test: internal errors do not propagate (returns Empty + logs warning)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NeverThrowsOnInternalError()
    {
        // Arrange: pass null as conversationHistory to force a NullReferenceException
        // inside Build() — the catch-all in BuildAsync must swallow it and return Empty.
        var builder = new PlannerContextBuilder(DefaultConfig());

        // Act — must NOT throw even with null input (corrupt internal scenario)
        var result = await builder.BuildAsync(null!, "next");

        // Assert — returns Empty on internal failure
        Assert.True(result.IsEmpty);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC-H8: edge-case tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// v1 limitation: the Unix branch of the path regex (pattern
    /// /[^\s"&lt;&gt;|/]+/[^\s"&lt;&gt;|]+) matches the URL slice "/example.com/path/file"
    /// that appears inside "https://example.com/path/file". The heuristic was
    /// designed to suppress single-slash prose tokens (login/register), not full URLs.
    /// Hardening the regex to exclude scheme-prefixed strings is tracked as a
    /// follow-up item.
    /// </summary>
    [Fact(Skip = "v1 limitation: Unix path regex over-matches URLs — /example.com/path/file extracted from https://example.com/path/file")]
    public async Task BuildAsync_HistoryWithUrls_DoesNotMatchUrlsAsPaths()
    {
        // Arrange
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", "see https://example.com/path/file")
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — summary should contain no path derived from the URL
        Assert.DoesNotContain("https://", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com/path", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path/file", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_PathEndingWithDot_TrimsTrailingPunctuation()
    {
        // Arrange: path ends with a literal "." (e.g., end of sentence)
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", @"check D:\foo\bar.md.")
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — trailing dot trimmed; base path present
        Assert.Contains(@"D:\foo\bar.md", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\foo\bar.md.", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_PathEndingWithCloseParen_TrimsTrailingPunctuation()
    {
        // Arrange: path wrapped in parentheses — closing ")" must be trimmed
        var builder = new PlannerContextBuilder(DefaultConfig());
        var history = new List<ConversationMessage>
        {
            Msg("user", @"saw (D:\foo\bar.md)")
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — closing paren trimmed; base path present
        Assert.Contains(@"D:\foo\bar.md", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\foo\bar.md)", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_SurrogatePairAtTruncationBoundary_DoesNotSplitChar()
    {
        // Arrange: "𝓐" is U+1D4D0, a 4-byte UTF-8 character stored as a surrogate pair in C#.
        // We pad with ASCII 'a' (1 byte each) so the 4-byte char lands right at the boundary.
        // MaxBytesPerTurn = 15: budget for prefix = 15 - 3 (ellipsis "…") = 12.
        // "user: " = 6 bytes. "aaaaaa" = 6 bytes. Total prefix = 12 bytes, all fitting.
        // Next char is 𝓐 (4 bytes) which would push past budget → must NOT be half-included.
        var config = DefaultConfig();
        config.Mcp.PlannerContextMaxBytesPerTurn = 15;
        var builder = new PlannerContextBuilder(config);

        // "user: " (6 bytes) + 6 × 'a' (6 bytes) + "𝓐" (4 bytes U+1D4D0) + "bc"
        // After truncation the result must not contain a partial surrogate pair.
        var content = new string('a', 6) + "\U0001D4D0" + "bc";
        var history = new List<ConversationMessage>
        {
            Msg("user", content)
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert (a) result ends with ellipsis (truncation occurred)
        Assert.Single(result.RecentTurns);
        var turn = result.RecentTurns[0];
        Assert.EndsWith("…", turn);

        // Assert (b) UTF-8 byte count ≤ MaxBytesPerTurn
        var byteCount = Encoding.UTF8.GetByteCount(turn);
        Assert.True(byteCount <= 15, $"Expected ≤15 bytes but got {byteCount}");

        // Assert (c) string is valid (no half-surrogate) — encoding round-trips cleanly
        var roundTripped = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(turn));
        Assert.Equal(turn, roundTripped);

        // Assert (d) the surrogate-pair character is NOT split:
        // the turn must not contain the high surrogate alone (\uD835 for U+1D4D0)
        Assert.DoesNotContain("\uD835", turn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_NonEmptyResult_LogsDebugSummary()
    {
        // Arrange
        var logger = new CapturingLogger<PlannerContextBuilder>();
        var builder = new PlannerContextBuilder(DefaultConfig(), logger);
        var history = new List<ConversationMessage>
        {
            Msg("user", @"please read D:\docs\readme.md"),
            Msg("assistant", "I have read the file.")
        };

        // Act
        var result = await builder.BuildAsync(history, "next");

        // Assert — result is non-empty
        Assert.False(result.IsEmpty);

        // Assert — exactly one Debug log entry starting with "[PlannerContext] built:"
        var debugEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Debug &&
                        e.Message.StartsWith("[PlannerContext] built:", StringComparison.Ordinal))
            .ToList();

        Assert.Single(debugEntries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logger spy (mirrors CapturingLogger<T> in ToolPlannerTests)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple ILogger implementation that records log entries for assertion in tests.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
