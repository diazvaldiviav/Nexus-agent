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

    // --- Raw control characters in JSON strings ---

    [Fact]
    public void SanitizeInvalidEscapes_RawNewlineInString_EscapedToBackslashN()
    {
        var json = "{\"name\": \"write_file\", \"content\": \"line1\nline2\"}";
        var result = ToolCallParser.SanitizeInvalidEscapes(json);
        Assert.Contains("line1\\nline2", result);
        // Must parse as valid JSON now
        using var doc = System.Text.Json.JsonDocument.Parse(result);
        Assert.Equal("line1\nline2", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void SanitizeInvalidEscapes_RawCrLfInString_EscapedCorrectly()
    {
        var json = "{\"content\": \"a\r\nb\"}";
        var result = ToolCallParser.SanitizeInvalidEscapes(json);
        Assert.Contains("a\\r\\nb", result);
    }

    [Fact]
    public void SanitizeInvalidEscapes_RawTabInString_EscapedToBackslashT()
    {
        var json = "{\"content\": \"col1\tcol2\"}";
        var result = ToolCallParser.SanitizeInvalidEscapes(json);
        Assert.Contains("col1\\tcol2", result);
    }

    [Fact]
    public void TryParse_ContentWithRawNewlines_ParsesSuccessfully()
    {
        var response = "[TOOL_CALL: {\"name\": \"write_file\", \"arguments\": {\"path\": \"test.cs\", \"content\": \"line1\nline2\nline3\"}}]";
        var result = ToolCallParser.TryParse(response);
        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.Equal("line1\nline2\nline3", result.Arguments!["content"]);
    }

    // --- Repetition loop detection ---

    [Fact]
    public void HasRepetitionLoop_RepeatingSegment_ReturnsTrue()
    {
        // Simulates model hallucinating "\\model\\.." repeated many times
        var repeated = string.Concat(Enumerable.Repeat(@"\model\..", 20));
        Assert.True(ToolCallParser.HasRepetitionLoop(repeated));
    }

    [Fact]
    public void HasRepetitionLoop_NormalPath_ReturnsFalse()
    {
        Assert.False(ToolCallParser.HasRepetitionLoop(@"D:\Nova Tech\Nexus\Nexus-agent\ecomerce\model"));
    }

    [Fact]
    public void HasRepetitionLoop_ShortString_ReturnsFalse()
    {
        Assert.False(ToolCallParser.HasRepetitionLoop("abc"));
    }

    [Fact]
    public void SanitizeRepetitionLoops_ReplacesHallucinatedPath()
    {
        var repeated = string.Concat(Enumerable.Repeat(@"\model\..", 20));
        var args = new Dictionary<string, object>
        {
            ["paths"] = repeated,
            ["name"] = "normal_value"
        };

        ToolCallParser.SanitizeRepetitionLoops(args);

        Assert.Equal("[REPETITION_ERROR]", args["paths"]);
        Assert.Equal("normal_value", args["name"]); // untouched
    }

    [Fact]
    public void TryParse_RepetitionInArgument_MarksAsError()
    {
        var repeated = string.Concat(Enumerable.Repeat(@"\\model\\..", 20));
        var response = "[TOOL_CALL: {\"name\": \"read_multiple_files\", \"arguments\": {\"paths\": \"" + repeated + "\"}}]";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("read_multiple_files", result.Name);
        Assert.Equal("[REPETITION_ERROR]", result.Arguments!["paths"]);
    }

    // --- AC-1: Markdown fence stripping ---

    [Fact]
    public void TryParse_MarkdownJsonFence_ExtractsToolCall()
    {
        // Arrange: model wraps output in ```json ... ``` code fence
        var response = "```json\n[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/tmp/test.txt\"}}]\n```";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("/tmp/test.txt", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_MarkdownPlainFence_ExtractsToolCall()
    {
        // Arrange: plain ``` fence without language tag
        var response = "```\n[TOOL_CALL: {\"name\": \"list_tools\"}]\n```";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("list_tools", result.Name);
    }

    [Fact]
    public void TryParse_MarkdownFenceWithSurroundingText_ExtractsToolCall()
    {
        // Arrange: text before and after the fence block
        var response = "Sure, let me do that.\n```json\n[TOOL_CALL: {\"name\": \"write_file\", \"arguments\": {\"path\": \"/a.txt\", \"content\": \"hi\"}}]\n```\nDone!";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.Equal("/a.txt", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_NoFenceWithMarker_StillWorks()
    {
        // Regression: unfenced marker must still parse correctly after fence-stripping step
        var response = """[TOOL_CALL: {"name": "ping", "arguments": {}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("ping", result.Name);
    }

    // --- AC-2: XML-style <tool_call> marker ---

    [Fact]
    public void TryParse_XmlToolCallMarker_ExtractsToolCall()
    {
        // Arrange: lowercase XML tags
        var response = "<tool_call>{\"name\": \"read_file\", \"arguments\": {\"path\": \"/etc/hosts\"}}</tool_call>";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("/etc/hosts", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_XmlToolCallMarkerUppercase_ExtractsToolCall()
    {
        // Arrange: UPPERCASE XML tags (case-insensitive matching required)
        var response = "<TOOL_CALL>{\"name\": \"list_tools\"}</TOOL_CALL>";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("list_tools", result.Name);
    }

    [Fact]
    public void TryParse_XmlToolCallNoClosingTag_ExtractsToolCall()
    {
        // Arrange: model drops the closing tag (best-effort extraction)
        var response = "<tool_call>{\"name\": \"read_file\", \"arguments\": {\"path\": \"/tmp/x.txt\"}}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("/tmp/x.txt", result.Arguments!["path"]);
    }

    // --- AC-3: Raw JSON fallback ---

    [Fact]
    public void TryParse_RawJsonWithName_ExtractsToolCall()
    {
        // Arrange: bare JSON object with no surrounding marker
        var response = "{\"name\": \"read_file\", \"arguments\": {\"path\": \"/tmp/raw.txt\"}}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("/tmp/raw.txt", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_RawJsonWithTextAround_ExtractsToolCall()
    {
        // Arrange: explanatory text surrounds the raw JSON
        var response = "Sure! {\"name\": \"read_file\", \"arguments\": {\"path\": \"/tmp/raw.txt\"}} Done.";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
        Assert.Equal("/tmp/raw.txt", result.Arguments!["path"]);
    }

    [Fact]
    public void TryParse_RawJsonNoNameField_ReturnsNull()
    {
        // Arrange: JSON object without "name" must not be treated as a tool call
        var response = "{\"foo\": \"bar\", \"count\": 42}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.Null(result);
    }

    // --- Priority / regression tests ---

    [Fact]
    public void TryParse_MarkerTakesPriorityOverRawJson()
    {
        // Arrange: both bracket marker and a raw JSON block are present — marker wins
        var response = "[TOOL_CALL: {\"name\": \"marker_tool\", \"arguments\": {}}] and also {\"name\": \"raw_tool\", \"arguments\": {}}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("marker_tool", result.Name);
    }

    [Fact]
    public void TryParse_XmlTakesPriorityOverRawJson()
    {
        // Arrange: both XML tag and a raw JSON block are present — XML wins (path 2 > path 3)
        var response = "<tool_call>{\"name\": \"xml_tool\", \"arguments\": {}}</tool_call> {\"name\": \"raw_tool\", \"arguments\": {}}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("xml_tool", result.Name);
    }

    [Fact]
    public void TryParse_AllThreePresent_MarkerWins()
    {
        // Arrange: all three formats present — bracket marker has highest priority
        var response = "[TOOL_CALL: {\"name\": \"marker_tool\", \"arguments\": {}}] <tool_call>{\"name\": \"xml_tool\", \"arguments\": {}}</tool_call> {\"name\": \"raw_tool\", \"arguments\": {}}";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("marker_tool", result.Name);
    }

    [Fact]
    public void StripMarkdownFences_UnterminatedFence_StripsPrefixOnly()
    {
        // Arrange: opening fence with no closing fence
        var text = "```json\n[TOOL_CALL: {\"name\": \"read_file\"}]";

        // Act
        var result = ToolCallParser.StripMarkdownFences(text);

        // Assert: content after the fence header line is returned (no closing fence to strip)
        Assert.Contains("[TOOL_CALL:", result);
        Assert.DoesNotContain("```", result);
    }

    // --- WalkJsonObject unit tests ---

    [Fact]
    public void WalkJsonObject_SimpleObject_ReturnsCorrectEndIndex()
    {
        // Arrange
        var text = """{"name": "foo"}""";

        // Act
        var (endIndex, missingBraces, endedInString) = ToolCallParser.WalkJsonObject(text, 0);

        // Assert
        Assert.Equal(text.Length - 1, endIndex);
        Assert.Equal(0, missingBraces);
        Assert.False(endedInString);
    }

    [Fact]
    public void WalkJsonObject_UnclosedObject_ReturnsMissingBraceCount()
    {
        // Arrange: one closing brace is missing
        var text = "{\"name\": \"foo\", \"arguments\": {\"path\": \"/x\"";

        // Act
        var (endIndex, missingBraces, endedInString) = ToolCallParser.WalkJsonObject(text, 0);

        // Assert
        Assert.Equal(-1, endIndex);
        Assert.True(missingBraces > 0);
        Assert.False(endedInString);
    }

    // --- AC-1: Mid-string JSON repair ---

    [Fact]
    public void WalkJsonObject_EndedInString_ReportsTrue()
    {
        // Arrange: truncated mid-string value
        var text = "{\"name\": \"foo\", \"val\": \"bar";

        // Act
        var (endIndex, missingBraces, endedInString) = ToolCallParser.WalkJsonObject(text, 0);

        // Assert
        Assert.Equal(-1, endIndex);
        Assert.True(endedInString);
    }

    [Fact]
    public void WalkJsonObject_EndedOutsideString_ReportsFalse()
    {
        // Arrange: truncated between properties (outside a string value)
        var text = "{\"name\": \"foo\", \"arguments\": {\"path\": \"/x\"";

        // Act
        var (endIndex, missingBraces, endedInString) = ToolCallParser.WalkJsonObject(text, 0);

        // Assert
        Assert.Equal(-1, endIndex);
        Assert.False(endedInString);
    }

    [Fact]
    public void TryParse_TruncatedMidStringValue_RepairsAndParses()
    {
        // Arrange: LLM output cut off mid-value string
        var response = "[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/some/pa";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert: partial path is repaired, name is extracted
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
    }

    [Fact]
    public void TryParse_TruncatedMidStringKey_ReturnsNull()
    {
        // Arrange: truncated mid-key — key without value is unrecoverable
        var response = "[TOOL_CALL: {\"name\": \"read_file\", \"argu";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert: cannot repair — key has no value
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_TruncatedMidStringXml_RepairsAndParses()
    {
        // Arrange: XML-style marker with truncated JSON content
        var response = "<tool_call>{\"name\": \"read_file\", \"arguments\": {\"path\": \"/trun";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
    }

    [Fact]
    public void TryParse_TruncatedMidStringRawJson_RepairsAndParses()
    {
        // Arrange: raw JSON (no marker) truncated mid-string
        var response = "{\"name\": \"read_file\", \"arguments\": {\"path\": \"/some/truncat";

        // Act
        var result = ToolCallParser.TryParse(response);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Name);
    }

    [Fact]
    public void ExtractJsonBlock_TruncatedMidString_ClosesQuoteBeforeBraces()
    {
        // Arrange: truncated mid-string inside arguments
        var text = "[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/some/pa";

        // Act
        var extracted = ToolCallParser.ExtractJsonBlock(text);

        // Assert: repaired JSON ends with "}}" (the closed string quote + two closing braces)
        Assert.NotNull(extracted);
        Assert.EndsWith("\"}}", extracted);
    }

    [Fact]
    public void TryParse_CompleteJson_UnaffectedByMidStringRepair()
    {
        // Regression: well-formed JSON must still parse correctly after mid-string repair was added
        var response = """[TOOL_CALL: {"name": "write_file", "arguments": {"path": "/tmp/f.txt", "content": "hello world"}}]""";

        var result = ToolCallParser.TryParse(response);

        Assert.NotNull(result);
        Assert.Equal("write_file", result.Name);
        Assert.Equal("/tmp/f.txt", result.Arguments!["path"]);
        Assert.Equal("hello world", result.Arguments["content"]);
    }

    [Fact]
    public void IsParsableJson_ValidJson_ReturnsTrue()
    {
        Assert.True(ToolCallParser.IsParsableJson("{\"name\": \"foo\"}"));
    }

    [Fact]
    public void IsParsableJson_InvalidJson_ReturnsFalse()
    {
        Assert.False(ToolCallParser.IsParsableJson("{\"name\": \"foo\""));
    }

    // --- AC-2: TryParseAll multiple tool call extraction ---

    [Fact]
    public void TryParseAll_MultipleBracketMarkers_ExtractsAll()
    {
        // Arrange: two bracket markers in sequence
        var response = "[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/a.txt\"}}] [TOOL_CALL: {\"name\": \"write_file\", \"arguments\": {\"path\": \"/b.txt\", \"content\": \"hi\"}}]";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("read_file", results[0].Request.Name);
        Assert.Equal("write_file", results[1].Request.Name);
    }

    [Fact]
    public void TryParseAll_MixedBracketAndXml_ExtractsBoth()
    {
        // Arrange: bracket marker followed by XML block
        var response = "[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/a.txt\"}}] <tool_call>{\"name\": \"list_tools\"}</tool_call>";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Equal(2, results.Count);
        var names = results.Select(r => r.Request.Name).ToHashSet();
        Assert.Contains("read_file", names);
        Assert.Contains("list_tools", names);
    }

    [Fact]
    public void TryParseAll_SingleToolCall_ReturnsSingleItemList()
    {
        // Arrange: exactly one bracket marker
        var response = "[TOOL_CALL: {\"name\": \"ping\", \"arguments\": {}}]";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Single(results);
        Assert.Equal("ping", results[0].Request.Name);
    }

    [Fact]
    public void TryParseAll_NoToolCalls_ReturnsEmptyList()
    {
        // Arrange: plain prose with no markers
        var response = "Sure, I can help you with that. Let me explain the concept.";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void TryParseAll_EmptyString_ReturnsEmptyList()
    {
        // Act
        var results = ToolCallParser.TryParseAll("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void TryParseAll_NullString_ReturnsEmptyList()
    {
        // Act
        var results = ToolCallParser.TryParseAll(null);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void TryParseAll_DuplicateNotDoubleCounted()
    {
        // Arrange: a bracket marker whose body is also valid raw JSON — should yield count = 1
        // The bracket extraction covers positions [0, N], the raw JSON scan finds the same '{' inside
        // and IsOverlapping suppresses the duplicate.
        var response = "[TOOL_CALL: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/a.txt\"}}]";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert: exactly 1 result, not 2
        Assert.Single(results);
        Assert.Equal("read_file", results[0].Request.Name);
    }

    [Fact]
    public void TryParseAll_MultipleRawJson_ExtractsAll()
    {
        // Arrange: two raw JSON objects separated by prose
        var response = "First: {\"name\": \"read_file\", \"arguments\": {\"path\": \"/a.txt\"}} then {\"name\": \"write_file\", \"arguments\": {\"path\": \"/b.txt\", \"content\": \"x\"}}";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Equal(2, results.Count);
        var names = results.Select(r => r.Request.Name).ToHashSet();
        Assert.Contains("read_file", names);
        Assert.Contains("write_file", names);
    }

    [Fact]
    public void TryParseAll_MixedAllThreeTypes_ExtractsAll()
    {
        // Arrange: bracket + XML + raw JSON all in one response
        var response =
            "[TOOL_CALL: {\"name\": \"bracket_tool\", \"arguments\": {}}] " +
            "<tool_call>{\"name\": \"xml_tool\", \"arguments\": {}}</tool_call> " +
            "{\"name\": \"raw_tool\", \"arguments\": {}}";

        // Act
        var results = ToolCallParser.TryParseAll(response);

        // Assert
        Assert.Equal(3, results.Count);
        var names = results.Select(r => r.Request.Name).ToHashSet();
        Assert.Contains("bracket_tool", names);
        Assert.Contains("xml_tool", names);
        Assert.Contains("raw_tool", names);
    }
}
