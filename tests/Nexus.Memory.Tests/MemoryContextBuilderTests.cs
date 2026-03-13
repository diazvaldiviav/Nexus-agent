using Microsoft.Data.Sqlite;
using Nexus.Memory.Models;
using Nexus.Memory.Tests.Fakes;

namespace Nexus.Memory.Tests;

public class MemoryContextBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly SemanticSearch _search;

    public MemoryContextBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_mcb_test_{Guid.NewGuid():N}.db");
        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.Initialize();
        _graph = new KnowledgeGraph(dbInit.ConnectionString);
        _search = new SemanticSearch(dbInit.ConnectionString);
    }

    [Fact]
    public async Task BuildContextAsync_GeneratesQueryEmbedding()
    {
        // Arrange
        var fakeEmbedding = new FakeEmbeddingService();
        var builder = new MemoryContextBuilder(_graph, _search, fakeEmbedding);

        // Act
        await builder.BuildContextAsync("test query");

        // Assert
        Assert.Equal(1, fakeEmbedding.CallCount);
        Assert.Contains("test query", fakeEmbedding.CalledWithTexts);
    }

    [Fact]
    public async Task BuildContextAsync_UsesEmbeddingForSearch()
    {
        // Arrange — create an entity with an embedding in the DB
        var entityEmbedding = new float[4] { 0.9f, 0.1f, 0f, 0f };
        var entity = new Entity
        {
            Name = "CSharp",
            Type = EntityType.Technology,
            TextSummary = "A programming language",
            Embedding = SemanticSearch.ToByteArray(entityEmbedding),
            MemoryLevel = MemoryLevel.Relevant,
            RelevanceScore = 1.0
        };
        await _graph.AddEntityAsync(entity);

        // FakeEmbeddingService returns a similar embedding
        var queryEmbedding = new float[4] { 0.8f, 0.2f, 0f, 0f };
        var fakeEmbedding = new FakeEmbeddingService(queryEmbedding);
        var builder = new MemoryContextBuilder(_graph, _search, fakeEmbedding, maxRetrievalNodes: 10);

        // Act
        var context = await builder.BuildContextAsync("CSharp programming");

        // Assert — entity found via embedding search
        Assert.NotEmpty(context.RelevantMemory);
        Assert.Contains(context.RelevantMemory, e => e.Name == "CSharp");
    }

    [Fact]
    public async Task BuildContextAsync_ReturnsTopKByCosine()
    {
        // Arrange — create entities with known embeddings
        // Query will be similar to "similar1" and "similar2", dissimilar to others
        var similar1Embedding = new float[4] { 0.9f, 0.1f, 0f, 0f };
        var similar2Embedding = new float[4] { 0.8f, 0.2f, 0f, 0f };
        var dissimilar1Embedding = new float[4] { 0f, 0f, 0.9f, 0.1f };
        var dissimilar2Embedding = new float[4] { 0f, 0f, 0.1f, 0.9f };
        var dissimilar3Embedding = new float[4] { 0.1f, 0f, 0f, 0.9f };

        var entities = new[]
        {
            ("Similar1", similar1Embedding),
            ("Similar2", similar2Embedding),
            ("Dissimilar1", dissimilar1Embedding),
            ("Dissimilar2", dissimilar2Embedding),
            ("Dissimilar3", dissimilar3Embedding),
        };

        foreach (var (name, emb) in entities)
        {
            await _graph.AddEntityAsync(new Entity
            {
                Name = name,
                Type = EntityType.Other,
                TextSummary = $"Entity {name}",
                Embedding = SemanticSearch.ToByteArray(emb),
                MemoryLevel = MemoryLevel.Relevant,
                RelevanceScore = 1.0
            });
        }

        // Query embedding is very similar to similar1 and similar2
        var queryEmbedding = new float[4] { 1.0f, 0f, 0f, 0f };
        var fakeEmbedding = new FakeEmbeddingService(queryEmbedding);
        var builder = new MemoryContextBuilder(_graph, _search, fakeEmbedding, maxRetrievalNodes: 3);

        // Act
        var context = await builder.BuildContextAsync("test query");

        // Assert — top results should be the similar entities
        Assert.True(context.RelevantMemory.Count <= 3);
        Assert.Equal("Similar1", context.RelevantMemory[0].Name);
        Assert.Equal("Similar2", context.RelevantMemory[1].Name);
    }

    [Fact]
    public async Task BuildContextAsync_EmbeddingServiceNull_FallsBackToTextSearch()
    {
        // Arrange — no IEmbeddingService injected, entity name matches query
        await _graph.AddEntityAsync(new Entity
        {
            Name = "Avalonia",
            Type = EntityType.Technology,
            TextSummary = "Cross-platform UI framework",
            MemoryLevel = MemoryLevel.Relevant,
            RelevanceScore = 1.0
        });

        var builder = new MemoryContextBuilder(_graph, _search, embeddingService: null, maxRetrievalNodes: 10);

        // Act
        var context = await builder.BuildContextAsync("Avalonia");

        // Assert — entity found via text search fallback
        Assert.NotEmpty(context.RelevantMemory);
        Assert.Contains(context.RelevantMemory, e => e.Name == "Avalonia");
    }

    [Fact]
    public async Task BuildContextAsync_EmbeddingServiceThrows_FallsBackToTextSearch()
    {
        // Arrange — FakeEmbeddingService throws, entity name matches query
        await _graph.AddEntityAsync(new Entity
        {
            Name = "Ollama",
            Type = EntityType.Technology,
            TextSummary = "Local LLM runtime",
            MemoryLevel = MemoryLevel.Relevant,
            RelevanceScore = 1.0
        });

        var fakeEmbedding = new FakeEmbeddingService(exception: new HttpRequestException("Connection refused"));
        var builder = new MemoryContextBuilder(_graph, _search, fakeEmbedding, maxRetrievalNodes: 10);

        // Act
        var context = await builder.BuildContextAsync("Ollama");

        // Assert — entity found via text search fallback despite embedding failure
        Assert.NotEmpty(context.RelevantMemory);
        Assert.Contains(context.RelevantMemory, e => e.Name == "Ollama");
        Assert.Equal(1, fakeEmbedding.CallCount); // Embedding was attempted
    }

    [Fact]
    public void FormatContextAsPrompt_IncludesRelevantMemorySection()
    {
        // Arrange
        var context = new MemoryContext
        {
            RelevantMemory = new List<Entity>
            {
                new()
                {
                    Name = "Nexus",
                    Type = EntityType.Project,
                    TextSummary = "AI agent with memory",
                    RelevanceScore = 0.95
                },
                new()
                {
                    Name = "SQLite",
                    Type = EntityType.Technology,
                    TextSummary = "Embedded database",
                    RelevanceScore = 0.80
                }
            }
        };

        var builder = new MemoryContextBuilder(_graph, _search);

        // Act
        var prompt = builder.FormatContextAsPrompt(context);

        // Assert
        Assert.Contains("Relevant Context", prompt);
        Assert.Contains("Nexus", prompt);
        Assert.Contains("AI agent with memory", prompt);
        Assert.Contains("SQLite", prompt);
        Assert.Contains("Embedded database", prompt);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
