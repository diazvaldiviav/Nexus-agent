using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexus.Connectors.ToolFiltering;

public sealed class ToolComplexityClassifier : IToolComplexityClassifier
{
    private static readonly string[] SemanticDescriptionKeywords =
        ["nested", "array of objects", "recursive", "hierarchical"];

    private static readonly string[] SemanticNamePatterns =
        ["edit_file", "multi_edit"];

    private readonly ILogger<ToolComplexityClassifier>? _logger;

    public ToolComplexityClassifier(ILogger<ToolComplexityClassifier>? logger = null)
        => _logger = logger;

    public ToolComplexityScore Classify(ToolDefinition tool)
    {
        if (!tool.InputSchema.HasValue)
        {
            return new ToolComplexityScore(
                ToolName: tool.Name,
                Score: 0,
                Tier: ToolComplexityTier.Simple,
                RequiredParamCount: 0,
                TotalParamCount: 0,
                MaxNestingDepth: 0,
                HasArrayOfObjects: false);
        }

        var schema = tool.InputSchema.Value;

        // Extract required parameter set
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var reqArray) &&
            reqArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArray.EnumerateArray())
            {
                var name = item.GetString();
                if (name is not null)
                    required.Add(name);
            }
        }

        // Extract properties and count params
        var hasProperties = schema.TryGetProperty("properties", out var properties) &&
                            properties.ValueKind == JsonValueKind.Object;

        var totalParams = 0;
        if (hasProperties)
        {
            foreach (var _ in properties.EnumerateObject())
                totalParams++;
        }

        // Compute structural metrics
        var maxNestingDepth = ComputeMaxNestingDepth(schema, 0);
        var hasArrayOfObjects = hasProperties && DetectArrayOfObjects(properties);
        var hasEnumConstraints = hasProperties && DetectEnumConstraints(properties);
        var hasSemanticHint = DetectSemanticComplexity(tool);

        // Score formula
        var requiredCount = required.Count;
        var optionalCount = Math.Max(0, totalParams - requiredCount);

        // Weighted score: 0.15*req + 0.08*total + 0.25*depth + 0.35*arrayOfObj + 0.05*enum + 0.15*semantic + 0.05*optionalExcess
        double score = 0.15 * requiredCount
                     + 0.08 * totalParams
                     + 0.25 * Math.Max(0, maxNestingDepth - 1)
                     + (hasArrayOfObjects ? 0.35 : 0)
                     + (hasEnumConstraints ? 0.05 : 0)
                     + (hasSemanticHint ? 0.15 : 0)
                     + 0.05 * Math.Max(0, optionalCount - 3);

        var tier = score < 0.50 ? ToolComplexityTier.Simple
                 : score < 0.80 ? ToolComplexityTier.Moderate
                 : ToolComplexityTier.Complex;

        return new ToolComplexityScore(
            ToolName: tool.Name,
            Score: score,
            Tier: tier,
            RequiredParamCount: requiredCount,
            TotalParamCount: totalParams,
            MaxNestingDepth: maxNestingDepth,
            HasArrayOfObjects: hasArrayOfObjects);
    }

    private int ComputeMaxNestingDepth(JsonElement schema, int currentDepth)
    {
        if (currentDepth >= 5)
            return currentDepth;

        if (!schema.TryGetProperty("properties", out var props) ||
            props.ValueKind != JsonValueKind.Object)
            return currentDepth;

        var maxChild = currentDepth;

        foreach (var prop in props.EnumerateObject())
        {
            try
            {
                var typeStr = prop.Value.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString()
                    : null;

                if (typeStr == "object" &&
                    prop.Value.TryGetProperty("properties", out var nestedProps) &&
                    nestedProps.ValueKind == JsonValueKind.Object)
                {
                    var depth = ComputeMaxNestingDepth(prop.Value, currentDepth + 1);
                    if (depth > maxChild)
                        maxChild = depth;
                }
                else if (typeStr == "array" &&
                         prop.Value.TryGetProperty("items", out var items))
                {
                    var itemTypeStr = items.TryGetProperty("type", out var itemType)
                        ? itemType.GetString()
                        : null;

                    if (itemTypeStr == "object" || items.TryGetProperty("properties", out _))
                    {
                        var depth = ComputeMaxNestingDepth(items, currentDepth + 1);
                        if (depth > maxChild)
                            maxChild = depth;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    "Skipping malformed property {Name}: {Error}", prop.Name, ex.Message);
            }
        }

        return maxChild;
    }

    private bool DetectArrayOfObjects(JsonElement properties)
    {
        foreach (var prop in properties.EnumerateObject())
        {
            try
            {
                var typeStr = prop.Value.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString()
                    : null;

                if (typeStr != "array")
                    continue;

                if (!prop.Value.TryGetProperty("items", out var items))
                    continue;

                var itemTypeStr = items.TryGetProperty("type", out var itemType)
                    ? itemType.GetString()
                    : null;

                if (itemTypeStr == "object" || items.TryGetProperty("properties", out _))
                    return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    "Skipping malformed property {Name}: {Error}", prop.Name, ex.Message);
            }
        }

        return false;
    }

    private bool DetectEnumConstraints(JsonElement properties)
    {
        foreach (var prop in properties.EnumerateObject())
        {
            try
            {
                if (prop.Value.TryGetProperty("enum", out var enumProp) &&
                    enumProp.ValueKind == JsonValueKind.Array)
                    return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    "Skipping malformed property {Name}: {Error}", prop.Name, ex.Message);
            }
        }

        return false;
    }

    private static bool DetectSemanticComplexity(ToolDefinition tool)
    {
        foreach (var keyword in SemanticDescriptionKeywords)
        {
            if (tool.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var pattern in SemanticNamePatterns)
        {
            if (tool.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (tool.Name.StartsWith("patch_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
