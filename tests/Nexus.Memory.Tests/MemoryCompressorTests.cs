using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Memory.Tests;

public class MemoryCompressorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;
    private readonly string _archiveDir;

    public MemoryCompressorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
        _archiveDir = Path.Combine(Path.GetTempPath(), $"nexus_archive_{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task ArchiveStaleEntities_MovesOldArchiveEntitiesToJson()
    {
        // Arrange — one stale Archive entity (200 days old)
        var staleEntity = new Entity
        {
            Name = "OldProject",
            Type = EntityType.Project,
            TextSummary = "A completed project",
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-200),
            FirstMentioned = DateTime.UtcNow.AddDays(-300),
            MentionCount = 5,
            RelevanceScore = 0.01
        };
        await _graph.AddEntityAsync(staleEntity);

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.ArchiveStaleEntitiesAsync();

        // Assert — entity archived and deleted from graph
        Assert.Equal(1, count);
        Assert.Null(await _graph.GetEntityAsync(staleEntity.Id));

        // JSON file should exist
        var expectedFile = Path.Combine(_archiveDir, $"archive-{DateTime.UtcNow:yyyy-MM-dd}.json");
        Assert.True(File.Exists(expectedFile));
    }

    [Fact]
    public async Task ArchiveStaleEntities_SkipsRecentArchiveEntities()
    {
        // Arrange — Archive entity that is only 10 days old (below 90-day threshold)
        var recentEntity = new Entity
        {
            Name = "RecentArchive",
            Type = EntityType.Technology,
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-10),
            MentionCount = 1,
            RelevanceScore = 0.04
        };
        await _graph.AddEntityAsync(recentEntity);

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.ArchiveStaleEntitiesAsync();

        // Assert — not archived, still in graph
        Assert.Equal(0, count);
        Assert.NotNull(await _graph.GetEntityAsync(recentEntity.Id));
    }

    [Fact]
    public async Task ArchiveStaleEntities_NeverDeletesWorkingLevelEntities()
    {
        // Arrange — Working-level entity that is very old
        var workingEntity = new Entity
        {
            Name = "ActiveProject",
            Type = EntityType.Project,
            MemoryLevel = MemoryLevel.Working,
            LastMentioned = DateTime.UtcNow.AddDays(-500),
            MentionCount = 10,
            RelevanceScore = 0.9
        };
        await _graph.AddEntityAsync(workingEntity);

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.ArchiveStaleEntitiesAsync();

        // Assert — Working entity untouched
        Assert.Equal(0, count);
        Assert.NotNull(await _graph.GetEntityAsync(workingEntity.Id));
    }

    [Fact]
    public async Task ArchiveStaleEntities_CreatesCorrectJsonFormat()
    {
        // Arrange — stale Archive entity with embedding and relation
        var embedding = SemanticSearch.ToByteArray(new float[] { 1.0f, 0.5f, 0.0f });
        var entity = new Entity
        {
            Name = "TestEntity",
            Type = EntityType.Person,
            TextSummary = "A test person",
            Embedding = embedding,
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-200),
            FirstMentioned = DateTime.UtcNow.AddDays(-300),
            MentionCount = 3,
            RelevanceScore = 0.02
        };
        await _graph.AddEntityAsync(entity);

        var otherEntity = new Entity { Name = "OtherEntity", Type = EntityType.Project };
        await _graph.AddEntityAsync(otherEntity);
        await _graph.AddRelationAsync(new Relation
        {
            EntityId1 = entity.Id,
            EntityId2 = otherEntity.Id,
            RelationType = "knows",
            Context = "test context",
            Confidence = 0.95
        });

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        await compressor.ArchiveStaleEntitiesAsync();

        // Assert — read and validate JSON structure
        var filePath = Path.Combine(_archiveDir, $"archive-{DateTime.UtcNow:yyyy-MM-dd}.json");
        var json = await File.ReadAllTextAsync(filePath);
        var archive = JsonSerializer.Deserialize<ArchiveFile>(json, MemoryCompressor.JsonOptions);

        Assert.NotNull(archive);
        Assert.Equal(1, archive!.Version);
        Assert.Single(archive.Entities);

        var archived = archive.Entities[0];
        Assert.Equal(entity.Id, archived.Id);
        Assert.Equal("TestEntity", archived.Name);
        Assert.Equal("Person", archived.Type);
        Assert.Equal("A test person", archived.TextSummary);
        Assert.Equal(Convert.ToBase64String(embedding), archived.Embedding);
        Assert.Equal(3, archived.MentionCount);
        Assert.Equal(0.02, archived.RelevanceScore);

        Assert.Single(archived.Relations);
        Assert.Equal("knows", archived.Relations[0].RelationType);
        Assert.Equal("test context", archived.Relations[0].Context);
        Assert.Equal(0.95, archived.Relations[0].Confidence);
    }

    [Fact]
    public async Task ArchiveStaleEntities_AppendsToExistingArchiveFile()
    {
        // Arrange — pre-existing archive file with one entity
        Directory.CreateDirectory(_archiveDir);
        var filePath = Path.Combine(_archiveDir, $"archive-{DateTime.UtcNow:yyyy-MM-dd}.json");

        var existingArchive = new ArchiveFile
        {
            ArchivedAt = DateTime.UtcNow.AddHours(-1),
            Entities = new List<ArchivedEntity>
            {
                new ArchivedEntity
                {
                    Id = "existing-id-1",
                    Name = "PreviousEntity",
                    Type = "Technology",
                    MentionCount = 2,
                    RelevanceScore = 0.01
                }
            }
        };
        var existingJson = JsonSerializer.Serialize(existingArchive, MemoryCompressor.JsonOptions);
        await File.WriteAllTextAsync(filePath, existingJson);

        // Add a new stale Archive entity to the graph
        var newEntity = new Entity
        {
            Name = "NewStaleEntity",
            Type = EntityType.Person,
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-200),
            MentionCount = 1,
            RelevanceScore = 0.01
        };
        await _graph.AddEntityAsync(newEntity);

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        await compressor.ArchiveStaleEntitiesAsync();

        // Assert — file contains both old and new entities
        var json = await File.ReadAllTextAsync(filePath);
        var archive = JsonSerializer.Deserialize<ArchiveFile>(json, MemoryCompressor.JsonOptions);

        Assert.NotNull(archive);
        Assert.Equal(2, archive!.Entities.Count);
        Assert.Contains(archive.Entities, e => e.Id == "existing-id-1");
        Assert.Contains(archive.Entities, e => e.Id == newEntity.Id);
    }

    [Fact]
    public async Task ArchiveStaleEntities_GraphThrows_ReturnsZero()
    {
        // Arrange — IKnowledgeGraph that throws on GetEntitiesByLevelAsync
        var throwingGraph = new ThrowingKnowledgeGraph();
        var compressor = new MemoryCompressor(throwingGraph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.ArchiveStaleEntitiesAsync();

        // Assert — never-throws contract: returns 0 on failure
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ArchiveStaleEntities_DeleteThrowsForOneEntity_StillCompletesArchive()
    {
        // Arrange — two stale Archive entities
        var entity1 = new Entity
        {
            Name = "Entity1",
            Type = EntityType.Person,
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-200),
            MentionCount = 1,
            RelevanceScore = 0.01
        };
        var entity2 = new Entity
        {
            Name = "Entity2",
            Type = EntityType.Project,
            MemoryLevel = MemoryLevel.Archive,
            LastMentioned = DateTime.UtcNow.AddDays(-200),
            MentionCount = 2,
            RelevanceScore = 0.01
        };
        await _graph.AddEntityAsync(entity1);
        await _graph.AddEntityAsync(entity2);

        // Use a wrapper graph that throws on DeleteEntityAsync for entity1
        var failDeleteGraph = new DeleteFailingKnowledgeGraph(_graph, failEntityId: entity1.Id);
        var compressor = new MemoryCompressor(failDeleteGraph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.ArchiveStaleEntitiesAsync();

        // Assert — both entities counted as archived (archive file written)
        Assert.Equal(2, count);

        // Verify archive file exists with both entities
        var filePath = Path.Combine(_archiveDir, $"archive-{DateTime.UtcNow:yyyy-MM-dd}.json");
        Assert.True(File.Exists(filePath));
        var json = await File.ReadAllTextAsync(filePath);
        var archive = JsonSerializer.Deserialize<ArchiveFile>(json, MemoryCompressor.JsonOptions);
        Assert.NotNull(archive);
        Assert.Equal(2, archive!.Entities.Count);
    }

    [Fact]
    public void ArchivePath_DefaultValue()
    {
        // Arrange & Act
        var config = new Nexus.Core.Config.NexusConfig();

        // Assert
        Assert.Equal("~/.nexus/archive/", config.Memory.ArchivePath);
    }

    [Fact]
    public async Task CompressSummaries_GroupsWeeklyInteractions()
    {
        // Arrange — 3 interactions pinned to a known Wednesday 10 days ago (mid-week, safe from ISO week boundary)
        var midWeek = DateTime.UtcNow.AddDays(-10);
        // Adjust to nearest Wednesday to avoid ISO week boundary flakiness
        midWeek = midWeek.AddDays(-(int)midWeek.DayOfWeek + (int)DayOfWeek.Wednesday);
        midWeek = new DateTime(midWeek.Year, midWeek.Month, midWeek.Day, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 3; i++)
        {
            await _graph.AddInteractionAsync(new Interaction
            {
                Summary = $"Weekly interaction {i}",
                Timestamp = midWeek.AddHours(i),
                TokenCount = 50,
                ReferencedEntityIds = new List<string> { $"entity-{i}" }
            });
        }

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.CompressSummariesAsync();

        // Assert — 3 originals replaced
        Assert.Equal(3, count);

        // Verify only 1 compressed interaction remains
        var remaining = await _graph.GetRecentInteractionsAsync(100);
        Assert.Single(remaining);
        Assert.Contains("Weekly summary", remaining[0].Summary);
    }

    [Fact]
    public async Task CompressSummaries_GroupsMonthlyInteractions()
    {
        // Arrange — 5 interactions pinned to mid-month (safe from month boundary)
        var midMonth = new DateTime(2025, 8, 15, 12, 0, 0, DateTimeKind.Utc); // August 15, 2025
        for (int i = 0; i < 5; i++)
        {
            await _graph.AddInteractionAsync(new Interaction
            {
                Summary = $"Monthly interaction {i}",
                Timestamp = midMonth.AddHours(i),
                TokenCount = 30,
                ReferencedEntityIds = new List<string> { "shared-entity" }
            });
        }

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.CompressSummariesAsync();

        // Assert — 5 originals replaced
        Assert.Equal(5, count);

        var remaining = await _graph.GetRecentInteractionsAsync(100);
        Assert.Single(remaining);
        Assert.Contains("Monthly summary", remaining[0].Summary);
    }

    [Fact]
    public async Task CompressSummaries_SkipsRecentInteractions()
    {
        // Arrange — 2 interactions from 2 days ago (too recent to compress)
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        for (int i = 0; i < 2; i++)
        {
            await _graph.AddInteractionAsync(new Interaction
            {
                Summary = $"Recent interaction {i}",
                Timestamp = twoDaysAgo.AddHours(i),
                TokenCount = 40
            });
        }

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.CompressSummariesAsync();

        // Assert — nothing compressed
        Assert.Equal(0, count);

        var remaining = await _graph.GetRecentInteractionsAsync(100);
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public async Task CompressSummaries_SingleInGroup_NotCompressed()
    {
        // Arrange — 1 interaction from 15 days ago (group of 1 is skipped)
        await _graph.AddInteractionAsync(new Interaction
        {
            Summary = "Solo interaction",
            Timestamp = DateTime.UtcNow.AddDays(-15),
            TokenCount = 60
        });

        var compressor = new MemoryCompressor(_graph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.CompressSummariesAsync();

        // Assert — single-item group not compressed
        Assert.Equal(0, count);

        var remaining = await _graph.GetRecentInteractionsAsync(100);
        Assert.Single(remaining);
        Assert.Equal("Solo interaction", remaining[0].Summary);
    }

    [Fact]
    public async Task CompressSummaries_NeverThrows_ReturnsZero()
    {
        // Arrange — ThrowingKnowledgeGraph throws on all methods
        var throwingGraph = new ThrowingKnowledgeGraph();
        var compressor = new MemoryCompressor(throwingGraph, _archiveDir, archiveThresholdDays: 90);

        // Act
        var count = await compressor.CompressSummariesAsync();

        // Assert — never-throws contract: returns 0 on failure
        Assert.Equal(0, count);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        try
        {
            if (Directory.Exists(_archiveDir))
                Directory.Delete(_archiveDir, recursive: true);
        }
        catch { }
    }
}

/// <summary>
/// Stub that throws on every method — used to verify MemoryCompressor's never-throws contract.
/// </summary>
internal sealed class ThrowingKnowledgeGraph : IKnowledgeGraph
{
    public Task<Entity> AddEntityAsync(Entity entity, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<Entity?> GetEntityAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<Entity?> GetEntityByNameAsync(string name, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Entity>> GetAllEntitiesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task UpdateEntityAsync(Entity entity, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<Relation> AddRelationAsync(Relation relation, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Relation>> GetAllRelationsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Interaction>> GetRecentInteractionsAsync(int limit = 10, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<int> GetInteractionCountAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task UpdateRelationEntityIdAsync(string oldEntityId, string newEntityId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Entity>> GetEntitiesByLevelAsync(MemoryLevel level, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task<List<Interaction>> GetInteractionsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
    public Task DeleteInteractionAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated failure");
}

/// <summary>
/// Wrapper that delegates all calls to an inner graph but throws on DeleteEntityAsync for a specific entity.
/// </summary>
internal sealed class DeleteFailingKnowledgeGraph : IKnowledgeGraph
{
    private readonly IKnowledgeGraph _inner;
    private readonly string _failEntityId;

    public DeleteFailingKnowledgeGraph(IKnowledgeGraph inner, string failEntityId)
    {
        _inner = inner;
        _failEntityId = failEntityId;
    }

    public Task<Entity> AddEntityAsync(Entity entity, CancellationToken cancellationToken = default) => _inner.AddEntityAsync(entity, cancellationToken);
    public Task<Entity?> GetEntityAsync(string id, CancellationToken cancellationToken = default) => _inner.GetEntityAsync(id, cancellationToken);
    public Task<Entity?> GetEntityByNameAsync(string name, CancellationToken cancellationToken = default) => _inner.GetEntityByNameAsync(name, cancellationToken);
    public Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default) => _inner.GetEntitiesByTypeAsync(type, cancellationToken);
    public Task<List<Entity>> GetAllEntitiesAsync(CancellationToken cancellationToken = default) => _inner.GetAllEntitiesAsync(cancellationToken);
    public Task UpdateEntityAsync(Entity entity, CancellationToken cancellationToken = default) => _inner.UpdateEntityAsync(entity, cancellationToken);
    public Task<Relation> AddRelationAsync(Relation relation, CancellationToken cancellationToken = default) => _inner.AddRelationAsync(relation, cancellationToken);
    public Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken cancellationToken = default) => _inner.GetRelationsForEntityAsync(entityId, cancellationToken);
    public Task<List<Relation>> GetAllRelationsAsync(CancellationToken cancellationToken = default) => _inner.GetAllRelationsAsync(cancellationToken);
    public Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken cancellationToken = default) => _inner.AddInteractionAsync(interaction, cancellationToken);
    public Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken cancellationToken = default) => _inner.LogActionAsync(action, cancellationToken);
    public Task<List<Interaction>> GetRecentInteractionsAsync(int limit = 10, CancellationToken cancellationToken = default) => _inner.GetRecentInteractionsAsync(limit, cancellationToken);
    public Task<int> GetInteractionCountAsync(CancellationToken cancellationToken = default) => _inner.GetInteractionCountAsync(cancellationToken);
    public Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken cancellationToken = default) => _inner.GetRecentActionsAsync(limit, cancellationToken);
    public Task UpdateRelationEntityIdAsync(string oldEntityId, string newEntityId, CancellationToken cancellationToken = default) => _inner.UpdateRelationEntityIdAsync(oldEntityId, newEntityId, cancellationToken);
    public Task<List<Entity>> GetEntitiesByLevelAsync(MemoryLevel level, CancellationToken cancellationToken = default) => _inner.GetEntitiesByLevelAsync(level, cancellationToken);

    public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        if (id == _failEntityId)
            throw new InvalidOperationException($"Simulated delete failure for entity {id}");
        return _inner.DeleteEntityAsync(id, cancellationToken);
    }

    public Task<List<Interaction>> GetInteractionsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default) => _inner.GetInteractionsOlderThanAsync(cutoff, cancellationToken);
    public Task DeleteInteractionAsync(string id, CancellationToken cancellationToken = default) => _inner.DeleteInteractionAsync(id, cancellationToken);
}
