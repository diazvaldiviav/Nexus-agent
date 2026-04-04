using Microsoft.Data.Sqlite;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Memory.Tests;

public class KnowledgeGraphNotifierTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;

    public KnowledgeGraphNotifierTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
    }

    [Fact]
    public async Task LogActionAsync_RaisesActionLoggedEvent()
    {
        // Arrange
        AgentAction? received = null;
        _graph.ActionLogged += a => received = a;
        var action = new AgentAction { ActionType = "chat", Detail = "Test action" };

        // Act
        await _graph.LogActionAsync(action);

        // Assert
        Assert.NotNull(received);
        Assert.Equal("chat", received.ActionType);
        Assert.Equal("Test action", received.Detail);
    }

    [Fact]
    public async Task LogActionAsync_NoSubscribers_DoesNotThrow()
    {
        // Arrange
        var action = new AgentAction { ActionType = "chat", Detail = "No subscriber test" };

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => _graph.LogActionAsync(action));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
