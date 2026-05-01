using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;

namespace Nexus.Core.Services;

/// <summary>
/// Heuristic planner context builder — deterministic, no LLM call, no I/O.
/// Filters synthetic messages, truncates per-turn to a configured byte budget,
/// extracts absolute file paths and last tool name into a summary, and caps
/// total context size before returning a <see cref="PlannerContext"/>.
/// </summary>
public sealed class PlannerContextBuilder : IPlannerContextBuilder
{
    // Regex: absolute Windows or Unix paths. Compiled once at class load.
    // Windows branch: drive letter + separator + chars (matches D:\foo or D:/foo).
    // Unix branch: requires at least one INTERNAL slash to exclude prose tokens
    // like "/register", "/Express", "(login/register)" — only "/dir/file" forms match.
    private static readonly Regex AbsolutePathRegex = new(
        @"(?:[A-Z]:[\\/]|/[^\s""<>|/]+/)[^\s""<>|]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Capture tool name from "[Tool Result for {Name}]" markers.
    private static readonly Regex ToolResultForRegex = new(
        @"\[Tool Result for ([^\]]+)\]",
        RegexOptions.Compiled);

    private const string Ellipsis = "…";

    private readonly NexusConfig _config;
    private readonly ILogger<PlannerContextBuilder>? _logger;

    public PlannerContextBuilder(NexusConfig config, ILogger<PlannerContextBuilder>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    /// <param name="conversationHistory">Full conversation history at the time of planning.</param>
    /// <param name="userMessage">The current user message (not yet appended to history).</param>
    /// <param name="cancellationToken">Cancellation token honored before the heuristic begins.</param>
    /// <returns>
    /// A <see cref="PlannerContext"/> with recent turns and a summary, or
    /// <see cref="PlannerContext.Empty"/> when there is no useful context.
    /// </returns>
    public Task<PlannerContext> BuildAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(Build(conversationHistory));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PlannerContextBuilder encountered an unexpected error; returning Empty.");
            return Task.FromResult(PlannerContext.Empty);
        }
    }

    private PlannerContext Build(IReadOnlyList<ConversationMessage> conversationHistory)
    {
        if (conversationHistory.Count == 0)
        {
            _logger?.LogDebug("[PlannerContext] history yielded empty context");
            return PlannerContext.Empty;
        }

        var maxBytes = _config.Mcp.PlannerContextMaxBytes;
        var maxTurns = _config.Mcp.PlannerContextMaxRecentTurns;
        var maxBytesPerTurn = _config.Mcp.PlannerContextMaxBytesPerTurn;

        // ── Step 1: collect paths and last tool name from the full history ──
        var seenPaths = new LinkedList<string>();  // maintain insertion order for dedup
        var seenPathsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastToolName = null;

        foreach (var msg in conversationHistory)
        {
            // Extract absolute paths from every message (including synthetic ones)
            foreach (Match m in AbsolutePathRegex.Matches(msg.Content))
            {
                var path = m.Value.TrimEnd('.', ',', ';', ')', ']');
                if (seenPathsSet.Add(path))
                    seenPaths.AddLast(path);
            }

            // Extract tool name from synthetic "[Tool Result for {Name}]" markers
            var toolMatch = ToolResultForRegex.Match(msg.Content);
            if (toolMatch.Success)
                lastToolName = toolMatch.Groups[1].Value.Trim();
        }

        // ── Step 2: filter out synthetic messages for RecentTurns list ──
        var naturalMessages = conversationHistory
            .Where(m => !SyntheticMarkers.IsSynthetic(m.Content))
            .ToList();

        if (naturalMessages.Count == 0)
        {
            _logger?.LogDebug("[PlannerContext] history yielded empty context");
            return PlannerContext.Empty;
        }

        // ── Step 3: take last MaxRecentTurns ──
        var recentSlice = naturalMessages.Count > maxTurns
            ? naturalMessages.GetRange(naturalMessages.Count - maxTurns, maxTurns)
            : naturalMessages;

        // ── Step 4: truncate each turn to MaxBytesPerTurn UTF-8 bytes ──
        var recentTurns = new List<string>(recentSlice.Count);
        foreach (var msg in recentSlice)
        {
            var text = $"{msg.Role}: {msg.Content}";
            var truncated = TruncateToUtf8Bytes(text, maxBytesPerTurn);
            recentTurns.Add(truncated);
        }

        // ── Step 5: build summary ──
        var summary = BuildSummary(seenPaths, lastToolName);

        // ── Step 6: honor MaxBytes total cap ──
        var totalBytes = Encoding.UTF8.GetByteCount(summary);
        foreach (var t in recentTurns)
            totalBytes += Encoding.UTF8.GetByteCount(t);

        while (recentTurns.Count > 0 && totalBytes > maxBytes)
        {
            totalBytes -= Encoding.UTF8.GetByteCount(recentTurns[0]);
            recentTurns.RemoveAt(0);
        }

        if (string.IsNullOrEmpty(summary) && recentTurns.Count == 0)
        {
            _logger?.LogDebug("[PlannerContext] history yielded empty context");
            return PlannerContext.Empty;
        }

        _logger?.LogDebug(
            "[PlannerContext] built: paths={PathCount}, lastTool={Tool}, turns={TurnCount}, bytes={TotalBytes}",
            seenPathsSet.Count, lastToolName ?? "(none)", recentTurns.Count, totalBytes);

        return new PlannerContext(summary, recentTurns, totalBytes);
    }

    /// <summary>
    /// Builds a compact summary string from the deduplicated path set and the last tool name.
    /// Emits the three most-recently-seen paths in insertion order, followed by the last tool name.
    /// Returns <see cref="string.Empty"/> when both inputs are empty or null.
    /// </summary>
    private static string BuildSummary(LinkedList<string> seenPaths, string? lastToolName)
    {
        // Keep last 3 unique paths (LinkedList preserves insertion order; take the tail)
        var paths = new List<string>();
        var node = seenPaths.Last;
        while (node != null && paths.Count < 3)
        {
            paths.Insert(0, node.Value);
            node = node.Previous;
        }

        if (paths.Count == 0 && lastToolName == null)
            return string.Empty;

        var sb = new StringBuilder();
        if (paths.Count > 0)
            sb.Append(string.Join(", ", paths));

        if (lastToolName != null)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append("last tool: ").Append(lastToolName);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Truncates <paramref name="text"/> so its UTF-8 byte length does not exceed
    /// <paramref name="maxBytes"/>, appending the ellipsis character (…) when truncation occurs.
    /// Walks character-by-character to avoid splitting surrogate pairs that encode
    /// supplementary-plane code points as two-char sequences. Returns the original
    /// string unchanged when it already fits within the budget.
    /// </summary>
    private static string TruncateToUtf8Bytes(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;

        // Binary-search for the longest prefix that fits within maxBytes - 3 (for "…")
        var ellipsisBytes = Encoding.UTF8.GetByteCount(Ellipsis);
        var budget = maxBytes - ellipsisBytes;

        // Walk char-by-char (safe for surrogate pairs: count bytes incrementally)
        var byteCount = 0;
        var charCount = 0;
        while (charCount < text.Length)
        {
            var charBytes = Encoding.UTF8.GetByteCount(text, charCount, 1);
            if (byteCount + charBytes > budget) break;
            byteCount += charBytes;
            charCount++;
        }

        return text[..charCount] + Ellipsis;
    }
}
