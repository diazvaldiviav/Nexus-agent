using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests verifying that SummaryFailureAnalyzer is wired into AgentService.ExecutePlanAsync
/// and correctly injects (or does not inject) the grounding message — AC-6.
/// </summary>
public class AgentServiceSummaryHardeningTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServiceSummaryHardeningTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"summary_hardening_{Guid.NewGuid():N}.db");
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
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Inner fakes
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// IToolPlanner that returns a fixed single-step plan.
    /// </summary>
    private sealed class SingleStepPlanner : IToolPlanner
    {
        private readonly ToolPlan _plan;

        public SingleStepPlanner(ToolPlan plan) => _plan = plan;

        public Task<ToolPlan?> GeneratePlanAsync(
            string userMessage,
            string toolDefinitionsForPrompt,
            PlannerContext? context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ToolPlan?>(_plan);
    }

    /// <summary>
    /// IToolExecutor that always returns the configured result string.
    /// </summary>
    private sealed class FixedResultExecutor : IToolExecutor
    {
        private readonly string _result;

        public FixedResultExecutor(string result) => _result = result;

        public bool HasTools => true;
        public string GetToolDefinitionsForPrompt() => "- do_task: Does the task";
        public string GetToolDefinitionsForPrompt(string? modelName) => GetToolDefinitionsForPrompt();
        public System.Text.Json.JsonElement? GetToolSchema(string toolName) => null;
        public string GetToolDefinition(string toolName) => "";
        public string GetToolServerName(string toolName) => "";

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    /// <summary>
    /// ILlmProvider that captures the full conversation history on each call
    /// and returns from a factory based on the last user message.
    /// </summary>
    private sealed class CapturingLlmProvider : ILlmProvider
    {
        private readonly Func<IReadOnlyList<ConversationMessage>, string> _responseFactory;

        /// <summary>The conversation history as received on the final call (summary call).</summary>
        public IReadOnlyList<ConversationMessage>? LastCapturedHistory { get; private set; }

        public CapturingLlmProvider(Func<IReadOnlyList<ConversationMessage>, string> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public string ProviderName => "ollama";

        public Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
        {
            LastCapturedHistory = conversationHistory;
            var lastUserMessage = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            return Task.FromResult(_responseFactory(conversationHistory));
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastCapturedHistory = conversationHistory;
            var response = _responseFactory(conversationHistory);
            yield return response;
            await Task.CompletedTask;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private (AgentService Agent, CapturingLlmProvider Provider) CreateAgent(
        Func<IReadOnlyList<ConversationMessage>, string> responseFactory,
        IToolPlanner toolPlanner,
        IToolExecutor toolExecutor)
    {
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.PlannerHeuristicEnabled = false;

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);

        var provider = new CapturingLlmProvider(responseFactory);
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { provider });

        var agent = new AgentService(
            config,
            _graph,
            promptBuilder,
            modelRouter,
            entityExtractor,
            providerFactory,
            summarizer,
            toolPlanner: toolPlanner,
            toolExecutor: toolExecutor);

        _lastAgent = agent;
        return (agent, provider);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: RetriesExhausted_InjectsGroundingMessage
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetriesExhausted_InjectsGroundingMessage()
    {
        // Arrange: 1-step plan; LLM never emits a tool call → retries exhaust → grounding injected
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Do the task", "do_task", 0.9f) },
            "Raw plan");

        var toolPlanner = new SingleStepPlanner(plan);
        var toolExecutor = new FixedResultExecutor("Task done.");

        IReadOnlyList<ConversationMessage>? historyCapturedAtSummary = null;
        var isSummaryCall = false;

        var (agent, provider) = CreateAgent(history =>
        {
            // The summary call is the one where the last user message contains "Summarize"
            var lastUser = history.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            if (lastUser.Contains("Summarize"))
            {
                isSummaryCall = true;
                historyCapturedAtSummary = history.ToList();
            }

            // Always return prose (no tool call) so all retries exhaust
            if (lastUser.Contains("[PLANNER]"))
                return "I will do the task now.";

            return "All done.";
        }, toolPlanner, toolExecutor);

        // Act
        await agent.ChatAsync("Do the task");

        // Assert: we captured the history at the summary call
        Assert.True(isSummaryCall, "Summary LLM call was never made");
        Assert.NotNull(historyCapturedAtSummary);

        // The history should contain a [PlanResult] grounding message injected before the "Summarize" message
        var groundingMessage = historyCapturedAtSummary!
            .FirstOrDefault(m => m.Role == "user" && m.Content.Contains("[PlanResult]"));

        Assert.NotNull(groundingMessage);
        Assert.Contains("Step retries exhausted:", groundingMessage!.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: AllSucceeded_NoInjection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllSucceeded_NoInjection()
    {
        // Arrange: 1-step plan; LLM immediately emits a valid tool call → step succeeds → no grounding
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Do the task", "do_task", 0.9f) },
            "Raw plan");

        var toolPlanner = new SingleStepPlanner(plan);
        var toolExecutor = new FixedResultExecutor("Task done.");

        IReadOnlyList<ConversationMessage>? historyCapturedAtSummary = null;
        var isSummaryCall = false;

        var (agent, provider) = CreateAgent(history =>
        {
            var lastUser = history.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            if (lastUser.Contains("Summarize"))
            {
                isSummaryCall = true;
                historyCapturedAtSummary = history.ToList();
            }

            // Return a valid tool call for the step instruction so it succeeds on first attempt
            if (lastUser.Contains("[PLANNER]") && lastUser.Contains("do_task"))
                return """[TOOL_CALL: {"name":"do_task","arguments":{}}]""";

            return "All steps succeeded.";
        }, toolPlanner, toolExecutor);

        // Act
        await agent.ChatAsync("Do the task");

        // Assert: we captured the history at the summary call
        Assert.True(isSummaryCall, "Summary LLM call was never made");
        Assert.NotNull(historyCapturedAtSummary);

        // No [PlanResult] grounding message should be present (byte-equivalent to pre-AC-6 behavior)
        var groundingMessage = historyCapturedAtSummary!
            .FirstOrDefault(m => m.Role == "user" && m.Content.Contains("[PlanResult]"));

        Assert.Null(groundingMessage);
    }
}
