using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core.Services;

/// <summary>
/// Represents a parsed tool call request extracted from an LLM response.
/// </summary>
public record ToolCallRequest(string Name, Dictionary<string, object>? Arguments);

/// <summary>
/// Parses tool call markers from LLM responses.
/// Format: [TOOL_CALL: {"name": "tool_name", "arguments": {"param": "value"}}]
/// </summary>
public static partial class ToolCallParser
{
    [GeneratedRegex(@"\[TOOL_CALL:\s*(\{.*?\})\s*\]", RegexOptions.Singleline)]
    private static partial Regex ToolCallRegex();

    /// <summary>
    /// Attempts to parse a tool call marker from the LLM response.
    /// Returns null if no valid tool call is found. Never throws.
    /// </summary>
    public static ToolCallRequest? TryParse(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return null;

        try
        {
            var match = ToolCallRegex().Match(llmResponse);
            if (!match.Success)
                return null;

            var json = RepairUnbalancedBraces(match.Groups[1].Value);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameElement))
                return null;

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            Dictionary<string, object>? arguments = null;
            if (root.TryGetProperty("arguments", out var argsElement) &&
                argsElement.ValueKind == JsonValueKind.Object)
            {
                arguments = new Dictionary<string, object>();
                foreach (var prop in argsElement.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            return new ToolCallRequest(name, arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Repairs JSON with unbalanced braces — common with smaller LLMs that drop
    /// the outer closing brace in nested tool call arguments.
    /// </summary>
    private static string RepairUnbalancedBraces(string json)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }

        return depth > 0 ? json + new string('}', depth) : json;
    }

    /// <summary>
    /// Returns the text before the first tool call marker, or the full text if no marker is found.
    /// </summary>
    public static string GetTextBeforeToolCall(string llmResponse)
    {
        if (string.IsNullOrEmpty(llmResponse))
            return llmResponse;

        var match = ToolCallRegex().Match(llmResponse);
        if (!match.Success)
            return llmResponse;

        return llmResponse[..match.Index].TrimEnd();
    }
}
