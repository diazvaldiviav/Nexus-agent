using Nexus.Connectors;
using Nexus.Core.Config;

namespace Nexus.Integration.Tests;

public class McpLifecycleServiceTests
{
    [Fact]
    public async Task ConnectServerAsync_RegistersToolsOnSuccess()
    {
        var manager = new FakeMcpClientManager
        {
            ConnectResult = true,
            DiscoveredTools = new List<ToolDefinition>
            {
                new() { Name = "read_file", Description = "Reads files", ServerName = "fs" }
            }
        };
        var registry = new ToolRegistry();
        var service = new McpLifecycleService(manager, registry);

        var result = await service.ConnectServerAsync(new McpServerEntry
        {
            Name = "fs",
            Transport = "stdio",
            Command = "npx"
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.ToolCount);
        Assert.NotNull(registry.GetTool("read_file"));
    }

    [Fact]
    public async Task DisconnectServerAsync_UnregistersTools()
    {
        var manager = new FakeMcpClientManager { ConnectResult = true };
        var registry = new ToolRegistry();
        registry.RegisterToolsFromServer("fs", new List<ToolDefinition>
        {
            new() { Name = "read_file", Description = "Reads files", ServerName = "fs" }
        });
        var service = new McpLifecycleService(manager, registry);

        await service.DisconnectServerAsync("fs");

        Assert.Null(registry.GetTool("read_file"));
    }

    [Fact]
    public async Task ConnectServerAsync_WhenManagerThrows_DoesNotThrow()
    {
        var manager = new FakeMcpClientManager { ThrowOnConnect = true };
        var registry = new ToolRegistry();
        var service = new McpLifecycleService(manager, registry);

        var result = await service.ConnectServerAsync(new McpServerEntry
        {
            Name = "bad",
            Transport = "stdio",
            Command = "missing-command"
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    private sealed class FakeMcpClientManager : IMcpClientManager
    {
        public bool ConnectResult { get; set; }
        public bool ThrowOnConnect { get; set; }
        public List<ToolDefinition> DiscoveredTools { get; set; } = new();
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
    }
}
