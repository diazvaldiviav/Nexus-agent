using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Nexus.Core.Config;

namespace Nexus.Connectors;

public interface IMcpClientManager
{
    Task<bool> ConnectAsync(McpServerEntry serverEntry, CancellationToken ct = default);
    Task DisconnectAsync(string serverName, CancellationToken ct = default);
    Task<List<ToolDefinition>> DiscoverToolsAsync(string serverName, CancellationToken ct = default);
    IReadOnlyDictionary<string, bool> GetServerStatus();
}

/// <summary>
/// Manages connections to MCP servers using the official MCP SDK.
/// Supports stdio transport (primary) and SSE transport (when available).
/// Never throws from public methods — returns error strings or false.
/// </summary>
public class McpClientManager : IMcpClientManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpClient> _clients = new();
    private readonly ILogger<McpClientManager>? _logger;

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connects to an MCP server using the configuration in the McpServerEntry.
    /// Returns true if connection and initialization succeeded.
    /// </summary>
    public async Task<bool> ConnectAsync(McpServerEntry serverEntry, CancellationToken ct = default)
    {
        try
        {
            _logger?.LogInformation("Connecting to MCP server {Name} via {Transport}",
                serverEntry.Name, serverEntry.Transport);

            IClientTransport transport;

            if (string.Equals(serverEntry.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(serverEntry.Command))
                {
                    _logger?.LogError("MCP server {Name}: stdio transport requires a command", serverEntry.Name);
                    return false;
                }

                var options = new StdioClientTransportOptions
                {
                    Command = serverEntry.Command,
                    Arguments = serverEntry.Args,
                    Name = serverEntry.Name
                };

                if (serverEntry.Env.Count > 0)
                {
                    options.EnvironmentVariables = serverEntry.Env.ToDictionary(
                        kvp => kvp.Key, kvp => (string?)kvp.Value);
                }

                transport = new StdioClientTransport(options);
            }
            else if (string.Equals(serverEntry.Transport, "sse", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(serverEntry.Url))
                {
                    _logger?.LogError("MCP server {Name}: SSE transport requires a URL", serverEntry.Name);
                    return false;
                }

                var httpOptions = new HttpClientTransportOptions
                {
                    Endpoint = new Uri(serverEntry.Url),
                    Name = serverEntry.Name,
                    TransportMode = HttpTransportMode.AutoDetect
                };
                transport = new HttpClientTransport(httpOptions);
            }
            else
            {
                _logger?.LogError("MCP server {Name}: unsupported transport '{Transport}'. Use 'stdio' or 'sse'",
                    serverEntry.Name, serverEntry.Transport);
                return false;
            }

            var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
            _clients[serverEntry.Name] = client;

            _logger?.LogInformation("Connected to MCP server {Name} successfully", serverEntry.Name);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Connection to MCP server {Name} was cancelled", serverEntry.Name);
            return false;
        }
        catch (ObjectDisposedException)
        {
            _logger?.LogWarning("Connection to MCP server {Name} failed: object already disposed", serverEntry.Name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to connect to MCP server {Name}", serverEntry.Name);
            return false;
        }
    }

    /// <summary>
    /// Disconnects from the named MCP server and disposes the client.
    /// </summary>
    public async Task DisconnectAsync(string serverName, CancellationToken ct = default)
    {
        try
        {
            if (_clients.TryRemove(serverName, out var client))
            {
                await client.DisposeAsync().ConfigureAwait(false);
                _logger?.LogInformation("Disconnected from MCP server {Name}", serverName);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disconnecting from MCP server {Name}", serverName);
        }
    }

    /// <summary>
    /// Discovers available tools from the named MCP server using tools/list.
    /// </summary>
    public async Task<List<ToolDefinition>> DiscoverToolsAsync(string serverName, CancellationToken ct = default)
    {
        var result = new List<ToolDefinition>();

        try
        {
            if (!_clients.TryGetValue(serverName, out var client))
            {
                _logger?.LogWarning("Cannot discover tools: server {Name} is not connected", serverName);
                return result;
            }

            var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);

            foreach (var tool in tools)
            {
                result.Add(new ToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description ?? string.Empty,
                    ServerName = serverName,
                    InputSchema = tool.JsonSchema is { } schema
                        ? JsonDocument.Parse(schema.GetRawText()).RootElement
                        : null
                });
            }

            _logger?.LogInformation("Discovered {Count} tools from MCP server {Name}", result.Count, serverName);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Tool discovery for MCP server {Name} was cancelled", serverName);
        }
        catch (ObjectDisposedException)
        {
            _logger?.LogWarning("Tool discovery for MCP server {Name} failed: client disposed", serverName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to discover tools from MCP server {Name}", serverName);
        }

        return result;
    }

    /// <summary>
    /// Invokes a tool on the named MCP server using tools/call.
    /// Returns the tool result as a string, or an error message.
    /// </summary>
    public async Task<string> InvokeToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_clients.TryGetValue(serverName, out var client))
            {
                return $"Error: MCP server '{serverName}' is not connected.";
            }

            var args = parameters != null
                ? new Dictionary<string, object?>(
                    parameters.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)))
                : null;

            var callResult = await client.CallToolAsync(
                toolName,
                args,
                cancellationToken: ct).ConfigureAwait(false);

            if (callResult.IsError == true)
            {
                var errorText = FormatContentBlocks(callResult.Content);
                return $"Tool error: {errorText}";
            }

            return FormatContentBlocks(callResult.Content);
        }
        catch (OperationCanceledException)
        {
            return $"Error: Tool invocation '{toolName}' on server '{serverName}' was cancelled.";
        }
        catch (ObjectDisposedException)
        {
            return $"Error: MCP server '{serverName}' connection has been closed.";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invoke tool {Tool} on server {Server}", toolName, serverName);
            return $"Error invoking tool '{toolName}': {ex.Message}";
        }
    }

    /// <summary>
    /// Returns the connection status for all known servers.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetServerStatus()
    {
        return _clients.ToDictionary(
            kvp => kvp.Key,
            kvp => true);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        foreach (var kvp in _clients.ToArray())
        {
            try
            {
                await kvp.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing MCP client {Name}", kvp.Key);
            }
        }
        _clients.Clear();
    }

    private static string FormatContentBlocks(IList<ContentBlock>? content)
    {
        if (content is null || content.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var block in content)
        {
            if (block is TextContentBlock textBlock)
            {
                parts.Add(textBlock.Text ?? string.Empty);
            }
            else
            {
                parts.Add(JsonSerializer.Serialize(block));
            }
        }

        return string.Join("\n", parts);
    }
}
