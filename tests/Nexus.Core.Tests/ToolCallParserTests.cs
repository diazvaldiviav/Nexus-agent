using Nexus.Core.Services;

namespace Nexus.Core.Tests;

public class ToolCallParserTests
{
    [Fact]
    public void TryParse_ValidToolCall_ReturnsRequest()
    {
        // Arrange
        var response = """[TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]""";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/test.txt", result.Arguments["path"]);
    }

    [Fact]
    public void TryParse_WithNoArguments_ReturnsRequestWithNullArgs()
    {
        // Arrange
        var response = """[TOOL_CALL: {"name": "list_tools"}]""";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("list_tools", result.Name);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void TryParse_NoToolCall_ReturnsNull()
    {
        // Arrange
        var response = "This is a normal response without any tool call markers.";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        // Arrange
        var response = """[TOOL_CALL: {"name": "read_file", "arguments": {broken json}]""";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_MissingOuterBrace_RepairsAndParses()
    {
        // Arrange — qwen3 sometimes drops the outer closing brace
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.html", "content": "<button>Click</button>"}]""";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/test.html", result.Arguments["path"]);
        Assert.Equal("<button>Click</button>", result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_NestedJsonWithEscapedQuotes_Parses()
    {
        // Arrange — content with escaped quotes (common in HTML)
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.html", "content": "<button onclick=\"alert('hi')\">Click</button>"}}]""";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
    }

    [Fact]
    public void GetTextBeforeToolCall_ReturnsTextBeforeMarker()
    {
        // Arrange
        var response = """
            Let me look that up for you.
            [TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]
            """;

        // Act
        var result = ToolCallParser.GetTextBeforeToolCall(response);

        // Assert
        Assert.Contains("Let me look that up for you.", result);
        Assert.DoesNotContain("TOOL_CALL", result);
    }
}
