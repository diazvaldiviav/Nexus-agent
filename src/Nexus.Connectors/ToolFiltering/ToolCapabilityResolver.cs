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
    // <3B models struggle with nested JSON, <8B can handle moderate schemas.
    internal const double LimitedModelThreshold = 3.0;
    internal const double CapableModelThreshold = 8.0;

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

        if (b < LimitedModelThreshold)
            return ToolCallingTier.Limited;

        if (b < CapableModelThreshold)
            return ToolCallingTier.Capable;

        return ToolCallingTier.Full;
    }
}
