namespace Nexus.Connectors.ToolFiltering;

public interface IToolComplexityClassifier
{
    ToolComplexityScore Classify(ToolDefinition tool);
}
