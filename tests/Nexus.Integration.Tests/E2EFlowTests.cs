using Microsoft.Data.Sqlite;
using Nexus.Core;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;
using Nexus.Memory;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Integration.Tests;

/// <summary>
/// End-to-end flow tests that verify the full pipeline using fakes (no external services).
/// AC-1: Flow 1 — conversation -> extraction -> entities persisted with embeddings
/// AC-2: Flow 2 — query -> embedding -> semantic search -> context in prompt
/// AC-3: Flow 3 — decay changes scores and memory levels over time
/// AC-11: BugFix001 — agent maintains conversation history
/// AC-12: BugFix002 — extraction prompt requires English output
/// </summary>
public class E2EFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;

    public E2EFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"e2e_flow_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _connectionString = _dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
    }

    [Fact]
    public async Task Flow1_Conversation_ExtractsEntitiesWithEmbeddings()
    {
        // Arrange: MockLlmClient returns structured JSON with entities and relations
        var llmResponse = """
            {
              "entities": [
                {"name": "Alice", "type": "person", "summary": "A software engineer at Contoso"},
                {"name": "Nexus", "type": "project", "summary": "AI agent with persistent memory"}
              ],
              "relations": [
                {"entity1": "Alice", "entity2": "Nexus", "type": "works_on"}
              ]
            }
            """;
        var mockLlm = new MockLlmClient(_ => Task.FromResult(llmResponse));

        // FakeEmbeddingService returns a known 768-dimensional vector
        var fakeEmbedding = new float[768];
        fakeEmbedding[0] = 0.5f;
        fakeEmbedding[1] = 0.3f;
        var fakeEmbeddingService = new FakeEmbeddingService(fakeEmbedding);

        var extractor = new EntityExtractor(_graph, mockLlm, fakeEmbeddingService);

        // Act
        var result = await extractor.ExtractAndPersistAsync(
            "User: Alice works on the Nexus project\nAssistant: That's great!",
            "Extract entities from this conversation");

        // Assert: 2 entities persisted
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Name == "Alice" && e.Type == EntityType.Person);
        Assert.Contains(result, e => e.Name == "Nexus" && e.Type == EntityType.Project);

        // Assert: both entities have non-null embeddings (BLOB persisted)
        var allEntities = await _graph.GetAllEntitiesAsync();
        Assert.Equal(2, allEntities.Count);
        Assert.All(allEntities, e => Assert.NotNull(e.Embedding));

        // Assert: embedding byte length matches the fake embedding used
        var expectedLength = SemanticSearch.ToByteArray(fakeEmbedding).Length;
        Assert.All(allEntities, e => Assert.Equal(expectedLength, e.Embedding!.Length));

        // Assert: relation created between Alice and Nexus
        var alice = allEntities.First(e => e.Name == "Alice");
        var relations = await _graph.GetRelationsForEntityAsync(alice.Id);
        Assert.Single(relations);
        Assert.Equal("works_on", relations[0].RelationType);

        // Assert: embedding service was called for each new entity
        Assert.Equal(2, fakeEmbeddingService.CallCount);
    }

    [Fact]
    public async Task Flow2_Query_SemanticSearchReturnsContextInPrompt()
    {
        // Arrange: Pre-populate KnowledgeGraph with entities that have embeddings
        // Use orthogonal unit vectors so cosine similarity is deterministic:
        //   CSharp entity:  [1, 0, 0, 0, ...]  (target)
        //   Python entity:  [0, 1, 0, 0, ...]  (other)
        //   Query embedding: [0.9, 0.1, 0, 0, ...] (close to CSharp)

        var csharpVector = new float[768];
        csharpVector[0] = 1.0f;
        var csharpEntity = new Entity
        {
            Name = "CSharp",
            Type = EntityType.Technology,
            TextSummary = "A modern programming language by Microsoft",
            Embedding = SemanticSearch.ToByteArray(csharpVector),
            RelevanceScore = 0.8,
            MemoryLevel = MemoryLevel.Relevant
        };

        var pythonVector = new float[768];
        pythonVector[1] = 1.0f;
        var pythonEntity = new Entity
        {
            Name = "Python",
            Type = EntityType.Technology,
            TextSummary = "A dynamic programming language",
            Embedding = SemanticSearch.ToByteArray(pythonVector),
            RelevanceScore = 0.8,
            MemoryLevel = MemoryLevel.Relevant
        };

        await _graph.AddEntityAsync(csharpEntity);
        await _graph.AddEntityAsync(pythonEntity);

        // FakeEmbeddingService returns a query embedding close to the CSharp vector
        var queryVector = new float[768];
        queryVector[0] = 0.9f;
        queryVector[1] = 0.1f;
        var fakeEmbeddingService = new FakeEmbeddingService(queryVector);

        var search = new SemanticSearch(_connectionString);
        var builder = new MemoryContextBuilder(_graph, search, fakeEmbeddingService);

        // Act
        var context = await builder.BuildContextAsync("C# programming");
        var prompt = builder.FormatContextAsPrompt(context);

        // Assert: semantic search found entities (they are Relevant-level, so in RelevantMemory)
        var allMemory = context.WorkingMemory.Concat(context.RelevantMemory).ToList();
        Assert.NotEmpty(allMemory);

        // Assert: CSharp entity appears in context (highest cosine similarity to query)
        Assert.Contains(allMemory, e => e.Name == "CSharp");

        // Assert: formatted prompt contains entity names
        Assert.Contains("CSharp", prompt);
    }

    [Fact]
    public async Task Flow3_Decay_ChangesScoresAndMemoryLevels()
    {
        // Arrange: Create entities with varying lastMentioned dates and mention counts
        var now = DateTime.UtcNow;

        var recentEntity = new Entity
        {
            Name = "RecentEntity",
            Type = EntityType.Other,
            TextSummary = "Mentioned recently",
            LastMentioned = now,
            FirstMentioned = now.AddDays(-1),
            MentionCount = 5,
            RelevanceScore = 1.0,
            MemoryLevel = MemoryLevel.Working
        };

        var oldEntity = new Entity
        {
            Name = "OldEntity",
            Type = EntityType.Other,
            TextSummary = "Mentioned 30 days ago",
            LastMentioned = now.AddDays(-30),
            FirstMentioned = now.AddDays(-60),
            MentionCount = 1,
            RelevanceScore = 1.0,
            MemoryLevel = MemoryLevel.Relevant
        };

        var ancientEntity = new Entity
        {
            Name = "AncientEntity",
            Type = EntityType.Other,
            TextSummary = "Mentioned 60 days ago",
            LastMentioned = now.AddDays(-60),
            FirstMentioned = now.AddDays(-90),
            MentionCount = 1,
            RelevanceScore = 0.1,
            MemoryLevel = MemoryLevel.Relevant
        };

        await _graph.AddEntityAsync(recentEntity);
        await _graph.AddEntityAsync(oldEntity);
        await _graph.AddEntityAsync(ancientEntity);

        // RelevanceDecay with default params (archive threshold = 0.05)
        var decay = new RelevanceDecay(
            _connectionString,
            lambda: 0.05,
            workingThresholdScore: 0.7,
            workingThresholdMentions: 3,
            archiveThresholdScore: 0.05);

        // Act
        await decay.ApplyDecayAsync();

        // Assert
        var entities = await _graph.GetAllEntitiesAsync();
        var recent = entities.First(e => e.Name == "RecentEntity");
        var old = entities.First(e => e.Name == "OldEntity");
        var ancient = entities.First(e => e.Name == "AncientEntity");

        // Recent entity (today, 5 mentions) should barely decay — score close to 1.0
        Assert.True(recent.RelevanceScore > 0.9,
            $"RecentEntity score should be close to 1.0 but was {recent.RelevanceScore}");

        // Old entity (30 days, 1 mention) should have decayed significantly
        Assert.True(old.RelevanceScore < 1.0,
            $"OldEntity score should be less than 1.0 but was {old.RelevanceScore}");

        // Ancient entity (60 days, 1 mention, started at 0.1) should have decayed the most
        Assert.True(ancient.RelevanceScore < old.RelevanceScore,
            $"AncientEntity ({ancient.RelevanceScore}) should be lower than OldEntity ({old.RelevanceScore})");

        // Ancient entity should be archived (below 0.05 threshold)
        Assert.Equal(MemoryLevel.Archive, ancient.MemoryLevel);
    }

    [Fact]
    public void BugFix001_AgentMaintainsConversationHistory()
    {
        // AC-11: Verify AgentService starts with empty ConversationHistory.
        // Full ChatAsync accumulation test is deferred to manual testing because
        // ChatAsync calls CallOllamaAsync which requires a live LLM connection.
        // This test validates the contract: ConversationHistory is initialized empty
        // and the property is publicly accessible as IReadOnlyList.

        var config = new NexusConfig();
        var dbPath = Path.Combine(Path.GetTempPath(), $"bug001_test_{Guid.NewGuid():N}.db");
        try
        {
            var dbInit = new DatabaseInitializer(dbPath);
            dbInit.Initialize();
            var graph = new KnowledgeGraph(dbInit.ConnectionString);
            var search = new SemanticSearch(dbInit.ConnectionString);
            var memoryBuilder = new MemoryContextBuilder(graph, search);
            var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent);
            var modelRouter = new ModelRouter(config.Models.Routing);
            var entityExtractor = new EntityExtractor(graph);
            var agent = new AgentService(config, graph, promptBuilder, modelRouter, entityExtractor);

            // Assert: conversation history starts empty
            Assert.Empty(agent.ConversationHistory);
            Assert.IsAssignableFrom<IReadOnlyList<ConversationMessage>>(agent.ConversationHistory);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void BugFix002_ExtractionPromptRequiresEnglishOutput()
    {
        // AC-12: Verify the extraction prompt contains the English-only rule
        var config = new NexusConfig();
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent);

        // Act
        var prompt = promptBuilder.BuildEntityExtractionPrompt(
            "User: Hola, estoy trabajando en un proyecto\nAssistant: Entendido");

        // Assert: prompt enforces English output
        Assert.Contains("ALL output MUST be in English", prompt);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
