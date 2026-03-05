using Microsoft.Extensions.Logging;

namespace Nexus.Connectors;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly ILogger<ToolRegistry>? _logger;

    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger;
        RegisterBuiltinTools();
    }

    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;

    public void RegisterTool(ToolDefinition tool)
    {
        _tools[tool.Name] = tool;
        _logger?.LogInformation("Registered tool: {ToolName}", tool.Name);
    }

    public void UnregisterToolsForServer(string serverName)
    {
        var toRemove = _tools.Where(kvp => kvp.Value.ServerName == serverName).Select(kvp => kvp.Key).ToList();
        foreach (var key in toRemove)
            _tools.Remove(key);
    }

    public ToolDefinition? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    private void RegisterBuiltinTools()
    {
        RegisterTool(new ToolDefinition
        {
            Name = "read_file",
            Description = "Read the contents of a file",
            ServerName = "filesystem",
            Parameters = new Dictionary<string, string> { { "path", "string" } }
        });

        RegisterTool(new ToolDefinition
        {
            Name = "write_file",
            Description = "Write content to a file",
            ServerName = "filesystem",
            Parameters = new Dictionary<string, string> { { "path", "string" }, { "content", "string" } }
        });

        RegisterTool(new ToolDefinition
        {
            Name = "list_directory",
            Description = "List contents of a directory",
            ServerName = "filesystem",
            Parameters = new Dictionary<string, string> { { "path", "string" } }
        });

        RegisterTool(new ToolDefinition
        {
            Name = "git_status",
            Description = "Get the git status of a repository",
            ServerName = "git",
            Parameters = new Dictionary<string, string> { { "repo_path", "string" } }
        });

        RegisterTool(new ToolDefinition
        {
            Name = "git_log",
            Description = "Get the git commit log",
            ServerName = "git",
            Parameters = new Dictionary<string, string> { { "repo_path", "string" }, { "limit", "integer" } }
        });
    }
}
