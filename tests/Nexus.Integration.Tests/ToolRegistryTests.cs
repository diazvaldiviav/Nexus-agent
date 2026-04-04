using Nexus.Connectors;

namespace Nexus.Integration.Tests;

/// <summary>
/// Tests for ToolRegistry — pure in-memory, no database required.
/// </summary>
public class ToolRegistryTests
{
    private static ToolDefinition CreateTool(string name, string server, string description = "A test tool") =>
        new() { Name = name, ServerName = server, Description = description };

    [Fact]
    public void RegisterTool_AddsToolToRegistry()
    {
        // Arrange
        var registry = new ToolRegistry();
        var tool = CreateTool("my_tool", "server-a");

        // Act
        registry.RegisterTool(tool);

        // Assert
        Assert.Single(registry.Tools);
        Assert.True(registry.Tools.ContainsKey("my_tool"));
    }

    [Fact]
    public void GetTool_RegisteredTool_ReturnsDefinition()
    {
        // Arrange
        var registry = new ToolRegistry();
        var tool = CreateTool("read_file", "server-a", "Reads a file from disk");
        registry.RegisterTool(tool);

        // Act
        var result = registry.GetTool("read_file");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("server-a", result.ServerName);
        Assert.Equal("Reads a file from disk", result.Description);
    }

    [Fact]
    public void GetTool_UnregisteredTool_ReturnsNull()
    {
        // Arrange
        var registry = new ToolRegistry();

        // Act
        var result = registry.GetTool("nonexistent_tool");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UnregisterToolsForServer_RemovesOnlyThatServersTools()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("tool_a", "server-1"));
        registry.RegisterTool(CreateTool("tool_b", "server-1"));
        registry.RegisterTool(CreateTool("tool_c", "server-2"));

        // Act
        registry.UnregisterToolsForServer("server-1");

        // Assert
        Assert.Single(registry.Tools);
        Assert.Null(registry.GetTool("tool_a"));
        Assert.Null(registry.GetTool("tool_b"));
        Assert.NotNull(registry.GetTool("tool_c"));
    }

    [Fact]
    public void FindToolServer_ReturnsCorrectServerName()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("search", "search-server"));

        // Act
        var serverName = registry.FindToolServer("search");

        // Assert
        Assert.Equal("search-server", serverName);
    }

    [Fact]
    public void GetToolDefinitionsForPrompt_FormatsAllTools()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file", "fs-server", "Reads a file"));
        registry.RegisterTool(CreateTool("search", "search-server", "Searches the web"));

        // Act
        var prompt = registry.GetToolDefinitionsForPrompt();

        // Assert
        Assert.Contains("Available tools:", prompt);
        Assert.Contains("- read_file: Reads a file", prompt);
        Assert.Contains("- search: Searches the web", prompt);
    }
}
