namespace Nexus.Connectors.ToolFiltering;

public record ToolComplexityScore(
    string ToolName,
    double Score,
    ToolComplexityTier Tier,
    int RequiredParamCount,
    int TotalParamCount,
    int MaxNestingDepth,
    bool HasArrayOfObjects);
