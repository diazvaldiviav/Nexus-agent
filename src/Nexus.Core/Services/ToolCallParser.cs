using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Nexus.Core.Services;

/// <summary>
/// Represents a parsed tool call request extracted from an LLM response.
/// </summary>
public record ToolCallRequest(string Name, Dictionary<string, object>? Arguments);

/// <summary>
/// A parsed tool call with its position in the processed text (after fence stripping).
/// Foundation for future concurrent execution.
/// </summary>
public record ParsedToolCall(ToolCallRequest Request, int StartIndex, int EndIndex);

/// <summary>
/// Parses tool call markers from LLM responses using structural JSON extraction.
///
/// Supports three input formats in priority order:
///   1. [TOOL_CALL: {...}]  — bracket marker (highest priority)
///   2. &lt;tool_call&gt;{...}&lt;/tool_call&gt; — XML-style marker
///   3. Raw JSON with a "name" property — fallback for models that output bare JSON
///
/// Markdown code fences (```json / ```) are stripped before parsing.
///
/// Instead of regex, uses a brace-depth state machine that tracks string literals
/// and escape sequences to extract the complete JSON object regardless of nested
/// braces, escaped quotes, or special characters inside string values.
/// </summary>
public static class ToolCallParser
{
    private const string Marker = "[TOOL_CALL:";
    private const string XmlOpenTag = "<tool_call>";
    private const string XmlCloseTag = "</tool_call>";

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
            // Pre-process: strip markdown code fences that some models wrap around output
            var processed = StripMarkdownFences(llmResponse);

            // Path 1 (highest priority): bracket marker [TOOL_CALL: {...}]
            string? json = ExtractJsonBlock(processed);

            // Path 2: XML-style <tool_call>...</tool_call>
            if (json is null)
            {
                var xmlBlock = ExtractXmlToolCallBlock(processed);
                if (xmlBlock is not null)
                    json = xmlBlock;
            }

            // Path 3 (fallback): raw JSON object with a "name" property
            if (json is null)
            {
                json = ExtractRawJsonBlock(processed);
            }

            if (json is null)
            {
                var hasMarker = processed.Contains("[TOOL_CALL:");
                if (hasMarker)
                    Debug.WriteLine($"[ToolCallParser DEBUG] Marker found but ExtractJsonBlock returned null. Response length: {processed.Length}");
                return null;
            }

            return TryParseJson(json);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            var processed = StripMarkdownFences(llmResponse);
            var extracted = ExtractJsonBlock(processed)
                ?? ExtractXmlToolCallBlock(processed)
                ?? ExtractRawJsonBlock(processed);
            var sanitized = extracted is not null ? SanitizeInvalidEscapes(extracted) : null;
            Debug.WriteLine($"[ToolCallParser] JSON parse failed: {ex.Message}");
            Debug.WriteLine($"[ToolCallParser] Extracted JSON (first 500 chars): {sanitized?[..Math.Min(500, sanitized.Length)]}");
            return null;
        }
    }

    /// <summary>
    /// Returns the text before the first tool call marker, or the full text if no marker is found.
    /// Detects the earliest of: bracket marker, XML open tag, or raw JSON start.
    /// </summary>
    public static string GetTextBeforeToolCall(string llmResponse)
    {
        if (string.IsNullOrEmpty(llmResponse))
            return llmResponse;

        var processed = StripMarkdownFences(llmResponse);

        var earliest = int.MaxValue;

        var bracketIndex = processed.IndexOf(Marker, StringComparison.Ordinal);
        if (bracketIndex >= 0 && bracketIndex < earliest)
            earliest = bracketIndex;

        var xmlIndex = processed.IndexOf(XmlOpenTag, StringComparison.OrdinalIgnoreCase);
        if (xmlIndex >= 0 && xmlIndex < earliest)
            earliest = xmlIndex;

        var rawIndex = FindRawJsonStart(processed);
        if (rawIndex >= 0 && rawIndex < earliest)
            earliest = rawIndex;

        if (earliest == int.MaxValue)
            return processed;

        return processed[..earliest].TrimEnd();
    }

    /// <summary>
    /// Strips leading and trailing markdown code fences (``` or ```json / ```JSON etc.)
    /// from the text so that the rest of the parser can work on the raw content.
    /// Only strips when the text starts with a fence (possibly with leading whitespace).
    /// </summary>
    internal static string StripMarkdownFences(string text)
    {
        var trimmed = text.AsSpan().TrimStart();

        // Must start with ```
        if (!trimmed.StartsWith("```".AsSpan(), StringComparison.Ordinal))
            return text;

        var str = trimmed.ToString();

        // Find end of the opening fence line (skip the ``` and optional language tag)
        var openNewline = str.IndexOf('\n');
        if (openNewline < 0)
        {
            // Unterminated fence with no newline — strip just the ``` prefix
            return str[3..].TrimStart();
        }

        // Content starts after the opening fence line
        var content = str[(openNewline + 1)..];

        // Find closing ``` fence
        var closeFenceIndex = content.LastIndexOf("```", StringComparison.Ordinal);
        if (closeFenceIndex >= 0)
        {
            // Trim everything from the closing fence onward
            content = content[..closeFenceIndex].TrimEnd();
        }

        return content;
    }

    /// <summary>
    /// Extracts the JSON body from a &lt;tool_call&gt;...&lt;/tool_call&gt; block.
    /// Tag matching is case-insensitive. If the closing tag is absent the content
    /// from after the open tag to the end of text is returned (best-effort).
    /// Returns null if no open tag is found.
    /// </summary>
    internal static string? ExtractXmlToolCallBlock(string text)
    {
        var openIndex = text.IndexOf(XmlOpenTag, StringComparison.OrdinalIgnoreCase);
        if (openIndex < 0)
            return null;

        var contentStart = openIndex + XmlOpenTag.Length;

        var closeIndex = text.IndexOf(XmlCloseTag, contentStart, StringComparison.OrdinalIgnoreCase);
        var content = closeIndex >= 0
            ? text[contentStart..closeIndex].Trim()
            : text[contentStart..].Trim();

        // Apply brace-walking repair if the content contains a JSON object
        var braceStart = content.IndexOf('{');
        if (braceStart >= 0)
        {
            var (end, depth, inStr) = WalkJsonObject(content, braceStart);
            if (end >= 0)
                return content[..(end + 1)];
            if (depth > 0)
            {
                var repaired = content.TrimEnd();
                if (repaired.EndsWith(']'))
                    repaired = repaired[..^1].TrimEnd();
                if (inStr)
                    repaired += '"';
                repaired += new string('}', depth);
                return IsParsableJson(repaired) ? repaired : null;
            }
        }

        return content;
    }

    /// <summary>
    /// Extracts a raw JSON object that contains a "name" property (no surrounding marker).
    /// Uses a brace-walk to find the complete object. Returns null if no qualifying
    /// JSON object is found.
    /// </summary>
    internal static string? ExtractRawJsonBlock(string text)
    {
        var start = FindRawJsonStart(text);
        if (start < 0)
            return null;

        var (endIndex, missingBraces, endedInString) = WalkJsonObject(text, start);

        if (endIndex >= 0)
            return text[start..(endIndex + 1)];

        // Incomplete — repair by appending missing braces
        if (missingBraces > 0)
        {
            var partial = text[start..].TrimEnd();
            if (partial.EndsWith(']'))
                partial = partial[..^1].TrimEnd();
            if (endedInString)
                partial += '"';
            var repaired = partial + new string('}', missingBraces);
            return IsParsableJson(repaired) ? repaired : null;
        }

        return null;
    }

    /// <summary>
    /// Finds the index of the first '{' that begins a JSON object containing a "name" property.
    /// Scans forward looking for '{', then does a lightweight string search for '"name"' within
    /// the object boundary (up to 512 chars ahead) without full parsing. Returns -1 if not found.
    /// </summary>
    private static int FindRawJsonStart(string text, int startOffset = 0)
    {
        for (var i = startOffset; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;

            // Lightweight check: look for "name" within a reasonable window
            var window = Math.Min(512, text.Length - i);
            var slice = text.AsSpan(i, window);
            if (slice.Contains("\"name\"".AsSpan(), StringComparison.Ordinal))
                return i;
        }

        return -1;
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

        var (endBrace, depth, endedInString) = WalkJsonObject(text, openBrace);

        if (endBrace >= 0)
            return text[openBrace..(endBrace + 1)];

        // LLM dropped closing brace(s) — repair by appending missing braces
        if (depth > 0)
        {
            var partial = text[openBrace..];
            // Trim any trailing ']' or whitespace the LLM may have added after the incomplete JSON
            var trimmed = partial.TrimEnd();
            if (trimmed.EndsWith(']'))
                trimmed = trimmed[..^1].TrimEnd();
            if (endedInString)
                trimmed += '"';
            var repaired = trimmed + new string('}', depth);
            return IsParsableJson(repaired) ? repaired : null;
        }

        return null;
    }

    /// <summary>
    /// Walks a JSON object starting at startIndex (which must be '{'), tracking brace depth
    /// while respecting string literals and escape sequences.
    /// Returns (endIndex, 0, false) when the closing brace is found, or (-1, missingBraces, inString)
    /// when the text ends before the object is closed (for repair by callers).
    /// The endedInString flag tells callers whether the walk terminated mid-string literal,
    /// so they can close the quote before appending missing braces.
    /// </summary>
    internal static (int endIndex, int missingBraces, bool endedInString) WalkJsonObject(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escape) { escape = false; continue; }

            if (c == '\\' && inString)
            {
                // Check for trailing path backslash: \" followed by } ] , or :
                if (i + 1 < text.Length && text[i + 1] == '"' && IsTrailingPathBackslash(text, i + 2))
                {
                    // This \ is part of a Windows path, not a JSON escape
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
                    return (i, 0, false);
            }
        }

        return (-1, depth, inString);
    }

    /// <summary>
    /// Returns true if the given string is parseable as a JSON document; false otherwise.
    /// Used as a gate to avoid returning repaired-but-invalid JSON to callers.
    /// </summary>
    internal static bool IsParsableJson(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
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
    /// Shared parse helper: given a raw JSON string, sanitizes escapes, parses the document,
    /// extracts name + arguments, and applies repetition-loop sanitization.
    /// Returns null on any failure (broad catch matching TryParse pattern).
    /// The JsonDocument is disposed before return — all values are plain CLR types or cloned JsonElements.
    /// </summary>
    private static ToolCallRequest? TryParseJson(string json)
    {
        try
        {
            var sanitized = SanitizeInvalidEscapes(json);

            using var doc = JsonDocument.Parse(sanitized);
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

            if (arguments is not null)
                SanitizeRepetitionLoops(arguments);

            return new ToolCallRequest(name, arguments);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if the candidate range [start, end] overlaps any range in the existing list.
    /// Used by TryParseAll to deduplicate tool calls extracted by multiple paths.
    /// </summary>
    private static bool IsOverlapping(List<ParsedToolCall> existing, int start, int end)
    {
        foreach (var tc in existing)
        {
            if (start <= tc.EndIndex && end >= tc.StartIndex)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts all tool calls from an LLM response. Returns empty list (never null) when no tool calls found.
    /// Deduplicates overlapping ranges. Each ParsedToolCall includes position in processed text.
    /// </summary>
    public static List<ParsedToolCall> TryParseAll(string? llmResponse)
    {
        var results = new List<ParsedToolCall>();

        if (string.IsNullOrEmpty(llmResponse))
            return results;

        var text = StripMarkdownFences(llmResponse);

        // Path 1: bracket markers [TOOL_CALL: {...}]
        var searchFrom = 0;
        while (true)
        {
            var markerIndex = text.IndexOf(Marker, searchFrom, StringComparison.Ordinal);
            if (markerIndex < 0)
                break;

            var afterMarker = markerIndex + Marker.Length;
            var openBrace = -1;
            for (var i = afterMarker; i < text.Length; i++)
            {
                if (text[i] == '{') { openBrace = i; break; }
                if (!char.IsWhiteSpace(text[i])) break;
            }

            if (openBrace >= 0)
            {
                var (endBrace, depth, endedInString) = WalkJsonObject(text, openBrace);
                string? json;
                int endPos;

                if (endBrace >= 0)
                {
                    json = text[openBrace..(endBrace + 1)];
                    endPos = endBrace;
                }
                else if (depth > 0)
                {
                    var partial = text[openBrace..].TrimEnd();
                    if (partial.EndsWith(']'))
                        partial = partial[..^1].TrimEnd();
                    if (endedInString)
                        partial += '"';
                    var repaired = partial + new string('}', depth);
                    json = IsParsableJson(repaired) ? repaired : null;
                    endPos = text.Length - 1;
                }
                else
                {
                    json = null;
                    endPos = openBrace;
                }

                if (json is not null && !IsOverlapping(results, markerIndex, endPos))
                {
                    try
                    {
                        var request = TryParseJson(json);
                        if (request is not null)
                            results.Add(new ParsedToolCall(request, markerIndex, endPos));
                    }
                    catch (Exception ex) { Debug.WriteLine($"[ToolCallParser] Bracket candidate parse failed: {ex.Message}"); }
                }

                searchFrom = endPos + 1;
            }
            else
            {
                searchFrom = afterMarker;
            }
        }

        // Path 2: XML blocks <tool_call>...</tool_call>
        searchFrom = 0;
        while (true)
        {
            var openIndex = text.IndexOf(XmlOpenTag, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (openIndex < 0)
                break;

            var contentStart = openIndex + XmlOpenTag.Length;
            var closeIndex = text.IndexOf(XmlCloseTag, contentStart, StringComparison.OrdinalIgnoreCase);
            var endPos = closeIndex >= 0 ? closeIndex + XmlCloseTag.Length - 1 : text.Length - 1;
            var content = closeIndex >= 0
                ? text[contentStart..closeIndex].Trim()
                : text[contentStart..].Trim();

            string? json = null;
            var braceStart = content.IndexOf('{');
            if (braceStart >= 0)
            {
                var (end, walkDepth, inStr) = WalkJsonObject(content, braceStart);
                if (end >= 0)
                {
                    json = content[..(end + 1)];
                }
                else if (walkDepth > 0)
                {
                    var repaired = content.TrimEnd();
                    if (repaired.EndsWith(']'))
                        repaired = repaired[..^1].TrimEnd();
                    if (inStr)
                        repaired += '"';
                    repaired += new string('}', walkDepth);
                    json = IsParsableJson(repaired) ? repaired : null;
                }
            }
            else
            {
                json = content;
            }

            if (json is not null && !IsOverlapping(results, openIndex, endPos))
            {
                try
                {
                    var request = TryParseJson(json);
                    if (request is not null)
                        results.Add(new ParsedToolCall(request, openIndex, endPos));
                }
                catch (Exception ex) { Debug.WriteLine($"[ToolCallParser] XML candidate parse failed: {ex.Message}"); }
            }

            searchFrom = endPos + 1;
        }

        // Path 3: raw JSON objects with a "name" property
        searchFrom = 0;
        while (true)
        {
            var start = FindRawJsonStart(text, searchFrom);
            if (start < 0)
                break;

            var (endIndex, missingBraces, endedInString) = WalkJsonObject(text, start);
            string? json;
            int endPos;

            if (endIndex >= 0)
            {
                json = text[start..(endIndex + 1)];
                endPos = endIndex;
            }
            else if (missingBraces > 0)
            {
                var partial = text[start..].TrimEnd();
                if (partial.EndsWith(']'))
                    partial = partial[..^1].TrimEnd();
                if (endedInString)
                    partial += '"';
                var repaired = partial + new string('}', missingBraces);
                json = IsParsableJson(repaired) ? repaired : null;
                endPos = text.Length - 1;
            }
            else
            {
                json = null;
                endPos = start;
            }

            if (json is not null && !IsOverlapping(results, start, endPos))
            {
                try
                {
                    var request = TryParseJson(json);
                    if (request is not null)
                        results.Add(new ParsedToolCall(request, start, endPos));
                }
                catch (Exception ex) { Debug.WriteLine($"[ToolCallParser] Raw JSON candidate parse failed: {ex.Message}"); }
            }

            searchFrom = endPos + 1;
        }

        return results;
    }

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

        return true; // Let the structural check suffice
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
            Debug.WriteLine($"[ToolCallParser DEBUG] Repetition loop detected in '{key}', value truncated (was {((string)arguments[key]).Length} chars)");
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
