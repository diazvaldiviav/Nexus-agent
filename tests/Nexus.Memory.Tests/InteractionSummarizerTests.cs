using Microsoft.Data.Sqlite;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;
using Nexus.Memory.Models;
using Nexus.Memory.Tests.Fakes;
using Xunit;

namespace Nexus.Memory.Tests;

public class InteractionSummarizerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly KnowledgeGraph _graph;

    public InteractionSummarizerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_summarizer_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _graph = new KnowledgeGraph(_dbInit.ConnectionString);
    }

    [Fact]
    public async Task SummarizeAsync_WithLlm_GeneratesSummaryAndPersists()
    {
        // Arrange
        var expectedSummary = "The user discussed C# programming. The assistant provided guidance on async patterns.";
        var mockLlm = new MockLlmClient(_ => Task.FromResult(expectedSummary));
        var summarizer = new InteractionSummarizer(_graph, mockLlm);

        var conversationText = "user: Tell me about C# async\nassistant: C# async uses Task-based patterns.";
        var summaryPrompt = "Summarize this conversation.";
        var entityIds = new List<string> { "entity-1", "entity-2" };

        // Act
        var result = await summarizer.SummarizeAsync(conversationText, summaryPrompt, entityIds);

        // Assert
        Assert.Equal(expectedSummary, result.Summary);
        Assert.Equal(entityIds, result.ReferencedEntityIds);
        Assert.NotNull(result.Id);
        Assert.Equal(expectedSummary.Length / 4, result.TokenCount);

        // Verify persisted in DB
        var interactions = await _graph.GetRecentInteractionsAsync(10);
        Assert.Single(interactions);
        Assert.Equal(expectedSummary, interactions[0].Summary);
    }

    [Fact]
    public async Task SummarizeAsync_LlmFails_FallsBackToHeuristic()
    {
        // Arrange
        var mockLlm = new MockLlmClient(_ =>
            throw new HttpRequestException("Ollama is not running"));
        var summarizer = new InteractionSummarizer(_graph, mockLlm);

        var conversationText = "user: What is Docker?\nassistant: Docker is a container platform for building and running applications.";

        // Act
        var result = await summarizer.SummarizeAsync(conversationText, "Summarize this.");

        // Assert: heuristic uses last assistant message
        Assert.Contains("Docker is a container platform", result.Summary);

        // Verify persisted
        var interactions = await _graph.GetRecentInteractionsAsync(10);
        Assert.Single(interactions);
    }

    [Fact]
    public async Task SummarizeAsync_GeneratesEmbedding_WhenServiceAvailable()
    {
        // Arrange
        var fakeEmbedding = new float[768];
        fakeEmbedding[0] = 0.5f;
        var fakeService = new FakeEmbeddingService(fakeEmbedding);
        var mockLlm = new MockLlmClient(_ => Task.FromResult("A summary of the conversation."));
        var summarizer = new InteractionSummarizer(_graph, mockLlm, fakeService);

        // Act
        var result = await summarizer.SummarizeAsync("user: hello\nassistant: hi", "Summarize.");

        // Assert
        Assert.NotNull(result.Embedding);
        var expectedLength = SemanticSearch.ToByteArray(fakeEmbedding).Length;
        Assert.Equal(expectedLength, result.Embedding.Length);
        Assert.Equal(1, fakeService.CallCount);

        // Verify persisted with embedding
        var interactions = await _graph.GetRecentInteractionsAsync(10);
        Assert.NotNull(interactions[0].Embedding);
    }

    [Fact]
    public async Task SummarizeAsync_NoEmbeddingService_PersistsWithoutEmbedding()
    {
        // Arrange
        var mockLlm = new MockLlmClient(_ => Task.FromResult("Summary without embedding."));
        var summarizer = new InteractionSummarizer(_graph, mockLlm);

        // Act
        var result = await summarizer.SummarizeAsync("user: test\nassistant: response", "Summarize.");

        // Assert
        Assert.Null(result.Embedding);
        Assert.Equal("Summary without embedding.", result.Summary);

        // Verify persisted
        var interactions = await _graph.GetRecentInteractionsAsync(10);
        Assert.Single(interactions);
        Assert.Null(interactions[0].Embedding);
    }

    [Fact]
    public async Task SummarizeAsync_NoLlmClient_UsesHeuristic()
    {
        // Arrange: no LLM client
        var summarizer = new InteractionSummarizer(_graph);

        var conversationText = "user: Hello\nassistant: Welcome to Nexus!";

        // Act
        var result = await summarizer.SummarizeAsync(conversationText, "Summarize.");

        // Assert: heuristic finds "Welcome to Nexus!"
        Assert.Equal("Welcome to Nexus!", result.Summary);
    }

    [Fact]
    public void CleanSummary_LongResponse_TruncatesTo3Sentences()
    {
        // Arrange
        var raw = "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence.";

        // Act
        var result = InteractionSummarizer.CleanSummary(raw);

        // Assert
        Assert.DoesNotContain("Fourth", result);
        Assert.DoesNotContain("Fifth", result);
        Assert.Contains("First sentence", result);
        Assert.Contains("Third sentence", result);
    }

    [Fact]
    public void GenerateHeuristicSummary_FindsLastAssistantMessage()
    {
        // Arrange
        var text = "user: question 1\nassistant: answer 1\nuser: question 2\nassistant: This is the final answer.";

        // Act
        var result = InteractionSummarizer.GenerateHeuristicSummary(text);

        // Assert
        Assert.Equal("This is the final answer.", result);
    }

    [Fact]
    public void GenerateHeuristicSummary_LongAssistantMessage_TruncatesTo200Chars()
    {
        // Arrange
        var longAnswer = new string('x', 300);
        var text = $"user: question\nassistant: {longAnswer}";

        // Act
        var result = InteractionSummarizer.GenerateHeuristicSummary(text);

        // Assert
        Assert.Equal(203, result.Length); // 200 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public async Task SummarizeAsync_LlmFailsAndPersistFails_ReturnsWithoutThrowing()
    {
        // Arrange: LLM will fail, forcing the outer catch block.
        // Use a KnowledgeGraph with an invalid connection string so AddInteractionAsync also fails.
        var badDbPath = Path.Combine(Path.GetTempPath(), $"nexus_bad_db_{Guid.NewGuid():N}.db");
        var badDbInit = new DatabaseInitializer(badDbPath);
        badDbInit.Initialize();
        var badGraph = new KnowledgeGraph(badDbInit.ConnectionString);

        // Close all pools and delete the file so that persist fails
        SqliteConnection.ClearAllPools();
        if (File.Exists(badDbPath))
            File.Delete(badDbPath);

        var mockLlm = new MockLlmClient(_ =>
            throw new InvalidOperationException("LLM unavailable"));
        var summarizer = new InteractionSummarizer(badGraph, mockLlm);

        // Act — should not throw despite both LLM and persist failing
        var result = await summarizer.SummarizeAsync(
            "user: test\nassistant: response", "Summarize.");

        // Assert: returns fallback interaction
        Assert.NotNull(result);
        Assert.Equal("Summary unavailable", result.Summary);
        Assert.Equal(0, result.TokenCount);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
