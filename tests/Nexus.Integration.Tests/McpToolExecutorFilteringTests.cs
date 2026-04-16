using System.Text.Json;
using Nexus.Connectors;
using Nexus.Connectors.ToolFiltering;
using Nexus.Integration.Tests.Fakes;

namespace Nexus.Integration.Tests;

public class McpToolExecutorFilteringTests
{
    private static ToolDefinition MakeSimpleTool(string name) => new()
    {
        Name = name,
        Description = "A simple tool",
        ServerName = "test-server",
        InputSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string" }
              },
              "required": ["path"]
            }
            """).RootElement
    };

    private static McpToolExecutor CreateExecutor(
        ToolRegistry registry,
        bool filteringEnabled = false,
        ToolPromptFormatter? formatter = null)
    {
        return new McpToolExecutor(
            new FakeMcpClientManager(),
            registry,
            logger: null,
            toolPromptFormatter: formatter,
            toolFilteringEnabled: filteringEnabled);
    }

    [Fact]
    public void GetToolDefinitionsForPrompt_FilteringDisabled_FallsBackToRegistry()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(MakeSimpleTool("read_file"));
        var formatter = new ToolPromptFormatter(new ToolComplexityClassifier());
        var executor = CreateExecutor(registry, filteringEnabled: false, formatter);

        // Act
        var result = executor.GetToolDefinitionsForPrompt("qwen3:1b");

        // Assert — falls back to unfiltered registry output
        var expected = registry.GetToolDefinitionsForPrompt();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetToolDefinitionsForPrompt_NullFormatter_FallsBackToRegistry()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(MakeSimpleTool("read_file"));
        var executor = CreateExecutor(registry, filteringEnabled: true, formatter: null);

        // Act
        var result = executor.GetToolDefinitionsForPrompt("qwen3:1b");

        // Assert
        var expected = registry.GetToolDefinitionsForPrompt();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetToolDefinitionsForPrompt_EmptyOrNullModelName_FallsBackToRegistry(string? modelName)
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(MakeSimpleTool("read_file"));
        var formatter = new ToolPromptFormatter(new ToolComplexityClassifier());
        var executor = CreateExecutor(registry, filteringEnabled: true, formatter);

        // Act
        var result = executor.GetToolDefinitionsForPrompt(modelName);

        // Assert
        var expected = registry.GetToolDefinitionsForPrompt();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetToolDefinitionsForPrompt_HappyPath_DelegatesToFormatter()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(MakeSimpleTool("read_file"));
        var formatter = new ToolPromptFormatter(new ToolComplexityClassifier());
        var executor = CreateExecutor(registry, filteringEnabled: true, formatter);

        // Act
        var result = executor.GetToolDefinitionsForPrompt("qwen3:1b");

        // Assert — formatter output differs from raw registry (includes "Available tools:" header)
        Assert.NotEmpty(result);
        Assert.Contains("read_file", result);
        // Formatter produces its own format; verify it's not identical to raw registry
        // (for a simple tool with a 1b model = Limited tier, the tool is still included but format differs)
        var formatterResult = formatter.Format(registry.Tools.Values, "qwen3:1b");
        Assert.Equal(formatterResult, result);
    }

    [Fact]
    public void GetToolDefinitionsForPrompt_EmptyTools_ReturnsEmpty()
    {
        // Arrange
        var registry = new ToolRegistry();
        var formatter = new ToolPromptFormatter(new ToolComplexityClassifier());
        var executor = CreateExecutor(registry, filteringEnabled: true, formatter);

        // Act
        var result = executor.GetToolDefinitionsForPrompt("qwen3:1b");

        // Assert
        Assert.Equal(string.Empty, result);
    }
}
