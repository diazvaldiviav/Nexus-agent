using Nexus.Core.Abstractions;

namespace Nexus.Core.Services;

/// <summary>
/// Static helper that extracts file-path pattern strings from tool arguments.
/// Used by the permission gate to determine which paths a tool invocation will touch.
/// Returns <c>["*"]</c> whenever extraction is impossible — preserving the back-compat invariant
/// that a wildcard pattern always produces a permission prompt rather than silently allowing.
/// </summary>
internal static class PermissionPatternExtractor
{
    private static readonly IReadOnlyList<string> Wildcard = new[] { "*" };

    private static readonly string[] CommonPathKeys =
    {
        "path", "source", "destination", "file_path", "filename"
    };

    /// <summary>
    /// Extracts pattern strings (file paths) from tool arguments based on the catalog rule's snapshot
    /// <c>args</c> JSONPath mapping. Falls back to common argument key names when no rule is provided.
    /// Returns <c>["*"]</c> when no paths can be extracted.
    /// </summary>
    /// <param name="toolName">Name of the tool being invoked (reserved for future diagnostics).</param>
    /// <param name="arguments">Arguments the agent intends to pass to the tool.</param>
    /// <param name="rule">Verification rule from the catalog, or null if no rule exists.</param>
    public static IReadOnlyList<string> Extract(
        string toolName,
        IReadOnlyDictionary<string, object>? arguments,
        VerificationRule? rule)
    {
        try
        {
            return ExtractCore(arguments, rule);
        }
        catch
        {
            return Wildcard;
        }
    }

    private static IReadOnlyList<string> ExtractCore(
        IReadOnlyDictionary<string, object>? arguments,
        VerificationRule? rule)
    {
        if (arguments == null || arguments.Count == 0)
            return Wildcard;

        // Rule has a snapshot with args JSONPath entries — walk those values.
        if (rule?.Snapshot?.Args is { Count: > 0 } argsMap)
        {
            var results = new List<string>();
            foreach (var jsonPath in argsMap.Values)
            {
                var resolved = ResolveSimpleJsonPath(jsonPath, arguments);
                if (resolved is not null)
                {
                    var trimmed = resolved.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        results.Add(trimmed);
                }
            }

            return results.Count > 0 ? results : Wildcard;
        }

        // No rule or no snapshot — fall back to common key names.
        var fallback = new List<string>();
        foreach (var key in CommonPathKeys)
        {
            if (arguments.TryGetValue(key, out var val) && val is not null)
            {
                var trimmed = val.ToString()?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    fallback.Add(trimmed!);
            }
        }

        return fallback.Count > 0 ? fallback : Wildcard;
    }

    /// <summary>
    /// Resolves a minimal JSONPath expression (<c>$.field</c> or <c>$.field[N]</c>) against
    /// a flat argument dictionary. Returns null when the path is malformed or the key is absent.
    /// </summary>
    private static string? ResolveSimpleJsonPath(
        string jsonPath,
        IReadOnlyDictionary<string, object> args)
    {
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
                return list[index]?.ToString();

            return null;
        }

        // Simple field lookup
        return args.TryGetValue(expression, out var val) ? val?.ToString() : null;
    }
}
