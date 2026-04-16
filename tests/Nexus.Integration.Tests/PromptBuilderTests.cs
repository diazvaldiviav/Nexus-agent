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
/// Tests for PromptBuilder — uses a real SQLite DB for MemoryContextBuilder integration.
/// </summary>
public class PromptBuilderTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly KnowledgeGraph _graph;
    private readonly MemoryContextBuilder _memoryBuilder;

    public PromptBuilderTests()
    {
        _dbPath = Path.GetTempFileName();
        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.Initialize();
        _connectionString = dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
        var search = new SemanticSearch(_connectionString);
        _memoryBuilder = new MemoryContextBuilder(_graph, search);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private PromptBuilder CreateBuilder(IToolExecutor? toolExecutor = null, AgentConfig? agentConfig = null)
    {
        agentConfig ??= new AgentConfig();
        return new PromptBuilder(_memoryBuilder, agentConfig, toolExecutor);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_ContainsAgentName()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.Contains("Nexus", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_ContainsLanguageInstruction()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.Contains("language", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithTools_ContainsToolSection()
    {
        // Arrange
        var fakeExecutor = new FakeToolExecutor();
        var builder = CreateBuilder(fakeExecutor);

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.Contains("# Available Tools", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithTools_ContainsToolCallFormat()
    {
        // Arrange
        var fakeExecutor = new FakeToolExecutor();
        var builder = CreateBuilder(fakeExecutor);

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.Contains("[TOOL_CALL:", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithoutTools_OmitsToolSection()
    {
        // Arrange
        var builder = CreateBuilder(toolExecutor: null);

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.DoesNotContain("# Available Tools", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithMemory_ContainsMemorySection()
    {
        // Arrange — seed an entity so memory context is non-empty
        await _graph.AddEntityAsync(new Entity
        {
            Name = "TestProject",
            Type = EntityType.Project,
            TextSummary = "A test project for unit testing",
            RelevanceScore = 1.0,
            MemoryLevel = MemoryLevel.Working
        });

        var builder = CreateBuilder();

        // Act
        var prompt = await builder.BuildSystemPromptAsync("Tell me about TestProject");

        // Assert
        Assert.Contains("# Your Memory", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_EmptyMemory_OmitsMemorySection()
    {
        // Arrange — empty DB, no entities
        var builder = CreateBuilder();

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello");

        // Assert
        Assert.DoesNotContain("# Your Memory", prompt);
    }

    [Fact]
    public void BuildEntityExtractionPrompt_ContainsConversationText()
    {
        // Arrange
        var builder = CreateBuilder();
        var conversationText = "User: What is Nexus?\nAssistant: Nexus is an AI agent.";

        // Act
        var prompt = builder.BuildEntityExtractionPrompt(conversationText);

        // Assert
        Assert.Contains(conversationText, prompt);
    }

    [Fact]
    public void BuildEntityExtractionPrompt_ContainsJsonStructure()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var prompt = builder.BuildEntityExtractionPrompt("User: Hello\nAssistant: Hi");

        // Assert
        Assert.Contains("entities", prompt);
        Assert.Contains("relations", prompt);
    }

    [Fact]
    public void BuildInteractionSummaryPrompt_ContainsSummarizeInstruction()
    {
        // Arrange
        var builder = CreateBuilder();
        var conversationText = "User: Let's plan the sprint.\nAssistant: Sure, let's start.";

        // Act
        var prompt = builder.BuildInteractionSummaryPrompt(conversationText);

        // Assert
        Assert.Contains("Summarize", prompt);
        Assert.Contains(conversationText, prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithModelName_ForwardsToToolExecutor()
    {
        // Arrange
        var fakeExecutor = new FakeToolExecutor();
        var builder = CreateBuilder(fakeExecutor);

        // Act
        await builder.BuildSystemPromptAsync("hello", "qwen3:1.7b");

        // Assert
        Assert.Equal("qwen3:1.7b", fakeExecutor.LastModelName);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_NullModelName_FallsBackToUnfiltered()
    {
        // Arrange
        var fakeExecutor = new FakeToolExecutor();
        var builder = CreateBuilder(fakeExecutor);

        // Act
        var prompt = await builder.BuildSystemPromptAsync("hello", modelName: null);

        // Assert
        Assert.Null(fakeExecutor.LastModelName);
        Assert.Contains("# Available Tools", prompt);
    }
}
