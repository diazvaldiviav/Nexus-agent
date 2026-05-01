using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Connectors.Catalog;

/// <summary>
/// Verifies mutating MCP tool calls by applying rules from IVerificationCatalog.
/// Supports SnapshotDiff, ResponseShape, and ResponseKeywords verification strategies.
/// </summary>
public sealed class McpToolVerifier : IToolVerifier
{
    private readonly IVerificationCatalog _catalog;
    private readonly IMcpClientManager _mcpClient;
    private readonly NexusConfig _config;
    private readonly ILogger<McpToolVerifier>? _logger;

    public McpToolVerifier(
        IVerificationCatalog catalog,
        IMcpClientManager mcpClient,
        NexusConfig config,
        ILogger<McpToolVerifier>? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VerificationOutcome> VerifyAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object>? arguments,
        IReadOnlyDictionary<string, object>? preSnapshot,
        string toolResult,
        CancellationToken cancellationToken = default)
    {
        var rule = _catalog.GetRule(serverName, toolName);
        if (rule is null) return VerificationOutcome.NoRule();
        if (!rule.Mutates) return VerificationOutcome.NoRule();

        try
        {
            return rule.Method switch
            {
                VerificationMethod.SnapshotDiff =>
                    await VerifySnapshotDiffAsync(serverName, rule, arguments, preSnapshot, toolResult, cancellationToken)
                        .ConfigureAwait(false),
                VerificationMethod.ResponseShape =>
                    VerifyResponseShape(rule, toolResult),
                VerificationMethod.ResponseKeywords =>
                    VerifyResponseKeywords(rule, toolResult),
                _ => VerificationOutcome.NoRule()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Verifier] {Tool} verification threw — degraded", toolName);
            return VerificationOutcome.Failed($"verification error: {ex.Message}", confidence: 0.5f);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object>? arguments,
        CancellationToken cancellationToken = default)
    {
        var rule = _catalog.GetRule(serverName, toolName);
        if (rule is null || !rule.Mutates ||
            rule.Method != VerificationMethod.SnapshotDiff || rule.Snapshot is null)
            return null;

        try
        {
            return await InvokeSnapshotAsync(serverName, rule.Snapshot, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[Verifier] {Tool} pre-snapshot failed — proceeding without", toolName);
            return null;
        }
    }

    private const string SnapshotContentKey = "content";

    // ── SnapshotDiff ──────────────────────────────────────────────────────────

    private async Task<VerificationOutcome> VerifySnapshotDiffAsync(
        string serverName,
        VerificationRule rule,
        IReadOnlyDictionary<string, object>? arguments,
        IReadOnlyDictionary<string, object>? preSnapshot,
        string toolResult,
        CancellationToken cancellationToken)
    {
        if (rule.Snapshot is null)
            return VerificationOutcome.NoRule();

        var preContent = ExtractContent(preSnapshot);

        // Capture post-snapshot with a timeout linked to the caller's CT
        IReadOnlyDictionary<string, object>? postSnapshot = null;
        var timeoutSeconds = _config.Mcp.VerificationSnapshotTimeoutSeconds;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            postSnapshot = await InvokeSnapshotAsync(serverName, rule.Snapshot, arguments, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out (not cancelled by caller)
            return VerificationOutcome.Failed(
                $"verification snapshot for {rule.Tool} timed out after {timeoutSeconds}s");
        }

        var postContent = ExtractContent(postSnapshot);

        if (rule.EmptyPostIsFailure && string.IsNullOrWhiteSpace(postContent))
            return VerificationOutcome.Failed("post snapshot is empty - tool did not write anything");

        return CompareSnapshots(rule.Snapshot.Compare, preContent, postContent, toolResult);
    }

    /// <summary>
    /// Invokes the snapshot read-tool on <paramref name="serverName"/> and returns
    /// a dictionary containing the raw string content under the <c>content</c> key.
    /// Returns <see langword="null"/> when the snapshot tool name is blank or when
    /// <see cref="ResolveJsonPathArgs"/> cannot map all required argument JSONPaths.
    /// Propagates <see cref="OperationCanceledException"/> so timeout logic in the
    /// caller can distinguish a user cancel from a per-snapshot timeout.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object>?> InvokeSnapshotAsync(
        string serverName,
        SnapshotSpec snap,
        IReadOnlyDictionary<string, object>? originalArgs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snap.Tool))
            return null;

        var resolvedArgs = ResolveJsonPathArgs(snap, originalArgs);
        if (resolvedArgs is null)
            return null;

        var mutableArgs = resolvedArgs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var content = await _mcpClient.InvokeToolAsync(serverName, snap.Tool, mutableArgs, cancellationToken)
            .ConfigureAwait(false);

        return new Dictionary<string, object> { [SnapshotContentKey] = content };
    }

    /// <summary>
    /// Maps each snapshot argument name to a value resolved from <paramref name="originalArgs"/>
    /// via a minimal JSONPath subset (<c>$.field</c> and <c>$.field[N]</c>).
    /// Returns an empty dictionary when the snapshot has no declared arguments.
    /// Returns <see langword="null"/> — and logs a debug entry — when any JSONPath
    /// resolves to null, indicating the snapshot call cannot proceed safely.
    /// </summary>
    private Dictionary<string, object>? ResolveJsonPathArgs(
        SnapshotSpec snap,
        IReadOnlyDictionary<string, object>? originalArgs)
    {
        // snap.Args is keyed by the snapshot-tool's arg name, valued by a JSONPath into originalArgs
        // We support the minimal subset: $.fieldName and $.fieldName[N]
        if (snap.Args is null || snap.Args.Count == 0)
            return new Dictionary<string, object>();

        if (originalArgs is null)
            return null;

        var result = new Dictionary<string, object>();
        foreach (var (snapshotArgName, jsonPath) in snap.Args)
        {
            var value = ResolveJsonPath(jsonPath, originalArgs);
            if (value is null)
            {
                _logger?.LogDebug(
                    "[Verifier] JSONPath '{Path}' resolved to null — snapshot args incomplete", jsonPath);
                return null;
            }

            result[snapshotArgName] = value;
        }

        return result;
    }

    /// <summary>
    /// Resolves a single JSONPath expression against a flat argument dictionary,
    /// supporting the minimal subset: <c>$.fieldName</c> for direct field lookup and
    /// <c>$.fieldName[N]</c> for zero-based indexed access into any <see cref="System.Collections.IList"/>.
    /// Returns <see langword="null"/> when the expression does not start with <c>$.</c>,
    /// the field is absent, or the index is out of range.
    /// </summary>
    private static object? ResolveJsonPath(string jsonPath, IReadOnlyDictionary<string, object> args)
    {
        // Minimal subset: $.fieldName  or  $.fieldName[N]
        if (!jsonPath.StartsWith("$.", StringComparison.Ordinal))
            return null;

        var expression = jsonPath[2..]; // strip "$."

        // Handle array indexer: fieldName[N]
        var bracketIdx = expression.IndexOf('[');
        if (bracketIdx >= 0)
        {
            var fieldName = expression[..bracketIdx];
            var closeBracket = expression.IndexOf(']', bracketIdx);
            if (closeBracket < 0) return null;

            var indexStr = expression[(bracketIdx + 1)..closeBracket];
            if (!int.TryParse(indexStr, out var index)) return null;

            if (!args.TryGetValue(fieldName, out var arrayVal)) return null;

            if (arrayVal is System.Collections.IList list && index >= 0 && index < list.Count)
                return list[index];

            return null;
        }

        // Simple field lookup
        return args.TryGetValue(expression, out var val) ? val : null;
    }

    /// <summary>
    /// Compares pre- and post-snapshot content strings according to the <paramref name="compare"/>
    /// strategy declared in the rule (<c>not_equal</c> by default; <c>different_size</c> as alternative).
    /// Returns <see cref="VerificationOutcome.Failed"/> when the comparison condition is not met,
    /// indicating the tool had no observable effect on the resource.
    /// </summary>
    private static VerificationOutcome CompareSnapshots(
        string compare,
        string? preContent,
        string? postContent,
        string toolResult)
    {
        _ = toolResult; // reserved for future heuristic enrichment

        return compare.ToLowerInvariant() switch
        {
            "different_size" =>
                (preContent?.Length ?? 0) != (postContent?.Length ?? 0)
                    ? VerificationOutcome.Verified("post snapshot has different size from pre-snapshot")
                    : VerificationOutcome.Failed("post snapshot is the same size as pre-snapshot — no change detected"),

            _ => // "not_equal" is the default
                !string.Equals(preContent, postContent, StringComparison.Ordinal)
                    ? VerificationOutcome.Verified("post snapshot differs from pre-snapshot")
                    : VerificationOutcome.Failed("post snapshot is identical to pre-snapshot — tool had no effect")
        };
    }

    /// <summary>
    /// Extracts the raw string value stored under the <c>content</c> key in a snapshot dictionary
    /// produced by <see cref="InvokeSnapshotAsync"/>. Returns <see langword="null"/> when
    /// the snapshot is null or the key is absent, allowing callers to treat a missing snapshot
    /// as equivalent to an empty pre-state.
    /// </summary>
    private static string? ExtractContent(IReadOnlyDictionary<string, object>? snapshot)
    {
        if (snapshot is null) return null;
        return snapshot.TryGetValue(SnapshotContentKey, out var val) ? val?.ToString() : null;
    }

    // ── ResponseShape ─────────────────────────────────────────────────────────

    /// <summary>
    /// Implements the <c>response_shape</c> verification strategy: parses <paramref name="toolResult"/>
    /// as JSON and asserts each required JSONPath field is present and non-empty.
    /// Returns <see cref="VerificationOutcome.Failed"/> immediately if the result is not valid JSON,
    /// or if any required field is missing or has a null/empty-string value.
    /// </summary>
    private static VerificationOutcome VerifyResponseShape(VerificationRule rule, string toolResult)
    {
        if (rule.RequiredFields.Count == 0)
            return VerificationOutcome.Verified("no required fields specified");

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(toolResult).RootElement;
        }
        catch (JsonException)
        {
            return VerificationOutcome.Failed("tool result is not valid JSON");
        }

        foreach (var field in rule.RequiredFields)
        {
            var value = ResolveJsonPathInElement(field, root);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null ||
                (value.Value.ValueKind == JsonValueKind.String && value.Value.GetString() == ""))
                return VerificationOutcome.Failed($"required field '{field}' is missing or empty");
        }

        return VerificationOutcome.Verified("all required fields present");
    }

    /// <summary>
    /// Resolves a <c>$.fieldName</c> JSONPath expression against a <see cref="JsonElement"/> root
    /// object, returning the matching property element.
    /// Returns <see langword="null"/> when the path does not start with <c>$.</c>, when the root
    /// is not a JSON object, or when the named property does not exist.
    /// </summary>
    private static JsonElement? ResolveJsonPathInElement(string jsonPath, JsonElement root)
    {
        if (!jsonPath.StartsWith("$.", StringComparison.Ordinal))
            return null;

        var fieldName = jsonPath[2..];
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(fieldName, out var prop))
            return prop;

        return null;
    }

    // ── ResponseKeywords ──────────────────────────────────────────────────────

    /// <summary>
    /// Implements the <c>response_keywords</c> verification strategy: scans <paramref name="toolResult"/>
    /// (case-insensitive) for failure keywords first, then for at least one success keyword.
    /// Returns <see cref="VerificationOutcome.Failed"/> when any failure keyword is found or no
    /// success keyword is present; returns <see cref="VerificationOutcome.Verified"/> when the
    /// success-keyword list is empty (keyword-presence check disabled) or a keyword matches.
    /// </summary>
    private static VerificationOutcome VerifyResponseKeywords(VerificationRule rule, string toolResult)
    {
        var lower = toolResult.ToLowerInvariant();

        foreach (var k in rule.FailureKeywords)
        {
            if (lower.Contains(k.ToLowerInvariant()))
                return VerificationOutcome.Failed($"failure keyword '{k}' present");
        }

        if (rule.SuccessKeywords.Count == 0)
            return VerificationOutcome.Verified();

        foreach (var k in rule.SuccessKeywords)
        {
            if (lower.Contains(k.ToLowerInvariant()))
                return VerificationOutcome.Verified();
        }

        return VerificationOutcome.Failed("no success keyword found");
    }
}
