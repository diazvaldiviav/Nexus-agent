using Microsoft.Extensions.Logging;
using Nexus.Connectors;
using Nexus.Connectors.Catalog;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for McpToolVerifier — AC-H8 edge-case coverage.
/// Covers invalid-JSON result for ResponseShape, and null JSONPath resolution
/// with the associated debug log.
/// </summary>
public class McpToolVerifierTests
{
    private static NexusConfig DefaultConfig() => new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static McpToolVerifier CreateVerifier(
        IVerificationCatalog catalog,
        IMcpClientManager? mcpClient = null,
        NexusConfig? config = null,
        ILogger<McpToolVerifier>? logger = null)
    {
        return new McpToolVerifier(
            catalog,
            mcpClient ?? new StubMcpClientManager(),
            config ?? DefaultConfig(),
            logger);
    }

    private static IVerificationCatalog CatalogWith(params VerificationRule[] rules)
        => new StubVerificationCatalog(rules);

    // ── ResponseShape — invalid JSON body ─────────────────────────────────────

    [Fact]
    public async Task VerifyResponseShape_InvalidJsonResult_ReturnsFailedWithReason()
    {
        // Arrange: ResponseShape rule expecting a ".path" field
        var rule = new VerificationRule
        {
            Server = "filesystem",
            Tool = "write_file",
            Mutates = true,
            Method = VerificationMethod.ResponseShape,
            RequiredFields = new List<string> { "$.path" }
        };
        var catalog = CatalogWith(rule);
        var verifier = CreateVerifier(catalog);

        // Act: pass non-JSON tool result
        var outcome = await verifier.VerifyAsync(
            "filesystem", "write_file",
            arguments: null,
            preSnapshot: null,
            toolResult: "this is not json",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(outcome.IsVerified);
        Assert.True(outcome.RuleMatched);
        // Reason must mention JSON (case-insensitive)
        Assert.Contains("json", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── ResolveJsonPathArgs — null JSONPath value → null + debug log ──────────

    [Fact]
    public async Task ResolveJsonPathArgs_NullJsonPathValue_ReturnsNullAndLogsDebug()
    {
        // Arrange: SnapshotDiff rule whose snapshot arg references "$.foo"
        var rule = new VerificationRule
        {
            Server = "filesystem",
            Tool = "write_file",
            Mutates = true,
            Method = VerificationMethod.SnapshotDiff,
            Snapshot = new SnapshotSpec
            {
                Tool = "read_text_file",
                Args = new Dictionary<string, string> { ["path"] = "$.foo" },
                Compare = "not_equal"
            }
        };
        var catalog = CatalogWith(rule);
        var logger = new CapturingLogger<McpToolVerifier>();
        var verifier = CreateVerifier(catalog, logger: logger);

        // arguments dict has no "foo" key → $.foo resolves to null
        var args = new Dictionary<string, object> { ["bar"] = "irrelevant" };

        // Act: CapturePreSnapshotAsync exercises ResolveJsonPathArgs internally
        var snapshot = await verifier.CapturePreSnapshotAsync(
            "filesystem", "write_file",
            arguments: args,
            cancellationToken: CancellationToken.None);

        // Assert (a) — snapshot is null (ResolveJsonPathArgs returned null)
        Assert.Null(snapshot);

        // Assert (b) — debug log fired with the expected substring
        var debugEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Debug &&
                        e.Message.Contains("[Verifier] JSONPath", StringComparison.Ordinal))
            .ToList();

        Assert.Single(debugEntries);
        Assert.Contains("resolved to null", debugEntries[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logger spy
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

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>
/// In-memory IVerificationCatalog backed by a fixed list of rules.
/// </summary>
file sealed class StubVerificationCatalog : IVerificationCatalog
{
    private readonly Dictionary<(string, string), VerificationRule> _rules;

    public StubVerificationCatalog(IEnumerable<VerificationRule> rules)
    {
        _rules = rules.ToDictionary(
            r => (r.Server.ToLowerInvariant(), r.Tool.ToLowerInvariant()),
            r => r);
    }

    public int Count => _rules.Count;

    public VerificationRule? GetRule(string server, string tool) =>
        _rules.TryGetValue((server.ToLowerInvariant(), tool.ToLowerInvariant()), out var rule)
            ? rule
            : null;
}

/// <summary>
/// Minimal IMcpClientManager that never gets called in these unit tests;
/// included to satisfy McpToolVerifier constructor requirements.
/// </summary>
file sealed class StubMcpClientManager : IMcpClientManager
{
    public Task<bool> ConnectAsync(McpServerEntry serverEntry, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task DisconnectAsync(string serverName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<ToolDefinition>> DiscoverToolsAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult(new List<ToolDefinition>());

    public IReadOnlyDictionary<string, bool> GetServerStatus()
        => new Dictionary<string, bool>();

    public Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}
