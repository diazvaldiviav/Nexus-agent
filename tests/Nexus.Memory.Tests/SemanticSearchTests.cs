using Microsoft.Data.Sqlite;
using Nexus.Memory;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Memory.Tests;

public class SemanticSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;
    private readonly SemanticSearch _search;

    public SemanticSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_search_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
        _search = new SemanticSearch(_dbInit.ConnectionString);
    }

    [Fact]
    public async Task SearchByTextAsync_ShouldFindMatchingEntities()
    {
        await _graph.AddEntityAsync(new Entity
        {
            Name = "Alice Smith",
            Type = EntityType.Person,
            TextSummary = "Senior engineer at Acme Corp"
        });
        await _graph.AddEntityAsync(new Entity
        {
            Name = "Bob Johnson",
            Type = EntityType.Person,
            TextSummary = "Product manager"
        });

        var results = await _search.SearchByTextAsync("Alice");

        Assert.NotEmpty(results);
        Assert.Contains(results, e => e.Name == "Alice Smith");
    }

    [Fact]
    public async Task SearchByTextAsync_ShouldReturnEmptyForNoMatch()
    {
        await _graph.AddEntityAsync(new Entity { Name = "C#", Type = EntityType.Technology });

        var results = await _search.SearchByTextAsync("XYZ_NONEXISTENT");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_ShouldRankBySimilarity()
    {
        var e1Embedding = new float[4] { 1f, 0f, 0f, 0f };
        var e2Embedding = new float[4] { 0f, 1f, 0f, 0f };

        await _graph.AddEntityAsync(new Entity
        {
            Name = "Entity1",
            Type = EntityType.Other,
            Embedding = SemanticSearch.ToByteArray(e1Embedding)
        });
        await _graph.AddEntityAsync(new Entity
        {
            Name = "Entity2",
            Type = EntityType.Other,
            Embedding = SemanticSearch.ToByteArray(e2Embedding)
        });

        var queryEmbedding = new float[4] { 0.9f, 0.1f, 0f, 0f };
        var results = await _search.SearchByEmbeddingAsync(queryEmbedding, topK: 2);

        Assert.NotEmpty(results);
        Assert.Equal("Entity1", results[0].Name);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
