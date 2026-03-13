using Microsoft.Data.Sqlite;
using Nexus.Memory;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Memory.Tests;

public class KnowledgeGraphTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;

    public KnowledgeGraphTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
    }

    [Fact]
    public async Task AddEntityAsync_ShouldPersistEntity()
    {
        var entity = new Entity
        {
            Name = "Alice",
            Type = EntityType.Person,
            TextSummary = "Software engineer"
        };

        var result = await _graph.AddEntityAsync(entity);

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(EntityType.Person, result.Type);
    }

    [Fact]
    public async Task GetEntityAsync_ShouldReturnCorrectEntity()
    {
        var entity = new Entity { Name = "TestProject", Type = EntityType.Project };
        await _graph.AddEntityAsync(entity);

        var retrieved = await _graph.GetEntityAsync(entity.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(entity.Id, retrieved.Id);
        Assert.Equal("TestProject", retrieved.Name);
    }

    [Fact]
    public async Task GetEntityAsync_WithUnknownId_ShouldReturnNull()
    {
        var result = await _graph.GetEntityAsync("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntitiesByTypeAsync_ShouldFilterCorrectly()
    {
        await _graph.AddEntityAsync(new Entity { Name = "Alice", Type = EntityType.Person });
        await _graph.AddEntityAsync(new Entity { Name = "Bob", Type = EntityType.Person });
        await _graph.AddEntityAsync(new Entity { Name = "C#", Type = EntityType.Technology });

        var people = await _graph.GetEntitiesByTypeAsync(EntityType.Person);

        Assert.Equal(2, people.Count);
        Assert.All(people, e => Assert.Equal(EntityType.Person, e.Type));
    }

    [Fact]
    public async Task UpdateEntityAsync_ShouldPersistChanges()
    {
        var entity = new Entity { Name = "OldName", Type = EntityType.Other };
        await _graph.AddEntityAsync(entity);

        entity.Name = "NewName";
        entity.MentionCount = 5;
        await _graph.UpdateEntityAsync(entity);

        var updated = await _graph.GetEntityAsync(entity.Id);

        Assert.NotNull(updated);
        Assert.Equal("NewName", updated.Name);
        Assert.Equal(5, updated.MentionCount);
    }

    [Fact]
    public async Task AddRelationAsync_ShouldPersistRelation()
    {
        var e1 = new Entity { Name = "Alice", Type = EntityType.Person };
        var e2 = new Entity { Name = "Nexus", Type = EntityType.Project };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var relation = new Relation
        {
            EntityId1 = e1.Id,
            EntityId2 = e2.Id,
            RelationType = "works_on",
            Confidence = 0.9
        };

        var result = await _graph.AddRelationAsync(relation);

        Assert.NotNull(result);
        Assert.Equal("works_on", result.RelationType);
    }

    [Fact]
    public async Task GetRelationsForEntityAsync_ShouldReturnRelatedRelations()
    {
        var e1 = new Entity { Name = "Alice", Type = EntityType.Person };
        var e2 = new Entity { Name = "Project X", Type = EntityType.Project };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = e1.Id, EntityId2 = e2.Id, RelationType = "works_on"
        });

        var relations = await _graph.GetRelationsForEntityAsync(e1.Id);

        Assert.Single(relations);
        Assert.Equal("works_on", relations[0].RelationType);
    }

    [Fact]
    public async Task LogActionAsync_ShouldPersistAction()
    {
        var action = new AgentAction
        {
            ActionType = "chat",
            ModelUsed = "ollama/qwen3:14b",
            TokensIn = 100,
            TokensOut = 200,
            DurationMs = 500
        };

        var result = await _graph.LogActionAsync(action);

        Assert.NotNull(result);
        Assert.Equal("chat", result.ActionType);
    }

    [Fact]
    public async Task GetRecentActionsAsync_ShouldReturnLimitedResults()
    {
        for (int i = 0; i < 10; i++)
            await _graph.LogActionAsync(new AgentAction { ActionType = "test" });

        var actions = await _graph.GetRecentActionsAsync(5);

        Assert.Equal(5, actions.Count);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
