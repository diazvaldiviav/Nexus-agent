using Microsoft.Data.Sqlite;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Integration.Tests.Fakes;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests that verify AgentService wires PlannerContextBuilder into ChatAsync
/// when planning is active (AC-4).
/// </summary>
public class AgentServicePlannerContextTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServicePlannerContextTests()
    {
        _dbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"planner_ctx_test_{Guid.NewGuid():N}.db");
        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.Initialize();
        _connectionString = dbInit.ConnectionString;
        _graph = new KnowledgeGraph(_connectionString);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_lastAgent is not null)
            await _lastAgent.FlushPendingExtractionAsync().ConfigureAwait(false);

        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(_dbPath))
            System.IO.File.Delete(_dbPath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Inner fakes
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// IToolPlanner that records the PlannerContext passed on each call and always
    /// returns a plan with one step so AgentService enters the plan-execute path.
    /// </summary>
    private sealed class RecordingToolPlanner : IToolPlanner
    {
        private readonly List<PlannerContext?> _capturedContexts = new();

        public IReadOnlyList<PlannerContext?> CapturedContexts => _capturedContexts;

        public PlannerContext? LastContext => _capturedContexts.Count > 0
            ? _capturedContexts[_capturedContexts.Count - 1]
            : null;

        public Task<ToolPlan?> GeneratePlanAsync(
            string userMessage,
            string toolDefinitionsForPrompt,
            PlannerContext? context,
            CancellationToken cancellationToken = default)
        {
            _capturedContexts.Add(context);

            // Return a minimal 1-step plan so AgentService enters the plan-execute path.
            // The step matches the "read_text_file" tool in FakeToolExecutor.
            var plan = new ToolPlan(
                new[] { new ToolPlanStep(1, "Read the file using read_text_file", "read_text_file", 1.0f) },
                "Recorded plan");
            return Task.FromResult<ToolPlan?>(plan);
        }
    }

    /// <summary>
    /// Minimal tool executor that always reports HasTools = true and can execute
    /// "read_text_file" successfully.
    /// </summary>
    private sealed class SimpleFakeToolExecutor : IToolExecutor
    {
        public bool HasTools => true;

        public string GetToolDefinitionsForPrompt() =>
            "- read_text_file: Reads a text file from disk";

        public string GetToolDefinitionsForPrompt(string? modelName) =>
            GetToolDefinitionsForPrompt();

        public System.Text.Json.JsonElement? GetToolSchema(string toolName) => null;

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"Result from {toolName}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private AgentService CreateAgent(
        Func<string, string> llmResponseFactory,
        RecordingToolPlanner toolPlanner,
        NexusConfig config,
        IPlannerContextBuilder? plannerContextBuilder = null)
    {
        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var toolExecutor = new SimpleFakeToolExecutor();
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider("ollama", llmResponseFactory);
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        var agent = new AgentService(
            config,
            _graph,
            promptBuilder,
            modelRouter,
            entityExtractor,
            providerFactory,
            summarizer,
            toolPlanner: toolPlanner,
            plannerContextBuilder: plannerContextBuilder,
            toolExecutor: toolExecutor);

        _lastAgent = agent;
        return agent;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: ChatAsync_PlannerEnabled_PassesContextToPlanner
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_PlannerEnabled_PassesContextToPlanner()
    {
        // Arrange
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.PlannerContextEnabled = true;
        // Disable heuristic: some test messages are intentionally short (e.g. "Do it again")
        // and would be blocked by the length gate, preventing plan execution under test.
        config.Mcp.PlannerHeuristicEnabled = false;

        var plannerContextBuilder = new PlannerContextBuilder(config);
        var toolPlanner = new RecordingToolPlanner();

        // LLM responses: step 1 tool call, then final summary.
        var agent = CreateAgent(
            lastUserMsg =>
            {
                if (lastUserMsg.Contains("[PLANNER]"))
                    return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/test.txt"}}]""";
                return "Done.";
            },
            toolPlanner,
            config,
            plannerContextBuilder);

        // Act: Turn 1 — contains an absolute path so the builder captures it
        await agent.ChatAsync("Check D:\\foo\\bar.md please").ConfigureAwait(false);

        // Turn 2
        await agent.ChatAsync("Now read the file").ConfigureAwait(false);

        // Turn 3 — by now the path D:\foo\bar.md is in the conversation history
        await agent.ChatAsync("Do it again").ConfigureAwait(false);

        // Assert: planner was called and received a non-null context on turn 3
        Assert.True(toolPlanner.CapturedContexts.Count >= 3,
            $"Expected at least 3 planner calls, got {toolPlanner.CapturedContexts.Count}");

        // The context passed on the third call should contain the absolute path from turn 1.
        var lastContext = toolPlanner.LastContext;
        Assert.NotNull(lastContext);
        Assert.False(lastContext.IsEmpty, "Context should not be empty after 3 turns with an absolute path.");
        Assert.Contains(@"D:\foo\bar.md", lastContext.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: ChatAsync_PlannerContextDisabled_PassesNullContext
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_PlannerContextDisabled_PassesNullContext()
    {
        // Arrange: PlannerContextEnabled = false so no context is built
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.PlannerContextEnabled = false;

        // Even if a builder is supplied, it must not be called when the flag is false.
        var plannerContextBuilder = new PlannerContextBuilder(config);
        var toolPlanner = new RecordingToolPlanner();

        var agent = CreateAgent(
            lastUserMsg =>
            {
                if (lastUserMsg.Contains("[PLANNER]"))
                    return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/test.txt"}}]""";
                return "Done.";
            },
            toolPlanner,
            config,
            plannerContextBuilder);

        // Act
        await agent.ChatAsync("Check D:\\foo\\bar.md please").ConfigureAwait(false);

        // Assert: planner was called with null context (flag is off)
        Assert.True(toolPlanner.CapturedContexts.Count >= 1,
            $"Expected at least 1 planner call, got {toolPlanner.CapturedContexts.Count}");
        Assert.Null(toolPlanner.LastContext);
    }
}
