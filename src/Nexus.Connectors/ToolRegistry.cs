using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexus.Connectors;

public interface IToolRegistry
{
    IReadOnlyDictionary<string, ToolDefinition> Tools { get; }
    void RegisterToolsFromServer(string serverName, List<ToolDefinition> tools);
    void UnregisterToolsForServer(string serverName);
}

/// <summary>
/// Result of fuzzy tool name resolution.
/// </summary>
public record ToolResolution(ToolDefinition? Tool, string? CorrectedName, string? Error);

/// <summary>
/// Describes a tool discovered from an MCP server.
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// Registry of tools discovered from MCP servers.
/// Supports registration, lookup, and prompt formatting.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private volatile Dictionary<string, string> _lowerToCanonical = new();
    private readonly ILogger<ToolRegistry>? _logger;

    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;

    /// <summary>
    /// Registers a single tool definition.
    /// </summary>
    public void RegisterTool(ToolDefinition tool)
    {
        _tools[tool.Name] = tool;
        RebuildLowerIndex();
        _logger?.LogInformation("Registered tool: {ToolName} from server {Server}", tool.Name, tool.ServerName);
    }

    /// <summary>
    /// Registers all tools discovered from a specific MCP server.
    /// Replaces any previously registered tools from the same server.
    /// During server reconnection there is a brief window where old tools have been
    /// removed but new tools have not yet been added. Callers should handle tool-not-found
    /// gracefully during this interval.
    /// </summary>
    public void RegisterToolsFromServer(string serverName, List<ToolDefinition> tools)
    {
        UnregisterToolsForServer(serverName);

        foreach (var tool in tools)
        {
            tool.ServerName = serverName;
            _tools[tool.Name] = tool;
        }

        RebuildLowerIndex();
        _logger?.LogInformation("Registered {Count} tools from server {Server}", tools.Count, serverName);
    }

    /// <summary>
    /// Removes all tools associated with the specified server.
    /// </summary>
    public void UnregisterToolsForServer(string serverName)
    {
        var toRemove = _tools
            .Where(kvp => kvp.Value.ServerName == serverName)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
            _tools.TryRemove(key, out _);

        if (toRemove.Count > 0)
        {
            RebuildLowerIndex();
            _logger?.LogInformation("Unregistered {Count} tools from server {Server}", toRemove.Count, serverName);
        }
    }

    /// <summary>
    /// Finds which server hosts the given tool.
    /// Uses fuzzy resolution internally so case mismatches and typos are handled.
    /// </summary>
    public string? FindToolServer(string toolName)
    {
        var resolution = ResolveTool(toolName);
        return resolution.Tool?.ServerName;
    }

    /// <summary>
    /// Gets a tool definition by name.
    /// </summary>
    public ToolDefinition? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// Resolves a tool name through exact match, case-insensitive, then Levenshtein fuzzy match.
    /// Never throws — returns structured result with error on failure.
    /// </summary>
    public ToolResolution ResolveTool(string name)
    {
        // Step 1: Exact match
        if (_tools.TryGetValue(name, out var exactTool))
            return new ToolResolution(exactTool, null, null);

        // Step 2: Case-insensitive
        if (_lowerToCanonical.TryGetValue(name.ToLowerInvariant(), out var canonical)
            && _tools.TryGetValue(canonical, out var ciTool))
        {
            _logger?.LogWarning("Tool name corrected: '{Original}' -> '{Corrected}' (case)", name, canonical);
            return new ToolResolution(ciTool, canonical, null);
        }

        // Step 3: Levenshtein distance <= 2
        var allNames = _tools.Keys.ToList();
        if (allNames.Count > 0)
        {
            var nameLower = name.ToLowerInvariant();
            int bestDist = int.MaxValue;
            var bestMatches = new List<string>();

            foreach (var candidate in allNames)
            {
                int dist = LevenshteinDistance(nameLower, candidate.ToLowerInvariant());
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMatches = [candidate];
                }
                else if (dist == bestDist)
                {
                    bestMatches.Add(candidate);
                }
            }

            if (bestDist <= 2 && bestMatches.Count == 1)
            {
                var match = bestMatches[0];
                _logger?.LogWarning("Tool name corrected: '{Original}' -> '{Corrected}' (Levenshtein dist={Distance})",
                    name, match, bestDist);
                return new ToolResolution(_tools[match], match, null);
            }

            if (bestDist <= 2 && bestMatches.Count > 1)
            {
                var suggestions = string.Join(", ", bestMatches);
                return new ToolResolution(null, null,
                    $"[InvalidTool] Ambiguous tool name '{name}'. Close matches: {suggestions}");
            }
        }

        // Step 4: Fail
        var available = string.Join(", ", _tools.Keys);
        return new ToolResolution(null, null,
            $"[InvalidTool] Unknown tool '{name}'. Available tools: {available}");
    }

    private void RebuildLowerIndex()
    {
        var index = new Dictionary<string, string>(_tools.Count, StringComparer.Ordinal);
        foreach (var key in _tools.Keys)
            index[key.ToLowerInvariant()] = key;
        _lowerToCanonical = index;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var rowLen = b.Length + 1;
        Span<int> row = rowLen <= 256 ? stackalloc int[rowLen] : new int[rowLen];
        for (int j = 0; j < rowLen; j++) row[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            int prev = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int temp = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prev + cost);
                prev = temp;
            }
        }
        return row[b.Length];
    }

    /// <summary>
    /// Returns a formatted string of all registered tools suitable for LLM prompts.
    /// Renders parameter schemas as human-readable text instead of raw JSON Schema
    /// so that smaller models can reliably use the correct parameter names.
    /// </summary>
    public string GetToolDefinitionsForPrompt()
    {
        if (_tools.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Available tools:");

        foreach (var tool in _tools.Values)
            RenderToolToStringBuilder(sb, tool);

        return sb.ToString();
    }

    internal static void RenderToolToStringBuilder(StringBuilder sb, ToolDefinition tool)
    {
        sb.AppendLine($"- {tool.Name}: {tool.Description}");

        if (!tool.InputSchema.HasValue)
            return;

        var schema = tool.InputSchema.Value;
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var reqArray) &&
            reqArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArray.EnumerateArray())
            {
                var name = item.GetString();
                if (name is not null) required.Add(name);
            }
        }

        if (schema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var paramType = prop.Value.TryGetProperty("type", out var t)
                    ? t.GetString() ?? "any"
                    : "any";
                var desc = prop.Value.TryGetProperty("description", out var d)
                    ? d.GetString() ?? ""
                    : "";
                var reqTag = required.Contains(prop.Name) ? "REQUIRED" : "optional";

                sb.AppendLine($"    {prop.Name} ({paramType}, {reqTag}): {desc}");
            }
        }
    }
}
