using Nexus.Connectors;
using Nexus.Core.Config;

namespace Nexus.Desktop.Tests.Fakes;

internal sealed class FakeMcpClientManager : IMcpClientManager
{
    public Task<bool> ConnectAsync(McpServerEntry serverEntry, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task DisconnectAsync(string serverName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<ToolDefinition>> DiscoverToolsAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult(new List<ToolDefinition>());

    public IReadOnlyDictionary<string, bool> GetServerStatus()
        => new Dictionary<string, bool>();
}
