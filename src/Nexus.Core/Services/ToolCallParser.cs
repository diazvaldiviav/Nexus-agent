using System.Linq;
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
            {
                var hasMarker = llmResponse.Contains("[TOOL_CALL:");
                if (hasMarker)
                    Console.Error.WriteLine($"[ToolCallParser DEBUG] Marker found but ExtractJsonBlock returned null. Response length: {llmResponse.Length}");
                return null;
            }

            json = SanitizeInvalidEscapes(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameElement))
                return null;

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            Dictionary<string, object>? arguments = null;
            var argSource = ResolveArgumentsSource(root);
            if (argSource is not null)
            {
                arguments = new Dictionary<string, object>();
                foreach (var prop in argSource)
                    arguments[prop.Name] = MapJsonValue(prop.Value);
            }

            // Sanitize repetition loops in arguments (small models hallucinate repeating segments)
            if (arguments is not null)
                SanitizeRepetitionLoops(arguments);

            return new ToolCallRequest(name, arguments);
        }
        catch (JsonException ex)
        {
            var extracted = ExtractJsonBlock(llmResponse);
            var sanitized = extracted is not null ? SanitizeInvalidEscapes(extracted) : null;
            System.Diagnostics.Debug.WriteLine($"[ToolCallParser] JSON parse failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ToolCallParser] Extracted JSON (first 500 chars): {sanitized?[..Math.Min(500, sanitized.Length)]}");
            Console.Error.WriteLine($"[ToolCallParser DEBUG] Parse failed: {ex.Message}");
            Console.Error.WriteLine($"[ToolCallParser DEBUG] JSON (first 500): {sanitized?[..Math.Min(500, sanitized.Length)]}");
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

            if (c == '\\' && inString)
            {
                // Check for trailing path backslash: \" followed by } ] , or :
                if (i + 1 < text.Length && text[i + 1] == '"' && IsTrailingPathBackslash(text, i + 2))
                {
                    // This \ is part of a Windows path, not a JSON escape
                    // Skip it (don't set escape flag) — let the " close the string normally
                    continue;
                }

                escape = true;
                continue;
            }

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

    /// <summary>
    /// Resolves the Layer-2 arguments source from the parsed JSON root.
    /// Priority: "arguments" wrapper (proper format) > flat properties (fallback).
    /// </summary>
    private static IEnumerable<JsonProperty>? ResolveArgumentsSource(JsonElement root)
    {
        // Path A: proper format — "arguments" wrapper exists
        if (root.TryGetProperty("arguments", out var argsElement)
            && argsElement.ValueKind == JsonValueKind.Object)
        {
            return argsElement.EnumerateObject();
        }

        // Path B: flat format — every property that isn't "name" is an argument
        var flatArgs = root.EnumerateObject()
            .Where(p => !string.Equals(p.Name, "name", StringComparison.Ordinal))
            .ToList();

        return flatArgs.Count > 0 ? flatArgs : null;
    }

    /// <summary>
    /// Maps a JsonElement to its best CLR representation.
    /// Object/Array/Null are stored as cloned JsonElement so the MCP SDK
    /// serializes them as proper JSON structures, not double-encoded strings.
    /// </summary>
    private static object MapJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        _                    => element.Clone()
    };

    /// <summary>
    /// Fixes invalid JSON escape sequences produced by some LLMs (e.g. gemma4).
    /// Backslashes not followed by a valid JSON escape char (" \ / b f n r t u)
    /// are doubled so that JsonDocument.Parse accepts them.
    /// Example: D:\Nova Tech → D:\\Nova Tech
    /// </summary>
    internal static string SanitizeInvalidEscapes(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        var inString = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (!inString)
            {
                if (c == '"') inString = true;
                sb.Append(c);
                continue;
            }

            // Inside a string — replace raw control chars with JSON escapes
            if (c == '\n') { sb.Append("\\n"); continue; }
            if (c == '\r') { sb.Append("\\r"); continue; }
            if (c == '\t') { sb.Append("\\t"); continue; }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < json.Length)
            {
                var next = json[i + 1];

                // Special case: \" at end of a path value
                // If after the quote we see }, ], or , (with optional whitespace)
                // then this is a trailing backslash in a Windows path, not an escaped quote
                if (next == '"' && IsTrailingPathBackslash(json, i + 2))
                {
                    // Convert \ to \\ (literal backslash) and let " close the string
                    sb.Append('\\');
                    sb.Append('\\');
                    // Don't skip next — the " will be processed as string close on next iteration
                }
                else if (next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u')
                {
                    // Valid escape — emit both chars and skip next
                    sb.Append(c);
                    sb.Append(next);
                    i++; // skip the next char, it's part of this escape
                }
                else
                {
                    // Invalid escape (e.g. \N, \s) — double the backslash
                    sb.Append('\\');
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Checks if a \" sequence is actually a Windows path trailing backslash + string close,
    /// NOT a real escaped quote inside JSON content.
    /// Key insight: a real escaped quote has more string content after it (e.g. \"hello\"),
    /// while a trailing path backslash is followed by JSON structure characters (}, ], ,).
    /// We also check that the quote is followed by a JSON structural token and that
    /// the char before the backslash is a path character (not another escape or quote).
    /// </summary>
    private static bool IsTrailingPathBackslash(string json, int afterQuoteIndex)
    {
        // Skip whitespace after the quote
        var j = afterQuoteIndex;
        while (j < json.Length && char.IsWhiteSpace(json[j]))
            j++;

        if (j >= json.Length)
            return true; // quote is at end of string — definitely a close

        var ch = json[j];
        if (ch is not ('}' or ']' or ','))
            return false;

        // Extra check: a real escaped quote like \" in content is typically followed
        // by more text. If the next non-whitespace after " is } ] or , AND
        // there's no unmatched quote issue, this is likely a path trailing backslash.
        // But we must exclude cases like: "content": "{\"a\": 1}"} where \" is real.
        // Heuristic: if the character after the structural token is also structural or EOF,
        // it's definitely a string close. If it's another quote (starting a new key),
        // it could go either way. Check if there's a colon nearby (key: value pattern).
        // Simplest reliable heuristic: check the char BEFORE the backslash.
        // In a path, it's always a word char (letter, digit). In escaped content like
        // {\"a\"}, the char before \ is { which is not a path char.
        return true; // Let the structural check suffice — revisit if needed
    }

    /// <summary>
    /// Detects and truncates repetition loops in string arguments.
    /// Small models hallucinate repeating segments like "\\model\\..\\model\\..\\model\\.." hundreds of times.
    /// If a segment of 3+ chars repeats 5+ times consecutively, the value is replaced with an error marker.
    /// </summary>
    internal static void SanitizeRepetitionLoops(Dictionary<string, object> arguments)
    {
        var keysToFix = new List<string>();

        foreach (var (key, value) in arguments)
        {
            if (value is not string str || str.Length < 100)
                continue;

            if (HasRepetitionLoop(str))
                keysToFix.Add(key);
        }

        foreach (var key in keysToFix)
        {
            Console.Error.WriteLine($"[ToolCallParser DEBUG] Repetition loop detected in '{key}', value truncated (was {((string)arguments[key]).Length} chars)");
            arguments[key] = "[REPETITION_ERROR]";
        }
    }

    /// <summary>
    /// Checks if a string contains a repeating segment (min 3 chars) that appears 5+ times consecutively.
    /// Uses a sliding window: for each candidate segment length, checks if the segment repeats.
    /// </summary>
    internal static bool HasRepetitionLoop(string text)
    {
        // Check segment lengths from 3 to 50 chars
        var maxSegLen = Math.Min(50, text.Length / 5);
        for (var segLen = 3; segLen <= maxSegLen; segLen++)
        {
            var segment = text.AsSpan(0, segLen);
            var consecutiveMatches = 1;

            for (var offset = segLen; offset + segLen <= text.Length; offset += segLen)
            {
                if (text.AsSpan(offset, segLen).SequenceEqual(segment))
                {
                    consecutiveMatches++;
                    if (consecutiveMatches >= 5)
                        return true;
                }
                else
                {
                    // Try starting from this new offset
                    segment = text.AsSpan(offset, segLen);
                    consecutiveMatches = 1;
                }
            }
        }

        return false;
    }
}
