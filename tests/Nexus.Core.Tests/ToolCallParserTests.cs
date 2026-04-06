using System.Text.Json;
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

    [Fact]
    public void TryParse_WindowsPathWithUnescapedBackslashes_ParsesCorrectly()
    {
        // gemma4 outputs Windows paths with single backslashes (invalid JSON)
        var response = @"[TOOL_CALL: {""name"": ""write_file"", ""arguments"": {""path"": ""D:\Nova Tech\Nexus\scrum_plan.md"", ""content"": ""hello""}}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal(@"D:\Nova Tech\Nexus\scrum_plan.md", result.Arguments["path"]);
        Assert.Equal("hello", result.Arguments["content"]);
    }

    [Fact]
    public void SanitizeInvalidEscapes_FixesInvalidButPreservesValid()
    {
        // \N and \s are invalid, \n and \" are valid
        var input = @"""D:\Nova Tech\secret\nope""";
        var result = ToolCallParser.SanitizeInvalidEscapes(input);

        // \N → \\N, \s → \\s, \n stays \n
        Assert.Equal(@"""D:\\Nova Tech\\secret\nope""", result);
    }

    [Fact]
    public void SanitizeInvalidEscapes_TrailingBackslashQuote_FixedToLiteralBackslash()
    {
        // "D:\ecommerce\" followed by } — the \" is a trailing path backslash, not escaped quote
        var input = @"{""path"": ""D:\ecommerce\""}";
        var result = ToolCallParser.SanitizeInvalidEscapes(input);

        // Should become \\\" so JSON sees: literal backslash + close quote
        Assert.Contains(@"\\""", result);
        Assert.EndsWith(@"\\""}", result);
    }

    [Fact]
    public void TryParse_TrailingBackslashInPath_ParsesCorrectly()
    {
        // Real case: model generates path ending with \ before closing quote
        var response = @"[TOOL_CALL: {""name"": ""create_directory"", ""path"": ""D:\Nova Tech\Nexus\ecommerce\""}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("create_directory", result.Name);
        Assert.Contains("ecommerce", (string)result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_TrailingBackslashInMoveFile_ParsesBothPaths()
    {
        // Real case: move_file with destination ending in backslash
        var response = @"[TOOL_CALL: {""name"": ""move_file"", ""source"": ""D:\docs\model"", ""destination"": ""D:\ecommerce\""}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("move_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Contains("model", (string)result.Arguments["source"]);
        Assert.Contains("ecommerce", (string)result.Arguments["destination"]);
    }

    [Fact]
    public void SanitizeInvalidEscapes_MidStringEscapedQuote_PreservedAsIs()
    {
        // A real escaped quote mid-string should NOT be treated as trailing backslash
        var input = @"{""content"": ""He said \""hello\"" today""}";
        var result = ToolCallParser.SanitizeInvalidEscapes(input);

        // The \" mid-string should stay as \" (escaped quote)
        Assert.Contains(@"\""hello\""", result);
    }

    [Fact]
    public void TryParse_ProperlyEscapedBackslashes_StillWorks()
    {
        // Already-valid JSON with double backslashes should not be broken
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "D:\\Nova Tech\\file.txt", "content": "test"}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal(@"D:\Nova Tech\file.txt", result.Arguments!["path"]);
    }

    // --- Flat arguments normalization (gemma4:e2b format) ---

    [Fact]
    public void TryParse_FlatArguments_NormalizesIntoArgumentsDictionary()
    {
        // gemma4:e2b omits the "arguments" wrapper
        var response = """[TOOL_CALL: {"name": "move_file", "source": "/a/b.txt", "destination": "/c/d.txt"}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("move_file", result.Name);
        Assert.NotNull(result.Arguments);
        Assert.Equal("/a/b.txt", result.Arguments["source"]);
        Assert.Equal("/c/d.txt", result.Arguments["destination"]);
    }

    [Fact]
    public void TryParse_FlatArguments_PreservesTypes()
    {
        var response = """[TOOL_CALL: {"name": "set_config", "port": 8080, "verbose": true}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal(8080.0, result.Arguments!["port"]);
        Assert.Equal(true, result.Arguments["verbose"]);
    }

    [Fact]
    public void TryParse_ArgumentsPropertyTakesPriorityOverFlatProperties()
    {
        // If model produces both "arguments" and a stray flat property, "arguments" wins
        var response = """[TOOL_CALL: {"name": "read_file", "arguments": {"path": "/correct"}, "path": "/wrong"}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("/correct", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_NestedObjectArgument_StoredAsJsonElement()
    {
        var response = """[TOOL_CALL: {"name": "create_entity", "arguments": {"metadata": {"key": "value", "count": 3}}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.IsType<JsonElement>(result.Arguments!["metadata"]);
        var element = (JsonElement)result.Arguments["metadata"];
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("value", element.GetProperty("key").GetString());
    }

    [Fact]
    public void TryParse_OnlyName_NullArguments_NoFlatFallback()
    {
        var response = """[TOOL_CALL: {"name": "list_tools"}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("list_tools", result.Name);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void TryParse_EmptyArgumentsObject_ReturnsEmptyDictionary()
    {
        var response = """[TOOL_CALL: {"name": "ping", "arguments": {}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.NotNull(result.Arguments);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void TryParse_FlatArguments_WithWindowsPath_Normalizes()
    {
        // gemma4 flat format + unescaped backslashes (two bugs at once)
        // Using \N and \s which are invalid JSON escapes (not \f which is valid form-feed)
        var response = @"[TOOL_CALL: {""name"": ""write_file"", ""path"": ""D:\Nova Tech\scrum.md"", ""content"": ""hello""}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.Equal(@"D:\Nova Tech\scrum.md", result.Arguments!["path"]);
        Assert.Equal("hello", result.Arguments["content"]);
    }
}
