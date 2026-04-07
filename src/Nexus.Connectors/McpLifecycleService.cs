using Microsoft.Extensions.Logging;
using Nexus.Core.Config;

namespace Nexus.Connectors;

public sealed class McpLifecycleEvent
{
    public required string ServerName { get; init; }
    public required string EventType { get; init; }
    public bool Success { get; init; }
    public int ToolCount { get; init; }
    public string? Detail { get; init; }
}

public sealed class McpConnectResult
{
    public required string ServerName { get; init; }
    public bool Success { get; init; }
    public int ToolCount { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class McpServerStatusView
{
    public required string ServerName { get; init; }
    public required string Transport { get; init; }
    public required string CommandOrUrl { get; init; }
    public bool IsConnected { get; init; }
    public int ToolCount { get; init; }
}

public class McpLifecycleService
{
    private readonly IMcpClientManager _manager;
    private readonly IToolRegistry _registry;
    private readonly ILogger<McpLifecycleService>? _logger;

    public McpLifecycleService(
        IMcpClientManager manager,
        IToolRegistry registry,
        ILogger<McpLifecycleService>? logger = null)
    {
        _manager = manager;
        _registry = registry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpConnectResult>> ConnectServersAsync(
        IEnumerable<McpServerEntry> servers,
        Func<McpLifecycleEvent, CancellationToken, Task>? actionLogger = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<McpConnectResult>();
        foreach (var server in servers)
        {
            var result = await ConnectServerAsync(server, actionLogger, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    public async Task<McpConnectResult> ConnectServerAsync(
        McpServerEntry server,
        Func<McpLifecycleEvent, CancellationToken, Task>? actionLogger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connected = await _manager.ConnectAsync(server, cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                var failure = new McpConnectResult
                {
                    ServerName = server.Name,
                    Success = false,
                    ErrorMessage = "Connection failed."
                };

                await TryLogAsync(actionLogger, new McpLifecycleEvent
                {
                    ServerName = server.Name,
                    EventType = "connect_failed",
                    Success = false,
                    ToolCount = 0,
                    Detail = failure.ErrorMessage
                }, cancellationToken).ConfigureAwait(false);
                return failure;
            }

            var tools = await _manager.DiscoverToolsAsync(server.Name, cancellationToken).ConfigureAwait(false);
            _registry.RegisterToolsFromServer(server.Name, tools);

            var success = new McpConnectResult
            {
                ServerName = server.Name,
                Success = true,
                ToolCount = tools.Count
            };

            await TryLogAsync(actionLogger, new McpLifecycleEvent
            {
                ServerName = server.Name,
                EventType = "connected",
                Success = true,
                ToolCount = tools.Count,
                Detail = $"Connected with {tools.Count} tools."
            }, cancellationToken).ConfigureAwait(false);

            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unexpected MCP connect workflow error for server {Server}", server.Name);
            var failure = new McpConnectResult
            {
                ServerName = server.Name,
                Success = false,
                ErrorMessage = ex.Message
            };

            await TryLogAsync(actionLogger, new McpLifecycleEvent
            {
                ServerName = server.Name,
                EventType = "connect_failed",
                Success = false,
                ToolCount = 0,
                Detail = ex.Message
            }, cancellationToken).ConfigureAwait(false);
            return failure;
        }
    }

    public async Task DisconnectServerAsync(
        string serverName,
        Func<McpLifecycleEvent, CancellationToken, Task>? actionLogger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _manager.DisconnectAsync(serverName, cancellationToken).ConfigureAwait(false);
            _registry.UnregisterToolsForServer(serverName);

            await TryLogAsync(actionLogger, new McpLifecycleEvent
            {
                ServerName = serverName,
                EventType = "disconnected",
                Success = true,
                ToolCount = 0,
                Detail = "Disconnected."
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unexpected MCP disconnect workflow error for server {Server}", serverName);
            await TryLogAsync(actionLogger, new McpLifecycleEvent
            {
                ServerName = serverName,
                EventType = "disconnect_failed",
                Success = false,
                ToolCount = 0,
                Detail = ex.Message
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<McpServerStatusView> GetServerStatuses(IEnumerable<McpServerEntry> configuredServers)
    {
        var connected = _manager.GetServerStatus();
        var connectedNames = new HashSet<string>(connected.Keys, StringComparer.OrdinalIgnoreCase);
        var toolsByServer = _registry.Tools.Values
            .GroupBy(t => t.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return configuredServers
            .Select(server =>
            {
                var commandOrUrl = string.Equals(server.Transport, "sse", StringComparison.OrdinalIgnoreCase)
                    ? server.Url ?? string.Empty
                    : $"{server.Command ?? string.Empty} {string.Join(" ", server.Args)}".Trim();

                return new McpServerStatusView
                {
                    ServerName = server.Name,
                    Transport = server.Transport,
                    CommandOrUrl = commandOrUrl,
                    IsConnected = connectedNames.Contains(server.Name),
                    ToolCount = toolsByServer.TryGetValue(server.Name, out var count) ? count : 0
                };
            })
            .ToList();
    }

    private async Task TryLogAsync(
        Func<McpLifecycleEvent, CancellationToken, Task>? actionLogger,
        McpLifecycleEvent entry,
        CancellationToken cancellationToken)
    {
        if (actionLogger is null) return;

        try
        {
            await actionLogger(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Skipping MCP action log entry for {Server}", entry.ServerName);
        }
    }
}
