using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Core.Config;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;
using Xunit;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests that verify the interaction between Core, Memory, and the agent service.
/// </summary>
public class AgentIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IServiceProvider _services;

    public AgentIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_integration_{Guid.NewGuid():N}.db");

        var config = new NexusConfig
        {
            Memory = new MemoryConfig { Database = _dbPath }
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddNexusAgent(config);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public void ServiceProvider_ShouldResolveAgentService()
    {
        var agentService = _services.GetService<AgentService>();
        Assert.NotNull(agentService);
    }

    [Fact]
    public void ServiceProvider_ShouldResolveKnowledgeGraph()
    {
        var graph = _services.GetService<IKnowledgeGraph>();
        Assert.NotNull(graph);
    }

    [Fact]
    public void ServiceProvider_ShouldResolveModelRouter()
    {
        var router = _services.GetService<ModelRouter>();
        Assert.NotNull(router);
    }

    [Fact]
    public async Task KnowledgeGraph_ShouldBeInitializedAndUsable()
    {
        var graph = _services.GetRequiredService<IKnowledgeGraph>();
        var entities = await graph.GetAllEntitiesAsync();
        Assert.NotNull(entities);
    }

    [Fact]
    public void AgentService_ShouldStartWithEmptyHistory()
    {
        var agent = _services.GetRequiredService<AgentService>();
        Assert.Empty(agent.ConversationHistory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        (_services as IDisposable)?.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
