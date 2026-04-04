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
using Xunit;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for cloud provider flow and summarization flow.
/// AC-5: Cloud flow -> extraction -> memory -> retrieval
/// AC-6: Summarization -> embedding -> retrieval
/// </summary>
public class CloudFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;

    public CloudFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cloud_flow_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _connectionString = _dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
    }

    [Fact]
    public async Task Flow4_CloudChat_ExtractsEntitiesAndAppearsInContext()
    {
        // Arrange: FakeLlmProvider returns text with entity-like patterns
        // EntityExtractor heuristic fallback picks up capitalized proper nouns
        var fakeProvider = new FakeLlmProvider("fake", _ =>
            "Alice is working on the Nexus project using Docker and Python. " +
            "She discussed the architecture with Bob at Contoso.");

        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        var fakeEmbedding = new float[768];
        fakeEmbedding[0] = 0.7f;
        var fakeEmbeddingService = new FakeEmbeddingService(fakeEmbedding);

        // EntityExtractor with no LLM client -> will fall through to heuristic
        var entityExtractor = new EntityExtractor(_graph, llmClient: null, fakeEmbeddingService);

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search, fakeEmbeddingService);
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Provider = "fake", Model = "test-model" }
            }
        };
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var summarizer = new InteractionSummarizer(_graph);

        var agentService = new AgentService(
            config, _graph, promptBuilder, modelRouter,
            entityExtractor, providerFactory, summarizer);

        // Act: Send a message through AgentService
        var response = await agentService.ChatAsync("Tell me about Alice and the Nexus project");

        // Wait for background extraction to complete
        await agentService.FlushPendingExtractionAsync();

        // Assert: Response came from the FakeLlmProvider
        Assert.Contains("Alice", response.Content);
        Assert.Contains("Nexus", response.Content);

        // Assert: Entities were extracted and persisted via heuristic
        var allEntities = await _graph.GetAllEntitiesAsync();
        Assert.NotEmpty(allEntities);

        // Heuristic extracts capitalized words: Alice, Nexus, Docker, Python, Bob, Contoso
        Assert.Contains(allEntities, e => e.Name == "Alice");
        Assert.Contains(allEntities, e => e.Name == "Nexus");

        // Assert: Entities have embeddings (from FakeEmbeddingService)
        var entitiesWithEmbeddings = allEntities.Where(e => e.Embedding is not null).ToList();
        Assert.NotEmpty(entitiesWithEmbeddings);

        // Assert: MemoryContextBuilder retrieves entities in context
        var context = await memoryBuilder.BuildContextAsync("Alice Nexus");
        var allMemory = context.WorkingMemory.Concat(context.RelevantMemory).ToList();

        // Text search fallback should find entities matching "Alice Nexus"
        // or embedding search should find them via the fake embedding
        Assert.NotEmpty(allMemory);
    }

    [Fact]
    public async Task Flow5_Summarization_PersistsAndAppearsInContext()
    {
        // Arrange: MockLlmClient that returns a summary
        var mockLlm = new MockLlmClient(_ =>
            Task.FromResult("Alice discussed Nexus project architecture with Bob."));

        var fakeEmbedding = new float[768];
        fakeEmbedding[0] = 0.7f;
        var fakeEmbeddingService = new FakeEmbeddingService(fakeEmbedding);

        var summarizer = new InteractionSummarizer(_graph, mockLlm, fakeEmbeddingService);

        // Act: Summarize a conversation
        var conversationText = "user: Tell me about the Nexus project\nassistant: Nexus is an AI agent with persistent memory.";
        var summaryPrompt = "Summarize this conversation.";
        var interaction = await summarizer.SummarizeAsync(conversationText, summaryPrompt);

        // Assert: Interaction was created with summary and embedding
        Assert.NotNull(interaction);
        Assert.NotEmpty(interaction.Summary);
        Assert.Contains("Alice", interaction.Summary);
        Assert.NotNull(interaction.Embedding);
        Assert.True(interaction.Embedding.Length > 0);

        // Assert: Interaction was persisted in the database
        var interactionCount = await _graph.GetInteractionCountAsync();
        Assert.Equal(1, interactionCount);

        // Assert: MemoryContextBuilder retrieves the interaction
        var search = new SemanticSearch(_connectionString);
        var builder = new MemoryContextBuilder(_graph, search, fakeEmbeddingService);
        var context = await builder.BuildContextAsync("Nexus architecture");

        Assert.NotEmpty(context.RecentInteractions);
        Assert.Contains(context.RecentInteractions, i => i.Summary.Contains("Alice"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
