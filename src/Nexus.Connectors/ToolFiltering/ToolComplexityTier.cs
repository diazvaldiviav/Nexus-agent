namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Discrete complexity tier for a tool's input schema.
/// </summary>
public enum ToolComplexityTier
{
    /// <summary>Score below 0.50 — flat schemas with few parameters.</summary>
    Simple,
    /// <summary>Score 0.50–0.79 — some nesting or many parameters.</summary>
    Moderate,
    /// <summary>Score 0.80+ — deep nesting, arrays of objects, or semantic complexity.</summary>
    Complex
}
