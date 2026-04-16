using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Stateless classifier that scores tool input schemas by structural complexity.
/// Uses a weighted formula combining parameter counts, nesting depth,
/// array-of-objects presence, enum constraints, and semantic hints.
/// </summary>
public sealed class ToolComplexityClassifier : IToolComplexityClassifier
{
    private static readonly string[] SemanticDescriptionKeywords =
        ["nested", "array of objects", "recursive", "hierarchical"];

    private static readonly string[] SemanticNamePatterns =
        ["edit_file", "multi_edit"];

    internal const int MaxNestingDepthCap = 5;
    internal const double SimpleTierThreshold = 0.50;
    internal const double ModerateTierThreshold = 0.80;

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

        // Weight rationale: arrayOfObjects (0.35) dominates because it is the strongest
        // predictor of small-model failure; nesting depth (0.25) is second because JSON
        // structure confusion causes malformed calls; semantic hints (0.15) and required
        // params (0.15) are moderate signals; total params (0.08), enum (0.05) and
        // optional excess (0.05) are minor adjustments.
        double score = 0.15 * requiredCount
                     + 0.08 * totalParams
                     + 0.25 * Math.Max(0, maxNestingDepth - 1)
                     + (hasArrayOfObjects ? 0.35 : 0)
                     + (hasEnumConstraints ? 0.05 : 0)
                     + (hasSemanticHint ? 0.15 : 0)
                     + 0.05 * Math.Max(0, optionalCount - 3);

        var tier = score < SimpleTierThreshold ? ToolComplexityTier.Simple
                 : score < ModerateTierThreshold ? ToolComplexityTier.Moderate
                 : ToolComplexityTier.Complex;

        _logger?.LogDebug(
            "Classified '{ToolName}': score={Score:F2}, tier={Tier}, depth={Depth}, arrayOfObj={AoO}",
            tool.Name, score, tier, maxNestingDepth, hasArrayOfObjects);

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
        if (currentDepth >= MaxNestingDepthCap)
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
            if (tool.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        foreach (var pattern in SemanticNamePatterns)
        {
            if (tool.Name?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        if (tool.Name?.StartsWith("patch_", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }
}
