using Nexus.Core.Services;

namespace Nexus.Core.Tests;

public class ToolCallParserTests
{
    [Fact]
    public void TryParse_ValidToolCall_ReturnsRequest()
    {
        var response = """[TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/test.txt", result.Arguments["path"]);
    }

    [Fact]
    public void TryParse_WithNoArguments_ReturnsRequestWithNullArgs()
    {
        var response = """[TOOL_CALL: {"name": "list_tools"}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("list_tools", result.Name);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void TryParse_NoToolCall_ReturnsNull()
    {
        var response = "This is a normal response without any tool call markers.";
        Assert.Null(ToolCallParser.TryParse(response));
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        var response = """[TOOL_CALL: {"name": "read_file", "arguments": {broken json}]""";
        Assert.Null(ToolCallParser.TryParse(response));
    }

    [Fact]
    public void TryParse_MissingOuterBrace_RepairsAndParses()
    {
        // qwen3 sometimes drops the outer closing brace
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.html", "content": "<button>Click</button>"}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/test.html", result.Arguments["path"]);
        Assert.Equal("<button>Click</button>", result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_NestedJsonWithEscapedQuotes_Parses()
    {
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.html", "content": "<button onclick=\"alert('hi')\">Click</button>"}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
    }

    [Fact]
    public void GetTextBeforeToolCall_ReturnsTextBeforeMarker()
    {
        var response = """
            Let me look that up for you.
            [TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]
            """;

        var result = ToolCallParser.GetTextBeforeToolCall(response);

        Assert.Contains("Let me look that up for you.", result);
        Assert.DoesNotContain("TOOL_CALL", result);
    }

    // --- Real-world failure cases from qwen3:14b ---

    [Fact]
    public void TryParse_CSharpWithConnectionString_ExtractsCompleteContent()
    {
        // Real case: DbContext with SQL Server connection string containing \\ and special chars
        var content = "using Microsoft.EntityFrameworkCore;\\n\\npublic class ApplicationDbContext : DbContext\\n{\\n    public DbSet<Product> Products { get; set; }\\n\\n    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)\\n    {\\n        optionsBuilder.UseSqlServer(\\\"Server=(localdb)\\\\\\\\.$;Database=ECommerceDB;Trusted_Connection=True\\\");\\n    }\\n}";
        var response = "[TOOL_CALL: {\"name\": \"write_file\", \"arguments\": {\"path\": \"D:\\\\Nexus\\\\ecommerce\\\\Data\\\\ApplicationDbContext.cs\", \"content\": \"" + content + "\"}}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("D:\\Nexus\\ecommerce\\Data\\ApplicationDbContext.cs", result.Arguments["path"]);
        Assert.Contains("ApplicationDbContext", (string)result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_ContentWithCurlyBracesInStrings_ParsesCorrectly()
    {
        // Content has { } inside JSON string values (C# code)
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.cs", "content": "public class Foo { public int Bar { get; set; } }"}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("public class Foo { public int Bar { get; set; } }", result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_ContentWithNewlinesAndBraces_ParsesCorrectly()
    {
        // Multiline C# content with nested braces — the case that broke the old regex
        var response = "[TOOL_CALL: {\"name\": \"write_file\", \"arguments\": {\"path\": \"/tmp/Program.cs\", \"content\": \"namespace App\\n{\\n    class Program\\n    {\\n        static void Main()\\n        {\\n            Console.WriteLine(\\\"Hello\\\");\\n        }\\n    }\\n}\"}}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Contains("namespace App", (string)result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_MissingTwoClosingBraces_Repairs()
    {
        // LLM drops both closing braces
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/f.txt", "content": "hello"]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("hello", result.Arguments["content"]);
    }

    [Fact]
    public void TryParse_TextBeforeAndAfterToolCall_ExtractsToolCall()
    {
        // LLM wraps tool call in explanation text (violates prompt rules but happens)
        var response = """
            Let me create that file for you.
            [TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/test.txt", "content": "data"}}]
            I've created the file.
            """;

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
    }

    [Fact]
    public void TryParse_EmptyStringContent_ParsesCorrectly()
    {
        // The "fake delete" case — write_file with empty content
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "D:\\Nexus\\index.html", "content": ""}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("", result.Arguments["content"]);
    }

    [Fact]
    public void ExtractJsonBlock_ReturnsNull_WhenNoMarker()
    {
        Assert.Null(ToolCallParser.ExtractJsonBlock("no marker here"));
    }

    [Fact]
    public void ExtractJsonBlock_ReturnsNull_WhenNoOpenBrace()
    {
        Assert.Null(ToolCallParser.ExtractJsonBlock("[TOOL_CALL: no brace"));
    }

    [Fact]
    public void ExtractJsonBlock_DeeplyNestedContent_ExtractsAll()
    {
        // 3 levels of nesting inside string values
        var json = """{"name": "write_file", "arguments": {"path": "/f.json", "content": "{\"a\": {\"b\": {\"c\": 1}}}"}}""";
        var text = $"[TOOL_CALL: {json}]";

        var extracted = ToolCallParser.ExtractJsonBlock(text);

        Assert.Equal(json, extracted);
    }
}
