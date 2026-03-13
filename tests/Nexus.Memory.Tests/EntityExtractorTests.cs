using Microsoft.Data.Sqlite;
using Nexus.Memory;
using Nexus.Memory.Models;
using Nexus.Memory.Tests.Fakes;
using Xunit;

namespace Nexus.Memory.Tests;

public class EntityExtractorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;

    public EntityExtractorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_extractor_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
    }

    // Hand-rolled mock for ILlmClient (single-method interface)
    private sealed class MockLlmClient : ILlmClient
    {
        private readonly Func<string, Task<string>> _handler;
        public string? LastPrompt { get; private set; }

        public MockLlmClient(Func<string, Task<string>> handler)
        {
            _handler = handler;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return _handler(prompt);
        }
    }

    [Fact]
    public async Task ExtractAndPersist_ValidLlmJson_CreatesEntities()
    {
        // Arrange
        var llmResponse = """
            {
              "entities": [
                {"name": "Alice", "type": "person", "summary": "A software engineer"},
                {"name": "Nexus", "type": "project", "summary": "AI agent project"}
              ],
              "relations": [
                {"entity1": "Alice", "entity2": "Nexus", "type": "works_on"}
              ]
            }
            """;
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync(
            "User: Alice works on Nexus", "Extract entities from this text");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Name == "Alice" && e.Type == EntityType.Person);
        Assert.Contains(result, e => e.Name == "Nexus" && e.Type == EntityType.Project);
    }

    [Fact]
    public async Task ExtractAndPersist_UsesProvidedExtractionPrompt()
    {
        // Arrange
        var llmResponse = """{"entities": [{"name": "Test", "type": "other", "summary": "test"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);
        var expectedPrompt = "Extract entities from: User said hello";

        // Act
        await extractor.ExtractAndPersistAsync("User said hello", expectedPrompt);

        // Assert
        Assert.Equal(expectedPrompt, mockLlm.LastPrompt);
    }

    [Fact]
    public async Task ExtractAndPersist_NewEntity_HasCorrectDefaults()
    {
        // Arrange
        var llmResponse = """{"entities": [{"name": "CSharp", "type": "technology", "summary": "Programming language"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var entity = result.Single();
        Assert.Equal(1, entity.MentionCount);
        Assert.Equal(1.0, entity.RelevanceScore);
        Assert.Equal(MemoryLevel.Relevant, entity.MemoryLevel);
        Assert.Equal("Programming language", entity.TextSummary);
    }

    [Fact]
    public async Task ExtractAndPersist_ExistingEntity_UpdatesMentionCount()
    {
        // Arrange: pre-populate an entity
        var existing = new Entity
        {
            Name = "Alice",
            Type = EntityType.Person,
            TextSummary = "A developer",
            MentionCount = 1
        };
        var beforeTime = DateTime.UtcNow;
        await _graph.AddEntityAsync(existing);

        var llmResponse = """{"entities": [{"name": "Alice", "type": "person", "summary": "A developer"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var updated = result.Single();
        Assert.Equal(2, updated.MentionCount);
        Assert.True(updated.LastMentioned >= beforeTime);
    }

    [Fact]
    public async Task ExtractAndPersist_ExistingEntity_SummaryUpdatedIfMoreDetailed()
    {
        // Arrange: existing entity with short summary
        var existing = new Entity
        {
            Name = "Nexus",
            Type = EntityType.Project,
            TextSummary = "AI project"
        };
        await _graph.AddEntityAsync(existing);

        var llmResponse = """{"entities": [{"name": "Nexus", "type": "project", "summary": "An advanced AI agent with persistent memory capabilities"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var updated = result.Single();
        Assert.Equal("An advanced AI agent with persistent memory capabilities", updated.TextSummary);
    }

    [Fact]
    public async Task ExtractAndPersist_ExistingEntity_TypeUpdatedFromOther()
    {
        // Arrange: existing entity with type Other
        var existing = new Entity
        {
            Name = "Docker",
            Type = EntityType.Other,
            TextSummary = "Mentioned"
        };
        await _graph.AddEntityAsync(existing);

        var llmResponse = """{"entities": [{"name": "Docker", "type": "technology", "summary": "Container platform"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var updated = result.Single();
        Assert.Equal(EntityType.Technology, updated.Type);
    }

    [Fact]
    public async Task ExtractAndPersist_RelationsCreated_BetweenEntities()
    {
        // Arrange
        var llmResponse = """
            {
              "entities": [
                {"name": "Alice", "type": "person", "summary": "Engineer"},
                {"name": "Nexus", "type": "project", "summary": "AI agent"}
              ],
              "relations": [
                {"entity1": "Alice", "entity2": "Nexus", "type": "works_on"}
              ]
            }
            """;
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var alice = result.First(e => e.Name == "Alice");
        var relations = await _graph.GetRelationsForEntityAsync(alice.Id);
        Assert.Single(relations);
        Assert.Equal("works_on", relations[0].RelationType);
    }

    [Fact]
    public async Task ExtractAndPersist_DuplicateRelations_NotCreated()
    {
        // Arrange: first extraction creates the relation
        var llmResponse = """
            {
              "entities": [
                {"name": "Bob", "type": "person", "summary": "PM"},
                {"name": "Alpha", "type": "project", "summary": "Project"}
              ],
              "relations": [
                {"entity1": "Bob", "entity2": "Alpha", "type": "manages"}
              ]
            }
            """;
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act: extract twice with same data
        await extractor.ExtractAndPersistAsync("text", "prompt");
        await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert: only one relation exists
        var bob = await _graph.GetEntityByNameAsync("Bob");
        Assert.NotNull(bob);
        var relations = await _graph.GetRelationsForEntityAsync(bob.Id);
        Assert.Single(relations);
    }

    [Fact]
    public async Task ExtractAndPersist_InvalidJson_FallsBackToHeuristic()
    {
        // Arrange: LLM returns garbage
        var mockLlm = new MockLlmClient(_ => Task.FromResult("This is not JSON at all!"));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act: use text with capitalized words for heuristic to find
        var result = await extractor.ExtractAndPersistAsync(
            "Alice works at Microsoft on Docker projects", "prompt");

        // Assert: heuristic extraction runs and finds capitalized words
        Assert.NotEmpty(result);
        Assert.Contains(result, e => e.Name == "Alice" || e.Name == "Microsoft" || e.Name == "Docker");
    }

    [Fact]
    public async Task ExtractAndPersist_LlmThrowsException_FallsBackToHeuristic()
    {
        // Arrange: LLM throws HttpRequestException
        var mockLlm = new MockLlmClient(_ =>
            throw new HttpRequestException("Ollama is not running"));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync(
            "Alice uses Docker for development", "prompt");

        // Assert: heuristic extraction runs without crash
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExtractAndPersist_JsonInCodeBlock_ParsedCorrectly()
    {
        // Arrange: LLM wraps JSON in markdown code fences
        var llmResponse = """
            ```json
            {
              "entities": [
                {"name": "Python", "type": "technology", "summary": "Programming language"}
              ],
              "relations": []
            }
            ```
            """;
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        Assert.Single(result);
        Assert.Equal("Python", result[0].Name);
        Assert.Equal(EntityType.Technology, result[0].Type);
    }

    [Fact]
    public async Task ExtractAndPersist_NoLlmClient_UsesHeuristicOnly()
    {
        // Arrange: no LLM client (null)
        var extractor = new EntityExtractor(_graph);

        // Act
        var result = await extractor.ExtractAndPersistAsync(
            "Alice and Bob discussed Python at Google");

        // Assert: heuristic extraction finds capitalized words
        Assert.NotEmpty(result);
        Assert.Contains(result, e => e.Name == "Alice" || e.Name == "Bob");
    }

    [Fact]
    public void TryParseExtractionJson_TrailingCommas_Handled()
    {
        // Arrange: JSON with trailing commas
        var json = """
            {
              "entities": [
                {"name": "Test", "type": "other", "summary": "desc",},
              ],
              "relations": [],
            }
            """;

        // Act
        var result = EntityExtractor.TryParseExtractionJson(json);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Entities);
        Assert.Equal("Test", result.Entities[0].Name);
    }

    [Fact]
    public void TryParseExtractionJson_EmptyString_ReturnsNull()
    {
        Assert.Null(EntityExtractor.TryParseExtractionJson(""));
        Assert.Null(EntityExtractor.TryParseExtractionJson("   "));
        Assert.Null(EntityExtractor.TryParseExtractionJson(null!));
    }

    [Fact]
    public async Task ExtractAndPersist_NewEntity_GeneratesEmbedding()
    {
        // Arrange
        var fakeEmbedding = new float[768];
        fakeEmbedding[0] = 0.42f;
        var fakeService = new FakeEmbeddingService(fakeEmbedding);
        var llmResponse = """{"entities": [{"name": "Alice", "type": "person", "summary": "A software engineer"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm, fakeService);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var entity = result.Single();
        Assert.NotNull(entity.Embedding);
        Assert.Equal(768 * 4, entity.Embedding.Length);
        Assert.Equal(1, fakeService.CallCount);
        Assert.Contains("Alice", fakeService.CalledWithTexts[0]);
        Assert.Contains("A software engineer", fakeService.CalledWithTexts[0]);
    }

    [Fact]
    public async Task ExtractAndPersist_EmbeddingServiceFails_EntityCreatedWithNullEmbedding()
    {
        // Arrange
        var fakeService = new FakeEmbeddingService(exception: new HttpRequestException("Ollama down"));
        var llmResponse = """{"entities": [{"name": "Bob", "type": "person", "summary": "A manager"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm, fakeService);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var entity = result.Single();
        Assert.Null(entity.Embedding);
        Assert.Equal("Bob", entity.Name);
        Assert.Equal(1, fakeService.CallCount);
    }

    [Fact]
    public async Task ExtractAndPersist_EmbeddingServiceNull_EntityCreatedWithNullEmbedding()
    {
        // Arrange: no IEmbeddingService injected
        var llmResponse = """{"entities": [{"name": "Carol", "type": "person", "summary": "A designer"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        var entity = result.Single();
        Assert.Null(entity.Embedding);
    }

    [Fact]
    public async Task ExtractAndPersist_ExistingEntityWithUpdatedSummary_RegeneratesEmbedding()
    {
        // Arrange: pre-populate entity with short summary
        var existing = new Entity
        {
            Name = "Nexus",
            Type = EntityType.Project,
            TextSummary = "AI project"
        };
        await _graph.AddEntityAsync(existing);

        var fakeService = new FakeEmbeddingService();
        var llmResponse = """{"entities": [{"name": "Nexus", "type": "project", "summary": "An advanced AI agent with persistent memory capabilities"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm, fakeService);

        // Act
        var result = await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert
        Assert.Equal(1, fakeService.CallCount);
        var entity = result.Single();
        Assert.NotNull(entity.Embedding);
        Assert.Contains("Nexus", fakeService.CalledWithTexts[0]);
    }

    [Fact]
    public async Task ExtractAndPersist_ExistingEntitySameSummary_NoEmbeddingRegeneration()
    {
        // Arrange: pre-populate entity with same-length summary
        var existing = new Entity
        {
            Name = "Docker",
            Type = EntityType.Technology,
            TextSummary = "Container platform for building apps"
        };
        await _graph.AddEntityAsync(existing);

        var fakeService = new FakeEmbeddingService();
        // LLM returns a shorter summary — no update triggered
        var llmResponse = """{"entities": [{"name": "Docker", "type": "technology", "summary": "Container platform"}], "relations": []}""";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));
        var extractor = new EntityExtractor(_graph, mockLlm, fakeService);

        // Act
        await extractor.ExtractAndPersistAsync("text", "prompt");

        // Assert: embedding service should NOT have been called (no summary change)
        Assert.Equal(0, fakeService.CallCount);
    }

    [Fact]
    public async Task AddEntityAsync_PersistsEmbeddingBlob()
    {
        // Arrange: create entity with byte[] embedding and persist
        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        var blob = SemanticSearch.ToByteArray(floats);
        var entity = new Entity
        {
            Name = "BlobTest",
            Type = EntityType.Technology,
            TextSummary = "Testing BLOB persistence",
            Embedding = blob
        };

        // Act
        await _graph.AddEntityAsync(entity);
        var retrieved = await _graph.GetEntityByNameAsync("BlobTest");

        // Assert
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Embedding);
        Assert.Equal(blob.Length, retrieved.Embedding.Length);
        Assert.Equal(blob, retrieved.Embedding);
    }

    [Fact]
    public async Task ExtractAndPersist_HeuristicFallback_GeneratesEmbedding()
    {
        // Arrange: no LLM client, no Gemini — heuristic path
        var fakeService = new FakeEmbeddingService();
        var extractor = new EntityExtractor(_graph, embeddingService: fakeService);

        // Act
        var result = await extractor.ExtractAndPersistAsync(
            "Alice and Bob discussed Python at Google");

        // Assert: heuristic entities created with embeddings
        Assert.NotEmpty(result);
        Assert.True(fakeService.CallCount > 0);
        Assert.All(result, e => Assert.NotNull(e.Embedding));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
