using Microsoft.Data.Sqlite;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;
using Nexus.Memory.Models;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for entity deduplication in AgentService and EntityResolver.
/// AC-7: CLI dedupe commands
/// AC-9: Background dedup in AgentService
/// </summary>
public class DeduplicationIntegrationTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public DeduplicationIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dedup_test_{Guid.NewGuid():N}.db");
        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.Initialize();
        _connectionString = dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_lastAgent is not null)
        {
            await _lastAgent.FlushPendingExtractionAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task AgentService_BackgroundDedup_CallsEntityResolver()
    {
        // Arrange: Seed two entities with near-identical embeddings (above 0.85 threshold)
        var vector1 = new float[768];
        vector1[0] = 1.0f;
        vector1[1] = 0.05f;

        var vector2 = new float[768];
        vector2[0] = 1.0f;
        vector2[1] = 0.06f;

        await _graph.AddEntityAsync(new Entity
        {
            Name = "CSharp",
            Type = EntityType.Technology,
            TextSummary = "A programming language",
            Embedding = SemanticSearch.ToByteArray(vector1),
            RelevanceScore = 0.9,
            MentionCount = 3
        });

        await _graph.AddEntityAsync(new Entity
        {
            Name = "C Sharp",
            Type = EntityType.Technology,
            TextSummary = "C# programming language by Microsoft",
            Embedding = SemanticSearch.ToByteArray(vector2),
            RelevanceScore = 0.8,
            MentionCount = 1
        });

        var entitiesBefore = await _graph.GetAllEntitiesAsync();
        Assert.Equal(2, entitiesBefore.Count);

        // Create AgentService with EntityResolver
        // AC-H1: disable Phase 9 defaults — dedup test uses ChatAsync with a fake LLM and no
        // tool calls; disabling is precautionary to guard against future tool injection paths.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider("ollama", _ => "Hello, I can help with that.");
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var resolver = new EntityResolver(_graph, threshold: 0.85);

        var agent = new AgentService(config, _graph, promptBuilder, modelRouter,
            entityExtractor, providerFactory, summarizer,
            toolExecutor: null, entityResolver: resolver);
        _lastAgent = agent;

        // Act: Chat triggers background extraction + dedup
        await agent.ChatAsync("Tell me about programming languages");
        await agent.FlushPendingExtractionAsync();

        // Assert: The original duplicate pair was merged — only one of the two names should remain
        var entitiesAfter = await _graph.GetAllEntitiesAsync();
        var csharpNames = entitiesAfter.Where(e =>
            string.Equals(e.Name, "CSharp", StringComparison.Ordinal) ||
            string.Equals(e.Name, "C Sharp", StringComparison.Ordinal)).ToList();
        Assert.Single(csharpNames);

        // The survivor should have the combined mention count
        var survivor = csharpNames[0];
        Assert.True(survivor.MentionCount >= 4,
            $"Expected combined mention count >= 4, but got {survivor.MentionCount}");
    }

    [Fact]
    public async Task AgentService_BackgroundDedup_NeverThrows_WhenResolverFails()
    {
        // Arrange: Create AgentService with a resolver whose graph will cause issues
        // We use a valid graph but create a scenario where FindAndMergeAsync works
        // but even if it threw, the ChatAsync should still complete
        // AC-H1: disable Phase 9 defaults — precautionary guard matching sibling dedup tests.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider("ollama", _ => "Sure, here's the answer.");
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        // Create a resolver with a non-existent DB path to force SQLite errors during dedup
        var brokenDbPath = Path.Combine(Path.GetTempPath(), $"broken_{Guid.NewGuid():N}.db");
        var brokenConnStr = $"Data Source={brokenDbPath}";
        var brokenGraph = new KnowledgeGraph(brokenConnStr);

        var resolver = new EntityResolver(brokenGraph, threshold: 0.85);

        var agent = new AgentService(config, _graph, promptBuilder, modelRouter,
            entityExtractor, providerFactory, summarizer,
            toolExecutor: null, entityResolver: resolver);
        _lastAgent = agent;

        // Act: ChatAsync should complete successfully despite resolver failure
        var response = await agent.ChatAsync("Hello world");
        await agent.FlushPendingExtractionAsync();

        // Assert: Got a valid response (dedup failure was swallowed)
        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }

    [Fact]
    public async Task CLI_MemoryDedupe_FindsDuplicates()
    {
        // Arrange: Seed entities with highly similar embeddings
        var vec1 = new float[768];
        vec1[0] = 1.0f;
        vec1[1] = 0.02f;

        var vec2 = new float[768];
        vec2[0] = 1.0f;
        vec2[1] = 0.03f;

        await _graph.AddEntityAsync(new Entity
        {
            Name = "JavaScript",
            Type = EntityType.Technology,
            TextSummary = "A scripting language",
            Embedding = SemanticSearch.ToByteArray(vec1),
            RelevanceScore = 0.9
        });

        await _graph.AddEntityAsync(new Entity
        {
            Name = "JS",
            Type = EntityType.Technology,
            TextSummary = "JavaScript language",
            Embedding = SemanticSearch.ToByteArray(vec2),
            RelevanceScore = 0.7
        });

        var resolver = new EntityResolver(_graph, threshold: 0.85);

        // Act
        var duplicates = await resolver.FindDuplicatesAsync();

        // Assert: Should find the pair as duplicates (cosine similarity of near-identical vectors > 0.85)
        Assert.NotEmpty(duplicates);
        Assert.Contains(duplicates, p =>
            (p.Entity1.Name == "JavaScript" && p.Entity2.Name == "JS") ||
            (p.Entity1.Name == "JS" && p.Entity2.Name == "JavaScript"));
        Assert.All(duplicates, p => Assert.True(p.Similarity >= 0.85));
    }

    [Fact]
    public void DeduplicationThreshold_DefaultValue_Is085()
    {
        // Arrange
        // AC-H1: verified compatible with Phase 9 defaults-ON — only reads a config value,
        // no AgentService, tool loop, or plan path involved.
        var config = new NexusConfig();

        // Act & Assert: Config regression — default threshold must be 0.85
        Assert.Equal(0.85, config.Memory.DeduplicationThreshold);
    }
}
