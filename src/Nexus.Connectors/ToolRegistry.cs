using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexus.Connectors;

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
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
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
            _logger?.LogInformation("Unregistered {Count} tools from server {Server}", toRemove.Count, serverName);
    }

    /// <summary>
    /// Finds which server hosts the given tool.
    /// Returns null if the tool is not registered.
    /// </summary>
    public string? FindToolServer(string toolName)
    {
        return _tools.TryGetValue(toolName, out var tool) ? tool.ServerName : null;
    }

    /// <summary>
    /// Gets a tool definition by name.
    /// </summary>
    public ToolDefinition? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

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
        {
            sb.AppendLine($"- {tool.Name}: {tool.Description}");

            if (!tool.InputSchema.HasValue)
                continue;

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

        return sb.ToString();
    }
}
