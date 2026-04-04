using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;

namespace Nexus.Connectors;

/// <summary>
/// Implements IToolExecutor by routing tool invocations through McpClientManager
/// and ToolRegistry. This is the bridge between the Core layer and MCP servers.
/// </summary>
public class McpToolExecutor : IToolExecutor
{
    private readonly McpClientManager _clientManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpToolExecutor>? _logger;

    public McpToolExecutor(
        McpClientManager clientManager,
        ToolRegistry toolRegistry,
        ILogger<McpToolExecutor>? logger = null)
    {
        _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    public bool HasTools => _toolRegistry.Tools.Count > 0;

    public string GetToolDefinitionsForPrompt()
    {
        return _toolRegistry.GetToolDefinitionsForPrompt();
    }

    public async Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedServer = serverName;

        // If serverName is empty, try to find the server from the registry
        if (string.IsNullOrEmpty(resolvedServer))
        {
            resolvedServer = _toolRegistry.FindToolServer(toolName);
            if (resolvedServer is null)
            {
                return $"Error: Tool '{toolName}' is not registered with any server.";
            }
        }

        _logger?.LogInformation("Invoking tool {Tool} on server {Server}", toolName, resolvedServer);

        return await _clientManager.InvokeToolAsync(
            resolvedServer, toolName, parameters, cancellationToken).ConfigureAwait(false);
    }
}
