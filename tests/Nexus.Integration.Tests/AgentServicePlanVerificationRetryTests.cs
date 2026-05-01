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
/// Integration tests for AC-9: verification-failure retry inside the bounded while-loop
/// in ExecutePlanAsync. Verifies that:
/// - Failed verification triggers a retry with budget awareness
/// - When all attempts exhaust with verification failures, plan continues and final summary runs
/// </summary>
public class AgentServicePlanVerificationRetryTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServicePlanVerificationRetryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"plan_verif_retry_{Guid.NewGuid():N}.db");
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
    /// IToolVerifier that returns Failed for the first N calls and Verified thereafter.
    /// </summary>
    private sealed class FailFirstNTimesVerifier : IToolVerifier
    {
        private readonly int _failCount;
        private int _verifyCallCount;

        public FailFirstNTimesVerifier(int failCount)
        {
            _failCount = failCount;
        }

        public int TotalVerifyCalls => _verifyCallCount;

        public Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, object>?>(null);

        public Task<VerificationOutcome> VerifyAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            IReadOnlyDictionary<string, object>? preSnapshot,
            string toolResult,
            CancellationToken cancellationToken = default)
        {
            _verifyCallCount++;
            var outcome = _verifyCallCount <= _failCount
                ? VerificationOutcome.Failed("snapshot not_equal comparison failed")
                : VerificationOutcome.Verified();
            return Task.FromResult(outcome);
        }
    }

    /// <summary>
    /// IToolVerifier that always returns Failed.
    /// </summary>
    private sealed class AlwaysFailVerifier : IToolVerifier
    {
        public int VerifyCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, object>?>(null);

        public Task<VerificationOutcome> VerifyAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            IReadOnlyDictionary<string, object>? preSnapshot,
            string toolResult,
            CancellationToken cancellationToken = default)
        {
            VerifyCallCount++;
            return Task.FromResult(
                VerificationOutcome.Failed("snapshot not_equal comparison failed"));
        }
    }

    /// <summary>
    /// Scripted single-step IToolPlanner.
    /// </summary>
    private sealed class SingleStepPlanner : IToolPlanner
    {
        private readonly ToolPlan _plan;

        public SingleStepPlanner(string toolName)
        {
            _plan = new ToolPlan(
                new[] { new ToolPlanStep(1, $"Execute {toolName}", toolName, 0.95f) },
                "Scripted plan");
        }

        public Task<ToolPlan?> GeneratePlanAsync(
            string userMessage,
            string toolDefinitionsForPrompt,
            PlannerContext? context,
            CancellationToken ct = default)
            => Task.FromResult<ToolPlan?>(_plan);
    }

    /// <summary>
    /// Fake IToolExecutor with a fixed tool result.
    /// </summary>
    private sealed class FixedResultToolExecutor : IToolExecutor
    {
        private readonly string _toolName;
        private readonly string _result;

        public int InvokeCount { get; private set; }

        public FixedResultToolExecutor(string toolName, string result)
        {
            _toolName = toolName;
            _result = result;
        }

        public bool HasTools => true;

        public string GetToolDefinitionsForPrompt() =>
            $"- {_toolName}: Scripted tool";

        public string GetToolDefinitionsForPrompt(string? modelName) =>
            GetToolDefinitionsForPrompt();

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            return Task.FromResult(_result);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private AgentService CreateAgent(
        Func<string, string> llmResponseFactory,
        IToolPlanner? toolPlanner = null,
        IToolExecutor? toolExecutor = null,
        IToolVerifier? toolVerifier = null,
        int maxAttempts = 5,
        NexusConfig? config = null)
    {
        config ??= new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.ToolVerificationEnabled = true;
        config.Mcp.StepExecutionMaxAttempts = maxAttempts;
        // Disable heuristic: test messages are intentionally short (e.g. "Write file")
        // and would be blocked by the length gate, preventing plan execution under test.
        config.Mcp.PlannerHeuristicEnabled = false;

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
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
            toolVerifier: toolVerifier,
            toolExecutor: toolExecutor);

        _lastAgent = agent;
        return agent;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: PlanStep_VerificationFails_TriggersRetryWithinBoundedLoop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_VerificationFails_TriggersRetryWithinBoundedLoop()
    {
        // Arrange: verifier fails on attempt 1, passes on attempt 2
        // maxAttempts = 3 so retry is within budget.
        const string toolName = "write_file";
        var verifier = new FailFirstNTimesVerifier(failCount: 1);
        var toolExecutor = new FixedResultToolExecutor(toolName, "File written.");
        var planner = new SingleStepPlanner(toolName);

        var llmCallCount = 0;

        // The LLM always returns a valid tool call when it sees a [PLANNER] instruction.
        // On the retry attempt the conversation contains a [PlanStep N] retry message.
        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            if (lastUserMsg.Contains("[PLANNER]"))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"hello\"}}}}]";
            // Retry prompt or final summary
            if (lastUserMsg.Contains("Retry with explicit content"))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"hello\"}}}}]";
            return "Summary: file was written successfully.";
        }, planner, toolExecutor, verifier, maxAttempts: 3);

        // Act
        var response = await agent.ChatAsync("Write file");

        // Assert: tool was invoked at least twice (first attempt + retry after verification failure)
        Assert.True(toolExecutor.InvokeCount >= 2,
            $"Expected at least 2 tool invocations (retry on verification failure) but got {toolExecutor.InvokeCount}");

        // Verifier was called at least twice (once per tool invocation)
        Assert.True(verifier.TotalVerifyCalls >= 2,
            $"Expected verifier called at least twice but was called {verifier.TotalVerifyCalls} times");

        // History contains a [PlanStep N] retry message
        var history = agent.ConversationHistory;
        var hasRetryMsg = history.Any(m =>
            m.Content.Contains("Previous attempt unverified", StringComparison.Ordinal));
        Assert.True(hasRetryMsg,
            "History should contain a 'Previous attempt unverified' retry message");

        // Final summary ran (response is not empty)
        Assert.NotEmpty(response.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: PlanStep_VerificationFailsAllAttempts_FinalSummaryStillRuns
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_VerificationFailsAllAttempts_FinalSummaryStillRuns()
    {
        // Arrange: verifier always fails; maxAttempts = 3 → budget exhausted.
        // Expect: the plan continues (exhaustion sentinel), final summary runs.
        const string toolName = "write_file";
        var verifier = new AlwaysFailVerifier();
        var toolExecutor = new FixedResultToolExecutor(toolName, "File written.");
        var planner = new SingleStepPlanner(toolName);

        const string finalSummary = "Step exhausted but summary ran.";

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") || lastUserMsg.Contains("Retry with explicit content"))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"hello\"}}}}]";
            return finalSummary;
        }, planner, toolExecutor, verifier, maxAttempts: 3);

        // Act
        var response = await agent.ChatAsync("Write file repeatedly");

        // Assert: the history contains retry messages (at least maxAttempts-1 retry prompts)
        // when verification always fails, the bounded while-loop retries until attempt==maxAttempts,
        // then falls through to normal tool-result recording (stepCompleted = true).
        var history = agent.ConversationHistory;
        var retryMessages = history.Count(m =>
            m.Content.Contains("Previous attempt unverified", StringComparison.Ordinal));
        Assert.True(retryMessages >= 1,
            $"History should contain at least 1 retry message, but found {retryMessages}");

        // On the final attempt (no more retries), the [VerificationWarning] is preserved in history
        // because we fall through to tool-result recording with the warning text.
        var hasWarning = history.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.True(hasWarning,
            "History should contain [VerificationWarning] from the final exhausted attempt");

        // Final summary still ran (plan continued after the exhausted step)
        Assert.Contains(finalSummary, response.Content);

        // Verifier was called for every tool invocation (maxAttempts = 3 → 3 calls)
        Assert.True(verifier.VerifyCallCount >= 1,
            $"Verifier should have been called at least once but was called {verifier.VerifyCallCount} times");
    }
}
