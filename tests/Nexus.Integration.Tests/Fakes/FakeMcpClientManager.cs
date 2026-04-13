using Nexus.Connectors;
using Nexus.Core.Config;

namespace Nexus.Integration.Tests.Fakes;

public sealed class FakeMcpClientManager : IMcpClientManager
{
    public bool ConnectResult { get; set; }
    public bool ThrowOnConnect { get; set; }
    public List<ToolDefinition> DiscoveredTools { get; set; } = new();
    public string InvokeResult { get; set; } = string.Empty;
    public List<(string ServerName, string ToolName, Dictionary<string, object>? Parameters)> Invocations { get; } = new();
    private readonly Dictionary<string, bool> _status = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ConnectAsync(McpServerEntry serverEntry, CancellationToken ct = default)
    {
        if (ThrowOnConnect)
            throw new InvalidOperationException("connect error");
        if (ConnectResult)
            _status[serverEntry.Name] = true;
        return Task.FromResult(ConnectResult);
    }

    public Task DisconnectAsync(string serverName, CancellationToken ct = default)
    {
        _status.Remove(serverName);
        return Task.CompletedTask;
    }

    public Task<List<ToolDefinition>> DiscoverToolsAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult(DiscoveredTools.ToList());

    public IReadOnlyDictionary<string, bool> GetServerStatus() => _status;

    public Task<string> InvokeToolAsync(string serverName, string toolName, Dictionary<string, object>? parameters = null, CancellationToken ct = default)
    {
        Invocations.Add((serverName, toolName, parameters));
        return Task.FromResult(InvokeResult);
    }
}
