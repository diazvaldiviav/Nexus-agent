namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Classifies MCP tool definitions by schema complexity to determine
/// whether a given model can reliably call them.
/// </summary>
public interface IToolComplexityClassifier
{
    /// <summary>
    /// Analyzes the tool's input schema and returns a complexity score and tier.
    /// </summary>
    ToolComplexityScore Classify(ToolDefinition tool);
}
