using Nexus.Connectors;

namespace Nexus.Integration.Tests;

/// <summary>
/// Tests for ToolRegistry.ResolveTool — fuzzy tool name resolution.
/// </summary>
public class ToolResolutionTests
{
    private static ToolDefinition CreateTool(string name, string server = "test-server") =>
        new() { Name = name, ServerName = server, Description = $"Tool {name}" };

    [Fact]
    public void ResolveTool_ExactMatch_ReturnsTool()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file"));

        // Act
        var result = registry.ResolveTool("read_file");

        // Assert
        Assert.NotNull(result.Tool);
        Assert.Equal("read_file", result.Tool.Name);
        Assert.Null(result.CorrectedName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResolveTool_CaseInsensitive_ReturnsCorrected()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file"));

        // Act
        var result = registry.ResolveTool("Read_File");

        // Assert
        Assert.NotNull(result.Tool);
        Assert.Equal("read_file", result.Tool.Name);
        Assert.Equal("read_file", result.CorrectedName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResolveTool_AllUpperCase_ReturnsCorrected()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file"));

        // Act
        var result = registry.ResolveTool("READ_FILE");

        // Assert
        Assert.NotNull(result.Tool);
        Assert.Equal("read_file", result.Tool.Name);
        Assert.Equal("read_file", result.CorrectedName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResolveTool_LevenshteinDist1_ReturnsCorrected()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("write_file"));

        // Act — "wrte_file" is missing 'i' (distance 1)
        var result = registry.ResolveTool("wrte_file");

        // Assert
        Assert.NotNull(result.Tool);
        Assert.Equal("write_file", result.Tool.Name);
        Assert.Equal("write_file", result.CorrectedName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResolveTool_LevenshteinDist2_ReturnsCorrected()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file"));

        // Act — "reed_fle" has distance 2 from "read_file" (a->e, missing 'i')
        var result = registry.ResolveTool("reed_file");

        // Assert
        Assert.NotNull(result.Tool);
        Assert.Equal("read_file", result.Tool.Name);
        Assert.Equal("read_file", result.CorrectedName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResolveTool_LevenshteinDistTooFar_ReturnsError()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_file"));

        // Act — "xyzabc" is far from any tool
        var result = registry.ResolveTool("xyzabc");

        // Assert
        Assert.Null(result.Tool);
        Assert.Null(result.CorrectedName);
        Assert.NotNull(result.Error);
        Assert.Contains("[InvalidTool]", result.Error);
        Assert.Contains("read_file", result.Error);
    }

    [Fact]
    public void ResolveTool_MultipleCloseMatches_ReturnsAmbiguous()
    {
        // Arrange — "read_fila" and "read_filb" are both distance 1 from "read_filc"
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("read_fila"));
        registry.RegisterTool(CreateTool("read_filb"));

        // Act — distance 1 from both tools (last char differs)
        var result = registry.ResolveTool("read_filc");

        // Assert
        Assert.Null(result.Tool);
        Assert.NotNull(result.Error);
        Assert.Contains("[InvalidTool]", result.Error);
        Assert.Contains("read_fila", result.Error);
        Assert.Contains("read_filb", result.Error);
    }

    [Fact]
    public void FindToolServer_CaseInsensitive_ReturnsServerName()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("search", "search-server"));

        // Act
        var serverName = registry.FindToolServer("SEARCH");

        // Assert
        Assert.Equal("search-server", serverName);
    }

    [Fact]
    public void ResolveTool_EmptyRegistry_ReturnsError()
    {
        // Arrange
        var registry = new ToolRegistry();

        // Act
        var result = registry.ResolveTool("anything");

        // Assert
        Assert.Null(result.Tool);
        Assert.NotNull(result.Error);
        Assert.Contains("[InvalidTool]", result.Error);
    }

    [Fact]
    public void ResolveTool_AfterUnregister_RebuildsIndex()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(CreateTool("my_tool", "server-a"));

        // Verify it resolves
        Assert.NotNull(registry.ResolveTool("MY_TOOL").Tool);

        // Act — unregister
        registry.UnregisterToolsForServer("server-a");

        // Assert — no longer resolves
        var result = registry.ResolveTool("MY_TOOL");
        Assert.Null(result.Tool);
        Assert.NotNull(result.Error);
    }
}
