using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Resolves a model name to its tool-calling capability tier
/// by extracting the parameter count from the name (e.g. "qwen3:1.7b" → 1.7 → Limited).
/// </summary>
public static class ToolCapabilityResolver
{
    private static readonly Regex ParamRegex = new(
        @"(\d+(?:\.\d+)?)\s*b(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Thresholds based on observed tool-calling reliability:
    // <4B models (1.7B, 2B, 3B) cannot reliably emit valid [TOOL_CALL: {...}] JSON —
    // they default to YAML/prose and break the parser; planner is skipped entirely.
    // 4B-8B handle simple/moderate tools but Complex schemas are excluded.
    // 8B-30B handle moderate schemas reliably with hints.
    // 30B+ are full-capable for arbitrary tool use.
    internal const double ChatOnlyModelThreshold = 4.0;
    internal const double LimitedModelThreshold = 8.0;
    internal const double CapableModelThreshold = 30.0;

    /// <summary>
    /// Resolves the tool-calling tier for the given model name.
    /// Returns <see cref="ToolCallingTier.Full"/> when the model name is null or unrecognized.
    /// </summary>
    public static ToolCallingTier Resolve(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return ToolCallingTier.Full;

        var match = ParamRegex.Match(modelName);
        if (!match.Success)
            return ToolCallingTier.Full;

        if (!double.TryParse(
                match.Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var b))
            return ToolCallingTier.Full;

        if (b < ChatOnlyModelThreshold)
            return ToolCallingTier.ChatOnly;

        if (b < LimitedModelThreshold)
            return ToolCallingTier.Limited;

        if (b < CapableModelThreshold)
            return ToolCallingTier.Capable;

        return ToolCallingTier.Full;
    }
}
