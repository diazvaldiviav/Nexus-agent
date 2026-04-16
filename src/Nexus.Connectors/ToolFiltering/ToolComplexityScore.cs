namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Result of classifying a tool's input schema complexity.
/// </summary>
/// <param name="ToolName">Name of the classified tool.</param>
/// <param name="Score">Weighted complexity score (0 = trivial, higher = more complex).</param>
/// <param name="Tier">Discrete complexity tier derived from the score.</param>
/// <param name="RequiredParamCount">Number of required parameters in the schema.</param>
/// <param name="TotalParamCount">Total number of parameters (required + optional).</param>
/// <param name="MaxNestingDepth">Deepest object/array nesting level in the schema.</param>
/// <param name="HasArrayOfObjects">Whether the schema contains an array whose items are objects.</param>
public record ToolComplexityScore(
    string ToolName,
    double Score,
    ToolComplexityTier Tier,
    int RequiredParamCount,
    int TotalParamCount,
    int MaxNestingDepth,
    bool HasArrayOfObjects);
