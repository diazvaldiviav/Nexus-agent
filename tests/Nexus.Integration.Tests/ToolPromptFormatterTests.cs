using System.Text.Json;
using Nexus.Connectors;
using Nexus.Connectors.ToolFiltering;

namespace Nexus.Integration.Tests;

public class ToolPromptFormatterTests
{
    private readonly ToolComplexityClassifier _classifier = new();
    private readonly ToolPromptFormatter _formatter;

    public ToolPromptFormatterTests()
    {
        _formatter = new ToolPromptFormatter(_classifier);
    }

    // SimpleSchema — {path: string}, required: [path] → Simple (~0.23)
    private const string SimpleSchema = """
        {
          "type": "object",
          "properties": { "path": { "type": "string" } },
          "required": ["path"]
        }
        """;

    // WriteFileSchema — {path, content: string}, required: [path,content] → Simple (~0.46)
    private const string WriteFileSchema = """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string" },
            "content": { "type": "string" }
          },
          "required": ["path", "content"]
        }
        """;

    // ModerateSchema — {a,b,c,d: string}, required: [a,b] → Moderate (~0.62)
    private const string ModerateSchema = """
        {
          "type": "object",
          "properties": {
            "a": { "type": "string" },
            "b": { "type": "string" },
            "c": { "type": "string" },
            "d": { "type": "string" }
          },
          "required": ["a", "b"]
        }
        """;

    // EditFileSchema — {path, edits: [{oldText,newText}]}, required: [path,edits] → Complex (≥1.04)
    // Name "edit_file" triggers semantic name pattern (+0.15)
    private const string EditFileSchema = """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Path to the file" },
            "edits": {
              "type": "array",
              "description": "List of edits",
              "items": {
                "type": "object",
                "properties": {
                  "oldText": { "type": "string" },
                  "newText": { "type": "string" }
                },
                "required": ["oldText", "newText"]
              }
            },
            "dryRun": { "type": "boolean" }
          },
          "required": ["path", "edits"]
        }
        """;

    // ComplexNonOverrideSchema — {items: [{id, value}], label}, required: [items] → Complex (≥0.80)
    // Description keyword "array of objects" pushes score via semantic detection
    private const string ComplexNonOverrideSchema = """
        {
          "type": "object",
          "properties": {
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string" },
                  "value": { "type": "string" }
                }
              }
            },
            "label": { "type": "string" }
          },
          "required": ["items"]
        }
        """;

    private static ToolDefinition MakeTool(
        string name, string? schemaJson, string server = "fs", string desc = "")
    {
        var tool = new ToolDefinition { Name = name, Description = desc, ServerName = server };
        if (schemaJson is not null)
            tool.InputSchema = JsonDocument.Parse(schemaJson).RootElement;
        return tool;
    }

    // -------------------------------------------------------------------------
    // 1. Full tier — output identical to ToolRegistry.GetToolDefinitionsForPrompt
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_FullTier_IdenticalToToolRegistry()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterToolsFromServer("fs", new List<ToolDefinition>
        {
            new() { Name = "read_text_file", Description = "" },
            new() { Name = "write_file",     Description = "" },
            new() { Name = "edit_file",      Description = "" },
        });

        // Patch schemas after registration (RegisterToolsFromServer sets ServerName)
        registry.Tools["read_text_file"].InputSchema = JsonDocument.Parse(SimpleSchema).RootElement;
        registry.Tools["write_file"].InputSchema     = JsonDocument.Parse(WriteFileSchema).RootElement;
        registry.Tools["edit_file"].InputSchema      = JsonDocument.Parse(EditFileSchema).RootElement;

        var expected = registry.GetToolDefinitionsForPrompt();

        // Act
        var actual = _formatter.Format(registry.Tools.Values, null);

        // Assert
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // 2. Limited tier — complex tool excluded, simple tools present
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_ExcludesComplexTool()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("write_file",     WriteFileSchema, server: "fs"),
            MakeTool("edit_file",      EditFileSchema,  server: "fs", desc: "Edit a file"),
            MakeTool("read_text_file", SimpleSchema,    server: "fs"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Contains("- write_file:", result);
        Assert.Contains("- read_text_file:", result);
        Assert.DoesNotContain("- edit_file:", result);
    }

    // -------------------------------------------------------------------------
    // 3. Limited tier — edit_file excluded → workflow override in footer
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_EditFileExcluded_WorkflowOverrideInFooter()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("write_file",     WriteFileSchema, server: "fs"),
            MakeTool("edit_file",      EditFileSchema,  server: "fs", desc: "Edit a file"),
            MakeTool("read_text_file", SimpleSchema,    server: "fs"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Contains(
            "Tool 'edit_file' hidden. Recommended workflow: read_text_file → modify content → write_file",
            result);
    }

    // -------------------------------------------------------------------------
    // 4. Capable tier — complex tool included with nested-args hint
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_CapableTier_ComplexToolIncludedWithHint()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("write_file", WriteFileSchema),
            MakeTool("edit_file",  EditFileSchema, desc: "Edit a file"),
        };

        // Act
        var result = _formatter.Format(tools, "mistral:7b");

        // Assert
        Assert.Contains("- edit_file:", result);
        Assert.Contains("(This tool takes nested arguments — double-check your JSON.)", result);
    }

    // -------------------------------------------------------------------------
    // 5. Empty tool list → returns empty string
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_EmptyToolList_ReturnsEmpty()
    {
        // Arrange
        var tools = Array.Empty<ToolDefinition>();

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    // -------------------------------------------------------------------------
    // 6. Limited tier — dynamic alternatives from same server listed
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_DynamicAlternatives_ListsSimpleFromSameServer()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("github_create_issue", SimpleSchema,              server: "github"),
            MakeTool("github_bulk_update",  ComplexNonOverrideSchema,  server: "github",
                     desc: "Apply an array of objects of updates"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.DoesNotContain("- github_bulk_update:", result);
        Assert.Contains(
            "Tool 'github_bulk_update' hidden. Simple tools from same server: github_create_issue",
            result);
    }

    // -------------------------------------------------------------------------
    // 7. Limited tier — no alternatives → not-available message
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_NoAlternatives_NotAvailableMessage()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("some_complex_tool", ComplexNonOverrideSchema, server: "isolated",
                     desc: "Apply an array of objects"),
            MakeTool("read_file", SimpleSchema, server: "fs"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Contains(
            "Tool 'some_complex_tool' is not available for this model due to complex arguments.",
            result);
    }

    // -------------------------------------------------------------------------
    // 8. Limited tier — workflow override takes priority over same-server hint
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_WorkflowOverrideTakesPriority()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("edit_file",      EditFileSchema,  server: "fs", desc: "Edit a file"),
            MakeTool("read_text_file", SimpleSchema,    server: "fs"),
            MakeTool("write_file",     WriteFileSchema, server: "fs"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Contains("Recommended workflow", result);
        Assert.DoesNotContain("Simple tools from same server", result);
    }

    // -------------------------------------------------------------------------
    // 9. Limited tier — moderate tool included with hint
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_LimitedTier_ModerateToolIncludedWithHint()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("flexible_tool", ModerateSchema),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:1.7b");

        // Assert
        Assert.Contains("- flexible_tool:", result);
        Assert.Contains("(Prefer simpler alternatives when possible.)", result);
    }

    // -------------------------------------------------------------------------
    // 10. Capable tier — moderate tool included without hint
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_CapableTier_ModerateToolNoHint()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("flexible_tool", ModerateSchema),
        };

        // Act
        var result = _formatter.Format(tools, "mistral:7b");

        // Assert
        Assert.Contains("- flexible_tool:", result);
        Assert.DoesNotContain("(Prefer simpler alternatives when possible.)", result);
    }

    // -------------------------------------------------------------------------
    // 11. Full tier — no hints, no exclusions for any complexity tier
    // -------------------------------------------------------------------------

    [Fact]
    public void Format_FullTier_NoHintsNoExclusions()
    {
        // Arrange
        var tools = new[]
        {
            MakeTool("read_text_file", SimpleSchema),
            MakeTool("flexible_tool",  ModerateSchema),
            MakeTool("edit_file",      EditFileSchema, desc: "Edit a file"),
        };

        // Act
        var result = _formatter.Format(tools, "qwen3:14b");

        // Assert
        Assert.Contains("- read_text_file:", result);
        Assert.Contains("- flexible_tool:", result);
        Assert.Contains("- edit_file:", result);
        Assert.DoesNotContain("Prefer simpler", result);
        Assert.DoesNotContain("double-check your JSON", result);
        Assert.DoesNotContain("hidden", result);
    }
}
