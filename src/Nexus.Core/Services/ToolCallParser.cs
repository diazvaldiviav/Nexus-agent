using System.Text.Json;

namespace Nexus.Core.Services;

/// <summary>
/// Represents a parsed tool call request extracted from an LLM response.
/// </summary>
public record ToolCallRequest(string Name, Dictionary<string, object>? Arguments);

/// <summary>
/// Parses tool call markers from LLM responses using structural JSON extraction.
/// Format: [TOOL_CALL: {"name": "tool_name", "arguments": {"param": "value"}}]
///
/// Instead of regex, uses a brace-depth state machine that tracks string literals
/// and escape sequences to extract the complete JSON object regardless of nested
/// braces, escaped quotes, or special characters inside string values.
/// </summary>
public static class ToolCallParser
{
    private const string Marker = "[TOOL_CALL:";

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
            var json = ExtractJsonBlock(llmResponse);
            if (json is null)
                return null;

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
    /// Returns the text before the first tool call marker, or the full text if no marker is found.
    /// </summary>
    public static string GetTextBeforeToolCall(string llmResponse)
    {
        if (string.IsNullOrEmpty(llmResponse))
            return llmResponse;

        var markerIndex = llmResponse.IndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return llmResponse;

        return llmResponse[..markerIndex].TrimEnd();
    }

    /// <summary>
    /// Structural JSON extractor: finds [TOOL_CALL: then walks forward to the
    /// opening '{', tracks brace depth respecting JSON string literals and escape
    /// sequences, and returns the complete JSON object. If the LLM dropped the
    /// outer closing brace (common with smaller models), appends the missing braces.
    /// </summary>
    internal static string? ExtractJsonBlock(string text)
    {
        var markerIndex = text.IndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        // Find the opening brace after the marker
        var searchStart = markerIndex + Marker.Length;
        var openBrace = -1;
        for (var i = searchStart; i < text.Length; i++)
        {
            if (text[i] == '{') { openBrace = i; break; }
            if (!char.IsWhiteSpace(text[i])) return null; // unexpected character before '{'
        }

        if (openBrace < 0)
            return null;

        // State machine: walk the JSON tracking brace depth inside/outside strings
        var depth = 0;
        var inString = false;
        var escape = false;
        var endBrace = -1;

        for (var i = openBrace; i < text.Length; i++)
        {
            var c = text[i];

            if (escape) { escape = false; continue; }

            if (c == '\\' && inString) { escape = true; continue; }

            if (c == '"') { inString = !inString; continue; }

            if (inString) continue;

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    endBrace = i;
                    break;
                }
            }
        }

        if (endBrace >= 0)
        {
            // Complete JSON object found
            return text[openBrace..(endBrace + 1)];
        }

        // LLM dropped closing brace(s) — repair by appending missing braces
        if (depth > 0)
        {
            var partial = text[openBrace..];
            // Trim any trailing ']' or whitespace the LLM may have added after the incomplete JSON
            var trimmed = partial.TrimEnd();
            if (trimmed.EndsWith(']'))
                trimmed = trimmed[..^1].TrimEnd();

            return trimmed + new string('}', depth);
        }

        return null;
    }
}
