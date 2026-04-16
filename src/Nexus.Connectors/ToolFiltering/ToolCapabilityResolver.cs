using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexus.Connectors.ToolFiltering;

public static class ToolCapabilityResolver
{
    private static readonly Regex ParamRegex = new(
        @"(\d+(?:\.\d+)?)\s*b(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        if (b < 3)
            return ToolCallingTier.Limited;

        if (b < 8)
            return ToolCallingTier.Capable;

        return ToolCallingTier.Full;
    }
}
