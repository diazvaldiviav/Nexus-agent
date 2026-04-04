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

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for the MCP tool call loop in AgentService.
/// AC-6: Tool call detection loop with max 3 iterations.
/// AC-10: Error handling for timeout, tool-not-found, server-unavailable.
/// AC-11: 4+ automated tests covering the MCP tool-call loop.
/// </summary>
public class McpToolCallLoopTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public McpToolCallLoopTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mcp_loop_test_{Guid.NewGuid():N}.db");
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

        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private AgentService CreateAgent(
        Func<string, string> responseFactory,
        IToolExecutor? toolExecutor = null,
        string providerName = "ollama",
        NexusConfig? config = null)
    {
        config ??= new NexusConfig();
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider(providerName, responseFactory);
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var agent = new AgentService(config, _graph, promptBuilder, modelRouter,
            entityExtractor, providerFactory, summarizer, toolExecutor);
        _lastAgent = agent;
        return agent;
    }

    [Fact]
    public async Task ChatAsync_WithToolCall_ExecutesToolAndReturnsResult()
    {
        // Arrange: LLM returns a tool call on first invocation, normal text on follow-up
        var callCount = 0;
        var fakeToolExecutor = new FakeToolExecutor((_, toolName, _) =>
            $"File content: Hello from {toolName}");

        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            if (!lastUserMessage.Contains("[Tool Result"))
                return """[TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]""";
            return "Based on the file content, the answer is 42.";
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("What is in the file?");

        // Assert: final response is the post-tool answer, not the tool call marker
        Assert.Contains("answer is 42", response.Content);
        Assert.Equal(2, callCount); // First call returned tool call, second returned answer
    }

    [Fact]
    public async Task ChatAsync_NoTools_SkipsToolCallDetection()
    {
        // Arrange: LLM returns a tool call marker but no tool executor is wired
        var agent = CreateAgent(_ =>
            """[TOOL_CALL: {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}]""",
            toolExecutor: null);

        // Act
        var response = await agent.ChatAsync("Read the file");

        // Assert: tool call marker is returned as-is since no executor is available
        Assert.Contains("TOOL_CALL", response.Content);
    }

    [Fact]
    public async Task ChatAsync_ToolError_ReturnsErrorStringToLlm()
    {
        // Arrange: FakeToolExecutor throws an exception
        var fakeToolExecutor = new FakeToolExecutor((_, _, _) =>
            throw new InvalidOperationException("Server unavailable"));

        var callCount = 0;
        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            if (!lastUserMessage.Contains("[Tool Result"))
                return """[TOOL_CALL: {"name": "broken_tool", "arguments": {}}]""";
            // LLM receives the error and provides a graceful response
            return "Sorry, I could not execute the tool. The server appears to be unavailable.";
        }, fakeToolExecutor);

        // Act
        var response = await agent.ChatAsync("Use the broken tool");

        // Assert: agent recovered gracefully; the error was fed back to LLM
        Assert.Contains("unavailable", response.Content);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ChatStreamAsync_WithToolCall_ExecutesAndReStreams()
    {
        // Arrange: LLM returns tool call on first stream, normal text on follow-up
        var fakeToolExecutor = new FakeToolExecutor((_, toolName, _) =>
            $"Result from {toolName}");

        var agent = CreateAgent(lastUserMessage =>
        {
            if (!lastUserMessage.Contains("[Tool Result"))
                return """[TOOL_CALL: {"name": "search", "arguments": {"query": "test"}}]""";
            return "Here is your answer based on the search results.";
        }, fakeToolExecutor);

        // Act: collect all streamed tokens
        var tokens = new List<string>();
        await foreach (var token in agent.ChatStreamAsync("Search for test"))
        {
            tokens.Add(token);
        }

        var fullOutput = string.Join("", tokens);

        // Assert: output includes the tool execution notification and final answer
        Assert.Contains("[Executing tool: search...]", fullOutput);
        Assert.Contains("answer based on the search results", fullOutput);
    }

    [Fact]
    public async Task ChatAsync_ConfiguredMaxIterations_RespectsLimit()
    {
        // Arrange: limit to 1 iteration; LLM always returns a tool call
        var config = new NexusConfig();
        config.Mcp.MaxToolCallIterations = 1;

        var toolInvocations = 0;
        var fakeToolExecutor = new FakeToolExecutor((_, _, _) =>
        {
            toolInvocations++;
            return "tool result";
        });

        var agent = CreateAgent(
            _ => """[TOOL_CALL: {"name": "my_tool", "arguments": {}}]""",
            fakeToolExecutor,
            config: config);

        // Act
        var response = await agent.ChatAsync("Do something");

        // Assert: tool executor invoked exactly once (1 iteration limit)
        Assert.Equal(1, toolInvocations);
    }

    [Fact]
    public async Task ChatAsync_ConfiguredTimeout_UsesConfigValue()
    {
        // Arrange: 1-second timeout with a tool that delays 5 seconds
        var config = new NexusConfig();
        config.Mcp.ToolCallTimeoutSeconds = 1;

        var delayExecutor = new DelayToolExecutor();

        var callCount = 0;
        var agent = CreateAgent(lastUserMessage =>
        {
            callCount++;
            if (!lastUserMessage.Contains("[Tool Result"))
                return """[TOOL_CALL: {"name": "slow_tool", "arguments": {}}]""";
            return lastUserMessage;
        }, delayExecutor, config: config);

        // Act
        var response = await agent.ChatAsync("Run slow tool");

        // Assert: response contains the configured timeout value
        Assert.Contains($"timed out after {config.Mcp.ToolCallTimeoutSeconds} seconds", response.Content);
    }

    private sealed class DelayToolExecutor : IToolExecutor
    {
        public bool HasTools => true;
        public string GetToolDefinitionsForPrompt() => "- slow_tool: A slow tool";
        public async Task<string> InvokeToolAsync(
            string serverName, string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return "never";
        }
    }

}
