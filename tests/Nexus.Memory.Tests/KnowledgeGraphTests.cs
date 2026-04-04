using Microsoft.Data.Sqlite;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
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

    [Fact]
    public async Task DeleteEntityAsync_RemovesEntityAndOrphanRelations()
    {
        // Arrange
        var e1 = new Entity { Name = "ToDelete", Type = EntityType.Person, RelevanceScore = 1.0 };
        var e2 = new Entity { Name = "Survivor", Type = EntityType.Project, RelevanceScore = 0.8 };
        var e3 = new Entity { Name = "Bystander", Type = EntityType.Technology, RelevanceScore = 0.5 };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);
        await _graph.AddEntityAsync(e3);

        // Relations involving e1 (should be deleted)
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = e1.Id, EntityId2 = e2.Id, RelationType = "works_on"
        });
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = e2.Id, EntityId2 = e1.Id, RelationType = "manages"
        });

        // Relation NOT involving e1 (should survive)
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = e2.Id, EntityId2 = e3.Id, RelationType = "uses"
        });

        // Act
        await _graph.DeleteEntityAsync(e1.Id);

        // Assert
        Assert.Null(await _graph.GetEntityAsync(e1.Id));
        Assert.NotNull(await _graph.GetEntityAsync(e2.Id));

        var e1Relations = await _graph.GetRelationsForEntityAsync(e1.Id);
        Assert.Empty(e1Relations);

        var survivorRelations = await _graph.GetRelationsForEntityAsync(e2.Id);
        Assert.Single(survivorRelations);
        Assert.Equal("uses", survivorRelations[0].RelationType);
    }

    [Fact]
    public async Task UpdateRelationEntityIdAsync_RePointsRelations()
    {
        // Arrange
        var entityA = new Entity { Name = "EntityA", Type = EntityType.Person };
        var entityB = new Entity { Name = "EntityB", Type = EntityType.Person };
        var entityC = new Entity { Name = "EntityC", Type = EntityType.Project };
        await _graph.AddEntityAsync(entityA);
        await _graph.AddEntityAsync(entityB);
        await _graph.AddEntityAsync(entityC);

        // Relation where A is entity_id_1
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = entityA.Id, EntityId2 = entityC.Id, RelationType = "created"
        });
        // Relation where A is entity_id_2
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = entityC.Id, EntityId2 = entityA.Id, RelationType = "owned_by"
        });

        // Act
        await _graph.UpdateRelationEntityIdAsync(entityA.Id, entityB.Id);

        // Assert — A should have no relations, B should have both
        var relationsForA = await _graph.GetRelationsForEntityAsync(entityA.Id);
        Assert.Empty(relationsForA);

        var relationsForB = await _graph.GetRelationsForEntityAsync(entityB.Id);
        Assert.Equal(2, relationsForB.Count);
        Assert.All(relationsForB, r =>
            Assert.True(r.EntityId1 == entityB.Id || r.EntityId2 == entityB.Id));
    }

    [Fact]
    public async Task GetEntitiesByLevelAsync_FiltersCorrectly()
    {
        // Arrange
        await _graph.AddEntityAsync(new Entity
        {
            Name = "WorkingEntity", Type = EntityType.Person,
            MemoryLevel = MemoryLevel.Working, RelevanceScore = 0.9
        });
        await _graph.AddEntityAsync(new Entity
        {
            Name = "HighRelevance", Type = EntityType.Project,
            MemoryLevel = MemoryLevel.Working, RelevanceScore = 1.0
        });
        await _graph.AddEntityAsync(new Entity
        {
            Name = "ArchivedEntity", Type = EntityType.Technology,
            MemoryLevel = MemoryLevel.Archive, RelevanceScore = 0.3
        });
        await _graph.AddEntityAsync(new Entity
        {
            Name = "RelevantEntity", Type = EntityType.Other,
            MemoryLevel = MemoryLevel.Relevant, RelevanceScore = 0.7
        });

        // Act
        var workingEntities = await _graph.GetEntitiesByLevelAsync(MemoryLevel.Working);

        // Assert
        Assert.Equal(2, workingEntities.Count);
        Assert.All(workingEntities, e => Assert.Equal(MemoryLevel.Working, e.MemoryLevel));

        // Verify ordered by relevance_score DESC
        Assert.Equal("HighRelevance", workingEntities[0].Name);
        Assert.Equal("WorkingEntity", workingEntities[1].Name);
    }

    [Fact]
    public async Task DeleteInteraction_RemovesFromDb()
    {
        // Arrange
        var interaction = new Interaction
        {
            Summary = "Test interaction to delete",
            TokenCount = 100
        };
        await _graph.AddInteractionAsync(interaction);

        // Verify it exists
        var before = await _graph.GetRecentInteractionsAsync(100);
        Assert.Single(before);

        // Act
        await _graph.DeleteInteractionAsync(interaction.Id);

        // Assert
        var after = await _graph.GetRecentInteractionsAsync(100);
        Assert.Empty(after);
    }

    [Fact]
    public async Task GetInteractionsOlderThan_FiltersCorrectly()
    {
        // Arrange — one old interaction and one recent
        var oldInteraction = new Interaction
        {
            Summary = "Old interaction",
            Timestamp = DateTime.UtcNow.AddDays(-30),
            TokenCount = 50
        };
        var recentInteraction = new Interaction
        {
            Summary = "Recent interaction",
            Timestamp = DateTime.UtcNow.AddDays(-1),
            TokenCount = 50
        };
        await _graph.AddInteractionAsync(oldInteraction);
        await _graph.AddInteractionAsync(recentInteraction);

        // Act — cutoff at 7 days ago
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var result = await _graph.GetInteractionsOlderThanAsync(cutoff);

        // Assert — only old interaction returned
        Assert.Single(result);
        Assert.Equal("Old interaction", result[0].Summary);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
