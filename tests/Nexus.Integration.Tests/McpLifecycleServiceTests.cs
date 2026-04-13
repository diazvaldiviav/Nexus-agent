using Nexus.Connectors;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;

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

}
