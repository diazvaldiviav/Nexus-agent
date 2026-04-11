using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;

namespace Nexus.Connectors;

/// <summary>
/// Validates tool call arguments against the tool's InputSchema before MCP invocation.
/// Catches missing required arguments, type mismatches, and junk parameters so that
/// small/local LLMs receive actionable self-correction feedback.
/// </summary>
public sealed class SchemaValidator : ISchemaValidator
{
    private readonly ToolRegistry _registry;
    private readonly bool _typeCoercionEnabled;
    private readonly ILogger<SchemaValidator>? _logger;

    public SchemaValidator(ToolRegistry registry, bool typeCoercionEnabled, ILogger<SchemaValidator>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _typeCoercionEnabled = typeCoercionEnabled;
        _logger = logger;
    }

    public SchemaValidationResult Validate(string toolName, Dictionary<string, object>? args)
    {
        var tool = _registry.GetTool(toolName);

        // Unknown tool or tool with no schema — pass through; tool executor will handle errors
        if (tool is null || !tool.InputSchema.HasValue)
            return SchemaValidationResult.Ok(args);

        var schema = tool.InputSchema.Value;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var reqArray) &&
            reqArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArray.EnumerateArray())
            {
                var name = item.GetString();
                if (name is not null)
                    required.Add(name);
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties) &&
                            properties.ValueKind == JsonValueKind.Object;

        // Build the param list once (used in all "missing required" error messages)
        var paramList = required.Count > 0
            ? (hasProperties
                ? BuildParamList(properties, required)
                : string.Join(", ", required.Select(r => $"{r} (REQUIRED)")))
            : "";

        // No args supplied
        if (args is null)
        {
            if (required.Count > 0)
            {
                var errors = required.Select(r =>
                    $"Missing required argument '{r}'. {toolName} requires: {paramList}").ToList();
                return SchemaValidationResult.Fail(errors);
            }
            return SchemaValidationResult.Ok(null);
        }

        var coercedArgs = new Dictionary<string, object>(args, StringComparer.Ordinal);
        var validationErrors = new List<string>();

        // Required presence check
        foreach (var name in required)
        {
            if (!coercedArgs.ContainsKey(name))
            {
                validationErrors.Add($"Missing required argument '{name}'. {toolName} requires: {paramList}");
            }
        }

        if (validationErrors.Count > 0)
            return SchemaValidationResult.Fail(validationErrors);

        // Type check + coercion for known properties
        if (hasProperties)
        {
            foreach (var key in coercedArgs.Keys.ToList())
            {
                if (!TryGetProperty(properties, key, out var propSchema))
                    continue;

                if (!propSchema.TryGetProperty("type", out var typeEl))
                    continue;

                var expectedType = typeEl.GetString();
                if (expectedType is null)
                    continue;

                var actualType = GetJsonSchemaType(coercedArgs[key]);

                if (actualType == expectedType)
                    continue;

                // Types don't match — attempt coercion or record error
                if (_typeCoercionEnabled)
                {
                    var (success, result) = TryCoerce(coercedArgs[key], expectedType);
                    if (success)
                    {
                        coercedArgs[key] = result!;
                    }
                    else
                    {
                        validationErrors.Add(
                            $"Argument '{key}' has wrong type: expected '{expectedType}', got '{actualType}'. " +
                            $"Could not coerce value to '{expectedType}'.");
                    }
                }
                else
                {
                    validationErrors.Add(
                        $"Argument '{key}' has wrong type: expected '{expectedType}', got '{actualType}'.");
                }
            }

            // Strip unknown arguments (keys not in schema properties)
            foreach (var key in coercedArgs.Keys.ToList())
            {
                if (!TryGetProperty(properties, key, out _))
                {
                    _logger?.LogDebug("[SchemaValidator] Stripping unknown argument '{Key}' from tool '{Tool}'", key, toolName);
                    coercedArgs.Remove(key);
                }
            }
        }

        return validationErrors.Count > 0
            ? SchemaValidationResult.Fail(validationErrors)
            : SchemaValidationResult.Ok(coercedArgs);
    }

    private static bool TryGetProperty(JsonElement properties, string key, out JsonElement value)
    {
        foreach (var prop in properties.EnumerateObject())
        {
            if (string.Equals(prop.Name, key, StringComparison.Ordinal))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string GetJsonSchemaType(object value)
    {
        return value switch
        {
            string  => "string",
            bool    => "boolean",
            int     => "number",
            long    => "number",
            double  => "number",
            float   => "number",
            IList   => "array",
            JsonElement el => el.ValueKind switch
            {
                JsonValueKind.String    => "string",
                JsonValueKind.Number    => "number",
                JsonValueKind.True      => "boolean",
                JsonValueKind.False     => "boolean",
                JsonValueKind.Array     => "array",
                JsonValueKind.Object    => "object",
                JsonValueKind.Null      => "null",
                JsonValueKind.Undefined => "null",
                _                       => "null"
            },
            null => "null",
            _    => "object"
        };
    }

    private static (bool success, object? result) TryCoerce(object value, string targetType)
    {
        switch (targetType)
        {
            case "boolean":
            {
                var str = value is JsonElement je && je.ValueKind == JsonValueKind.String
                    ? je.GetString()
                    : value as string;
                if (str is not null && bool.TryParse(str, out var b))
                    return (true, b);
                return (false, null);
            }

            case "number":
            case "integer":
            {
                var str = value is JsonElement je && je.ValueKind == JsonValueKind.String
                    ? je.GetString()
                    : value as string;
                if (str is not null && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    return (true, d);
                return (false, null);
            }

            case "array":
            {
                if (value is IList || value is Array)
                    return (true, value);
                if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
                    return (true, value);
                return (true, new List<object> { value });
            }

            default:
                return (false, null);
        }
    }

    private static string BuildParamList(JsonElement properties, HashSet<string> required)
    {
        var parts = new List<string>();
        foreach (var prop in properties.EnumerateObject())
        {
            var paramType = prop.Value.TryGetProperty("type", out var t)
                ? t.GetString() ?? "any"
                : "any";
            var reqTag = required.Contains(prop.Name) ? "REQUIRED" : "optional";
            parts.Add($"{prop.Name} ({paramType}, {reqTag})");
        }
        return string.Join(", ", parts);
    }
}
