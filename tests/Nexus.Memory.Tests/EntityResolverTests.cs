using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Models;
using Nexus.Memory.Tests.Fakes;
using Xunit;

namespace Nexus.Memory.Tests;

public class EntityResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;

    public EntityResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ReturnsPairsAboveThreshold()
    {
        // Arrange — two near-identical embeddings + one different
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { 0.99f, 0.1f, 0.0f }; // very similar to embedding1
        var embedding3 = new float[] { 0.0f, 0.0f, 1.0f };  // orthogonal

        var e1 = new Entity { Name = "Entity1", Type = EntityType.Person, Embedding = SemanticSearch.ToByteArray(embedding1) };
        var e2 = new Entity { Name = "Entity2", Type = EntityType.Person, Embedding = SemanticSearch.ToByteArray(embedding2) };
        var e3 = new Entity { Name = "Entity3", Type = EntityType.Technology, Embedding = SemanticSearch.ToByteArray(embedding3) };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);
        await _graph.AddEntityAsync(e3);

        var resolver = new EntityResolver(_graph, threshold: 0.85);

        // Act
        var duplicates = await resolver.FindDuplicatesAsync();

        // Assert
        Assert.Single(duplicates);
        Assert.True(duplicates[0].Similarity >= 0.85);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ReturnsEmpty_WhenNoDuplicates()
    {
        // Arrange — orthogonal embeddings
        var e1 = new Entity { Name = "A", Type = EntityType.Person, Embedding = SemanticSearch.ToByteArray(new float[] { 1, 0, 0 }) };
        var e2 = new Entity { Name = "B", Type = EntityType.Technology, Embedding = SemanticSearch.ToByteArray(new float[] { 0, 1, 0 }) };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var resolver = new EntityResolver(_graph, threshold: 0.85);

        // Act
        var duplicates = await resolver.FindDuplicatesAsync();

        // Assert
        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task FindDuplicatesAsync_SkipsEntitiesWithoutEmbeddings()
    {
        // Arrange — one with embedding, one without
        var e1 = new Entity { Name = "WithEmb", Type = EntityType.Person, Embedding = SemanticSearch.ToByteArray(new float[] { 1, 0, 0 }) };
        var e2 = new Entity { Name = "NoEmb", Type = EntityType.Person, Embedding = null };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var resolver = new EntityResolver(_graph, threshold: 0.0);

        // Act
        var duplicates = await resolver.FindDuplicatesAsync();

        // Assert — only 1 entity with embedding, need at least 2 for a pair
        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task MergeEntitiesAsync_ConsolidatesFieldsCorrectly()
    {
        // Arrange
        var earlier = DateTime.UtcNow.AddDays(-10);
        var later = DateTime.UtcNow;

        var e1 = new Entity
        {
            Name = "Alice",
            Type = EntityType.Person,
            TextSummary = "Short",
            MentionCount = 5,
            FirstMentioned = earlier,
            LastMentioned = earlier,
            RelevanceScore = 0.8,
            Embedding = SemanticSearch.ToByteArray(new float[] { 1, 0, 0 })
        };
        var e2 = new Entity
        {
            Name = "Alice Smith",
            Type = EntityType.Person,
            TextSummary = "A longer summary about Alice",
            MentionCount = 3,
            FirstMentioned = later,
            LastMentioned = later,
            RelevanceScore = 0.9,
            Embedding = SemanticSearch.ToByteArray(new float[] { 0.99f, 0.1f, 0 })
        };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        // Add a relation from e2 to some third entity
        var e3 = new Entity { Name = "Project", Type = EntityType.Project };
        await _graph.AddEntityAsync(e3);
        await _graph.AddRelationAsync(new Relation { EntityId1 = e2.Id, EntityId2 = e3.Id, RelationType = "works_on" });

        var pair = new DuplicatePair(e1, e2, 0.99);
        var resolver = new EntityResolver(_graph);

        // Act
        var survivor = await resolver.MergeEntitiesAsync(pair);

        // Assert — e1 has higher MentionCount, so it survives
        Assert.Equal(e1.Id, survivor.Id);
        Assert.Equal(8, survivor.MentionCount); // 5 + 3
        Assert.Equal("A longer summary about Alice", survivor.TextSummary); // longer wins
        Assert.Equal(earlier, survivor.FirstMentioned); // min
        Assert.Equal(later, survivor.LastMentioned); // max
        Assert.Equal(0.9, survivor.RelevanceScore); // max

        // Duplicate should be deleted
        Assert.Null(await _graph.GetEntityAsync(e2.Id));

        // Relations should be re-pointed to survivor
        var relations = await _graph.GetRelationsForEntityAsync(e1.Id);
        Assert.Single(relations);
        Assert.Equal("works_on", relations[0].RelationType);
    }

    [Fact]
    public async Task MergeEntitiesAsync_RegeneratesEmbedding_WhenServiceAvailable()
    {
        // Arrange
        var fakeEmbedding = new FakeEmbeddingService(new float[] { 0.5f, 0.5f, 0.5f });
        var e1 = new Entity { Name = "A", Type = EntityType.Person, MentionCount = 2, Embedding = SemanticSearch.ToByteArray(new float[] { 1, 0, 0 }) };
        var e2 = new Entity { Name = "B", Type = EntityType.Person, MentionCount = 1, Embedding = SemanticSearch.ToByteArray(new float[] { 0.9f, 0.1f, 0 }) };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var pair = new DuplicatePair(e1, e2, 0.95);
        var resolver = new EntityResolver(_graph, embeddingService: fakeEmbedding);

        // Act
        await resolver.MergeEntitiesAsync(pair);

        // Assert
        Assert.Equal(1, fakeEmbedding.CallCount);
    }

    [Fact]
    public async Task ConfirmDuplicateAsync_ReturnsTrue_WhenNoLlmAvailable()
    {
        // Arrange
        var e1 = new Entity { Name = "A", Type = EntityType.Person };
        var e2 = new Entity { Name = "B", Type = EntityType.Person };
        var pair = new DuplicatePair(e1, e2, 0.9);
        var resolver = new EntityResolver(_graph, llmClient: null);

        // Act
        var result = await resolver.ConfirmDuplicateAsync(pair);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmDuplicateAsync_ParsesLlmYesNo()
    {
        // Arrange
        var llm = new MockLlmClient(_ => Task.FromResult("Yes, these are the same entity."));
        var e1 = new Entity { Name = "C#", Type = EntityType.Technology, TextSummary = "Programming language" };
        var e2 = new Entity { Name = "CSharp", Type = EntityType.Technology, TextSummary = "Microsoft language" };
        var pair = new DuplicatePair(e1, e2, 0.9);
        var resolver = new EntityResolver(_graph, llmClient: llm);

        // Act
        var result = await resolver.ConfirmDuplicateAsync(pair);

        // Assert
        Assert.True(result);
        Assert.NotNull(llm.LastPrompt);
        Assert.Contains("C#", llm.LastPrompt);
        Assert.Contains("CSharp", llm.LastPrompt);
    }

    [Fact]
    public async Task FindAndMergeAsync_MergesAllDuplicates()
    {
        // Arrange — two very similar entities
        var embedding = new float[] { 1.0f, 0.0f, 0.0f };
        var similar = new float[] { 0.99f, 0.05f, 0.0f };

        var e1 = new Entity { Name = "Dup1", Type = EntityType.Person, MentionCount = 2, Embedding = SemanticSearch.ToByteArray(embedding) };
        var e2 = new Entity { Name = "Dup2", Type = EntityType.Person, MentionCount = 1, Embedding = SemanticSearch.ToByteArray(similar) };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var resolver = new EntityResolver(_graph, threshold: 0.85);

        // Act
        var kept = await resolver.FindAndMergeAsync(useLlmConfirmation: false);

        // Assert
        Assert.Single(kept);
        var allEntities = await _graph.GetAllEntitiesAsync();
        Assert.Single(allEntities);
        Assert.Equal(3, allEntities[0].MentionCount); // 2 + 1
    }

    [Fact]
    public async Task ConfirmDuplicateAsync_LlmThrows_ReturnsTrueAndLogs()
    {
        // Arrange
        var llm = new MockLlmClient(_ => throw new InvalidOperationException("LLM unavailable"));
        var e1 = new Entity { Name = "X", Type = EntityType.Other };
        var e2 = new Entity { Name = "Y", Type = EntityType.Other };
        var pair = new DuplicatePair(e1, e2, 0.9);
        var resolver = new EntityResolver(_graph, llmClient: llm);

        // Act
        var result = await resolver.ConfirmDuplicateAsync(pair);

        // Assert — should return true (auto-confirm) even though LLM threw
        Assert.True(result);
    }

    [Fact]
    public async Task FindAndMergeAsync_LlmSaysNo_DoesNotMerge()
    {
        // Arrange — two entities with near-identical embeddings
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { 0.99f, 0.05f, 0.0f };

        var e1 = new Entity { Name = "React", Type = EntityType.Technology, MentionCount = 2, Embedding = SemanticSearch.ToByteArray(embedding1) };
        var e2 = new Entity { Name = "React Native", Type = EntityType.Technology, MentionCount = 1, Embedding = SemanticSearch.ToByteArray(embedding2) };
        await _graph.AddEntityAsync(e1);
        await _graph.AddEntityAsync(e2);

        var llm = new MockLlmClient(_ => Task.FromResult("No, these are different entities"));
        var resolver = new EntityResolver(_graph, llmClient: llm, threshold: 0.85);

        // Act
        var kept = await resolver.FindAndMergeAsync(useLlmConfirmation: true);

        // Assert — LLM rejected the merge, both entities should still exist
        Assert.Empty(kept);
        var allEntities = await _graph.GetAllEntitiesAsync();
        Assert.Equal(2, allEntities.Count);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
