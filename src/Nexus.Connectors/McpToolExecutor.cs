using Microsoft.Extensions.Logging;
using Nexus.Connectors.ToolFiltering;
using Nexus.Core.Abstractions;

namespace Nexus.Connectors;

/// <summary>
/// Implements IToolExecutor by routing tool invocations through McpClientManager
/// and ToolRegistry. This is the bridge between the Core layer and MCP servers.
/// </summary>
public class McpToolExecutor : IToolExecutor
{
    private readonly IMcpClientManager _clientManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpToolExecutor>? _logger;
    private readonly ToolPromptFormatter? _toolPromptFormatter;
    private readonly bool _toolFilteringEnabled;

    public McpToolExecutor(
        IMcpClientManager clientManager,
        ToolRegistry toolRegistry,
        ILogger<McpToolExecutor>? logger = null,
        ToolPromptFormatter? toolPromptFormatter = null,
        bool toolFilteringEnabled = false)
    {
        _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
        _toolPromptFormatter = toolPromptFormatter;
        _toolFilteringEnabled = toolFilteringEnabled;
    }

    public bool HasTools => _toolRegistry.Tools.Count > 0;

    public string GetToolDefinitionsForPrompt()
    {
        return _toolRegistry.GetToolDefinitionsForPrompt();
    }

    public string GetToolDefinitionsForPrompt(string? modelName)
    {
        if (!_toolFilteringEnabled || _toolPromptFormatter is null || string.IsNullOrWhiteSpace(modelName))
            return GetToolDefinitionsForPrompt();

        var tools = _toolRegistry.Tools.Values;
        if (!tools.Any())
            return string.Empty;

        return _toolPromptFormatter.Format(tools, modelName);
    }

    public async Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedServer = serverName;
        var resolvedToolName = toolName;

        // If serverName is empty, resolve the tool from the registry (with fuzzy matching)
        if (string.IsNullOrEmpty(resolvedServer))
        {
            var resolution = _toolRegistry.ResolveTool(toolName);
            if (resolution.Tool is null)
            {
                return resolution.Error ?? $"Error: Tool '{toolName}' is not registered with any server.";
            }

            resolvedServer = resolution.Tool.ServerName;
            resolvedToolName = resolution.CorrectedName ?? toolName;

            if (resolution.CorrectedName is not null)
            {
                _logger?.LogInformation("Tool name resolved: '{Original}' -> '{Corrected}'",
                    toolName, resolution.CorrectedName);
            }
        }

        // Override dryRun: small models default to true out of caution.
        // Permissions will be handled at the UI layer in the future.
        if (parameters is not null)
        {
            foreach (var key in parameters.Keys.ToList())
            {
                if (key.Equals("dryRun", StringComparison.OrdinalIgnoreCase) &&
                    parameters[key] is true or "true" or "True")
                {
                    parameters[key] = false;
                    _logger?.LogInformation("Overrode dryRun to false for tool {Tool}", resolvedToolName);
                }
            }
        }

        _logger?.LogInformation("Invoking tool {Tool} on server {Server}", resolvedToolName, resolvedServer);

        return await _clientManager.InvokeToolAsync(
            resolvedServer, resolvedToolName, parameters, cancellationToken).ConfigureAwait(false);
    }
}
