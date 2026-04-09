using System.Text.Json;
using Nexus.Connectors;
using Nexus.Core.Abstractions;

namespace Nexus.Core.Tests;

public class SchemaValidatorTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ToolRegistry CreateRegistryWithTool(string name, string? schemaJson)
    {
        var registry = new ToolRegistry(logger: null);

        if (schemaJson is not null)
        {
            var schema = JsonDocument.Parse(schemaJson).RootElement.Clone();
            registry.RegisterTool(new ToolDefinition
            {
                Name = name,
                Description = "test tool",
                ServerName = "test-server",
                InputSchema = schema
            });
        }

        return registry;
    }

    private static SchemaValidator CreateValidator(ToolRegistry registry, bool coercionEnabled = true) =>
        new(registry, coercionEnabled);

    // ---------------------------------------------------------------------------
    // AC-1: Missing required arguments produce actionable errors
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_MissingRequiredArg_ReturnsActionableError()
    {
        // Arrange — write_file requires path AND content; only path is supplied
        const string schema = """
            {
                "type": "object",
                "required": ["path", "content"],
                "properties": {
                    "path":    { "type": "string" },
                    "content": { "type": "string" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("write_file", schema);
        var validator = CreateValidator(registry);
        var args = new Dictionary<string, object> { ["path"] = "/tmp/out.txt" };

        // Act
        var result = validator.Validate("write_file", args);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("content", result.Errors[0]);
        Assert.Contains("REQUIRED", result.Errors[0]);
    }

    [Fact]
    public void Validate_AllRequiredPresent_ReturnsOk()
    {
        // Arrange
        const string schema = """
            {
                "type": "object",
                "required": ["path", "content"],
                "properties": {
                    "path":    { "type": "string" },
                    "content": { "type": "string" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("write_file", schema);
        var validator = CreateValidator(registry);
        var args = new Dictionary<string, object>
        {
            ["path"]    = "/tmp/out.txt",
            ["content"] = "hello world"
        };

        // Act
        var result = validator.Validate("write_file", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NullArgsWithRequiredFields_ReturnsError()
    {
        // Arrange — tool requires path but args is null
        const string schema = """
            {
                "type": "object",
                "required": ["path"],
                "properties": {
                    "path": { "type": "string" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("read_file", schema);
        var validator = CreateValidator(registry);

        // Act
        var result = validator.Validate("read_file", null);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("path", result.Errors[0]);
    }

    [Fact]
    public void Validate_NullArgsNoRequiredFields_ReturnsOk()
    {
        // Arrange — schema has optional properties only, args null → valid
        const string schema = """
            {
                "type": "object",
                "properties": {
                    "limit": { "type": "number" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("list_files", schema);
        var validator = CreateValidator(registry);

        // Act
        var result = validator.Validate("list_files", null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.CoercedArgs);
    }

    // ---------------------------------------------------------------------------
    // AC-2: Type coercion
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_CoerceStringTrue_ToBool()
    {
        // Arrange — schema expects boolean, LLM sends string "true"
        const string schema = """
            {
                "type": "object",
                "required": ["x"],
                "properties": {
                    "x": { "type": "boolean" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("toggle", schema);
        var validator = CreateValidator(registry, coercionEnabled: true);
        var args = new Dictionary<string, object> { ["x"] = "true" };

        // Act
        var result = validator.Validate("toggle", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.CoercedArgs);
        Assert.IsType<bool>(result.CoercedArgs!["x"]);
        Assert.True((bool)result.CoercedArgs["x"]);
    }

    [Fact]
    public void Validate_CoerceStringNumber_ToNumber()
    {
        // Arrange — schema expects number, LLM sends string "42"
        const string schema = """
            {
                "type": "object",
                "required": ["x"],
                "properties": {
                    "x": { "type": "number" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("compute", schema);
        var validator = CreateValidator(registry, coercionEnabled: true);
        var args = new Dictionary<string, object> { ["x"] = "42" };

        // Act
        var result = validator.Validate("compute", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.CoercedArgs);
        Assert.IsType<double>(result.CoercedArgs!["x"]);
        Assert.Equal(42.0, (double)result.CoercedArgs["x"]);
    }

    [Fact]
    public void Validate_CoercionDisabled_WrongType_ReturnsError()
    {
        // Arrange — schema expects boolean, LLM sends string "true", coercion OFF
        const string schema = """
            {
                "type": "object",
                "required": ["x"],
                "properties": {
                    "x": { "type": "boolean" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("toggle", schema);
        var validator = CreateValidator(registry, coercionEnabled: false);
        var args = new Dictionary<string, object> { ["x"] = "true" };

        // Act
        var result = validator.Validate("toggle", args);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ---------------------------------------------------------------------------
    // AC-3: Unknown arguments silently stripped
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_UnknownArgs_SilentlyStripped()
    {
        // Arrange — schema defines only "path"; args also has "junk"
        const string schema = """
            {
                "type": "object",
                "required": ["path"],
                "properties": {
                    "path": { "type": "string" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("read_file", schema);
        var validator = CreateValidator(registry);
        var args = new Dictionary<string, object>
        {
            ["path"] = "/tmp/file.txt",
            ["junk"] = "unwanted"
        };

        // Act
        var result = validator.Validate("read_file", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.CoercedArgs);
        Assert.True(result.CoercedArgs!.ContainsKey("path"));
        Assert.False(result.CoercedArgs.ContainsKey("junk"));
    }

    // ---------------------------------------------------------------------------
    // AC-5 / pass-through scenarios
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_ToolWithoutSchema_PassesThrough()
    {
        // Arrange — tool registered with no InputSchema
        var registry = new ToolRegistry(logger: null);
        registry.RegisterTool(new ToolDefinition
        {
            Name        = "no_schema_tool",
            Description = "tool with no schema",
            ServerName  = "test-server",
            InputSchema = null
        });

        var validator = CreateValidator(registry);
        var args = new Dictionary<string, object> { ["anything"] = "goes" };

        // Act
        var result = validator.Validate("no_schema_tool", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.Same(args, result.CoercedArgs);
    }

    [Fact]
    public void Validate_ToolNotFound_PassesThrough()
    {
        // Arrange — registry is empty; unknown tool should pass through
        var registry = new ToolRegistry(logger: null);
        var validator = CreateValidator(registry);
        var args = new Dictionary<string, object> { ["x"] = "1" };

        // Act
        var result = validator.Validate("ghost_tool", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.Same(args, result.CoercedArgs);
    }

    // ---------------------------------------------------------------------------
    // AC-2: Array coercion
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_SingleValueToArray_CoercedToList()
    {
        // Arrange — schema expects array, LLM sends a plain string
        const string schema = """
            {
                "type": "object",
                "required": ["tags"],
                "properties": {
                    "tags": { "type": "array" }
                }
            }
            """;

        var registry = CreateRegistryWithTool("tag_item", schema);
        var validator = CreateValidator(registry, coercionEnabled: true);
        var args = new Dictionary<string, object> { ["tags"] = "hello" };

        // Act
        var result = validator.Validate("tag_item", args);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.CoercedArgs);
        Assert.IsAssignableFrom<IList<object>>(result.CoercedArgs!["tags"]);
        var list = (IList<object>)result.CoercedArgs["tags"];
        Assert.Single(list);
        Assert.Equal("hello", list[0]);
    }
}
