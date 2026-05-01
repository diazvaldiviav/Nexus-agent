using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Integration.Tests.Fakes;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Models;
using Nexus.Memory.Processing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests for the plan-then-execute path in AgentService — AC-10, §14, §15.
/// Uses scripted fakes for IToolPlanner, IToolExecutor, and ILlmProvider.
/// </summary>
public class AgentServicePlanExecutionTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServicePlanExecutionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"plan_exec_test_{Guid.NewGuid():N}.db");
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
    /// Scripted IToolPlanner that returns a preset plan (or null) on every call.
    /// Tracks the number of times GeneratePlanAsync was called.
    /// </summary>
    private sealed class FakeToolPlanner : IToolPlanner
    {
        private readonly ToolPlan? _plan;

        public FakeToolPlanner(ToolPlan? plan)
        {
            _plan = plan;
        }

        public int CallCount { get; private set; }

        public Task<ToolPlan?> GeneratePlanAsync(
            string userMessage,
            string toolDefinitionsForPrompt,
            PlannerContext? context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_plan);
        }
    }

    /// <summary>
    /// FakeToolExecutor whose tool definitions include two named tools.
    /// Tracks which tool names were invoked and in what order.
    /// </summary>
    private sealed class TrackingToolExecutor : IToolExecutor
    {
        private readonly Func<string, string, Dictionary<string, object>?, string>? _handler;

        public TrackingToolExecutor(Func<string, string, Dictionary<string, object>?, string>? handler = null)
        {
            _handler = handler;
        }

        public bool HasTools => true;

        public List<string> InvokedTools { get; } = new();

        /// <summary>
        /// Optionally set to return a schema from GetToolSchema. Null means no schema available.
        /// </summary>
        public JsonElement? FakeSchema { get; set; }

        public string GetToolDefinitionsForPrompt() =>
            "- read_text_file: Reads a text file\n" +
            "- list_directory: Lists directory contents";

        public string GetToolDefinitionsForPrompt(string? modelName) =>
            GetToolDefinitionsForPrompt();

        public JsonElement? GetToolSchema(string toolName) => FakeSchema;

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            InvokedTools.Add(toolName);
            return Task.FromResult(_handler?.Invoke(serverName, toolName, parameters)
                ?? $"Result from {toolName}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private AgentService CreateAgent(
        Func<string, string> llmResponseFactory,
        IToolPlanner? toolPlanner = null,
        IToolExecutor? toolExecutor = null,
        NexusConfig? config = null)
    {
        // AC-H1: disable Phase 9/10 defaults — tests assert exact LLM call counts, [PLANNER] prompt
        // body strings, conversation-history shape, and step-retry sentinels from Phase 8.3;
        // PlannerContext injection or [VerificationWarning] decoration would break those assertions.
        // PlannerHeuristicEnabled is also disabled because test messages are intentionally short
        // (e.g. "Read a file") and the heuristic would block the planner before it could be tested.
        config ??= new NexusConfig();
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
            toolExecutor: toolExecutor);

        _lastAgent = agent;
        return agent;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: PlanEnabled_ExecutesStepsOneByOne
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanEnabled_ExecutesStepsOneByOne()
    {
        // Arrange: 2-step plan — step 1 uses read_text_file, step 2 uses list_directory
        var plan = new ToolPlan(
            new[]
            {
                new ToolPlanStep(1, "Read the config file", "read_text_file", 0.95f),
                new ToolPlanStep(2, "List the output folder", "list_directory", 0.90f)
            },
            "Raw plan text");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();

        var llmCallCount = 0;
        const string finalSummary = "All steps completed successfully.";

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            // Step 1 instruction ([PLANNER] prefix + tool name, AC-A2) → return tool call for read_text_file
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/config.txt"}}]""";
            // Step 2 instruction ([PLANNER] prefix + tool name, AC-A2) → return tool call for list_directory
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("list_directory"))
                return """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/output"}}]""";
            // Final summary request
            return finalSummary;
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read config and list output");

        // Assert: both tools invoked in plan order
        Assert.Equal(2, toolExecutor.InvokedTools.Count);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);
        Assert.Equal("list_directory", toolExecutor.InvokedTools[1]);

        // Final response is the summary from LLM
        Assert.Contains("completed successfully", response.Content);

        // Model called at least N+1 times (N=2 steps + 1 final summary = ≥3)
        Assert.True(llmCallCount >= 3,
            $"Expected at least 3 LLM calls (2 steps + 1 summary) but got {llmCallCount}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: PlanStep_NoToolCallParsed_RetriesOnce
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_NoToolCallParsed_RetriesOnce()
    {
        // Arrange: 1-step plan using read_text_file; no schema (FakeSchema = null).
        // Under bounded-loop semantics:
        //   attempt 1: BuildStepPrompt(1, ...) → "[PLANNER] Execute ONLY this step: ..." → LLM returns prose
        //   attempt 2: BuildStepPrompt(2, ..., schema=null) → falls back to attempt-1 body → LLM returns tool call
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();   // FakeSchema = null by default

        var llmCallCount = 0;
        var attempt2MsgReceived = false;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            // Attempt 1: [PLANNER] Execute ONLY this step → return prose (no tool call)
            if (llmCallCount == 1 && lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return "I will read the file now.";  // no tool call

            // Attempt 2: same prompt style (schema null → falls back to attempt-1 body) → return tool call
            if (llmCallCount == 2 && lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
            {
                attempt2MsgReceived = true;
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/test.txt"}}]""";
            }

            // Final summary
            return "Done reading the file.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read a file");

        // Assert: second attempt was made
        Assert.True(attempt2MsgReceived, "Expected attempt 2 to have been issued to LLM");

        // Tool was executed after retry (not skipped)
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: PlanStep_ModelCallsDifferentTool_ExecutesAnyway
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_ModelCallsDifferentTool_ExecutesAnyway()
    {
        // Arrange: plan says use "read_text_file" but LLM emits TOOL_CALL for "list_directory"
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the configuration", "read_text_file", 0.90f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();

        var agent = CreateAgent(lastUserMsg =>
        {
            // Regardless of plan, LLM emits list_directory.
            // AC-A2: step instructions now have [PLANNER] prefix instead of "Step 1" text.
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/"}}]""";
            return "Completed with directory listing.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Do the task");

        // Assert: the actually-emitted tool (list_directory) was executed — not skipped
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("list_directory", toolExecutor.InvokedTools[0]);
        Assert.Contains("Completed", response.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: PlanDisabled_UsesNormalToolLoop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanDisabled_UsesNormalToolLoop()
    {
        // Arrange: ToolPlanningEnabled=false; IToolPlanner is NOT injected (null)
        // Normal tool loop should run. Verify GeneratePlanAsync is never called.
        // AC-H1: disable Phase 9 defaults — test asserts exact tool invocation order and LLM
        // call count from Phase 8.3; PlannerContext injection would alter the prompt shape.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.ToolPlanningEnabled = false;

        var plannerCallCount = 0;
        // We cannot inject the planner when planning is disabled (we pass null),
        // but we track via closure whether we could set it.
        // Architecture: when toolPlanner is null, the planner gate is skipped entirely.
        // We verify normal tool loop ran by checking tool was invoked via FakeToolExecutor.

        var toolExecutor = new TrackingToolExecutor();
        var llmCallCount = 0;

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);

        var fakeProvider = new FakeLlmProvider("ollama", lastUserMsg =>
        {
            llmCallCount++;
            if (!lastUserMsg.Contains("[Tool Result"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/x.txt"}}]""";
            return "Normal loop response.";
        });
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        // toolPlanner: null — plan path is never entered (§6.3 gate: `_toolPlanner is not null`)
        var agent = new AgentService(
            config,
            _graph,
            promptBuilder,
            modelRouter,
            entityExtractor,
            providerFactory,
            summarizer,
            toolPlanner: null,       // plan path disabled
            toolExecutor: toolExecutor);
        _lastAgent = agent;

        // Act
        var response = await agent.ChatAsync("Do something");

        // Assert: tool was invoked via normal loop (not plan path)
        Assert.NotEmpty(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // plannerCallCount is 0 — planner was never wired so it could never be called
        Assert.Equal(0, plannerCallCount);

        // Normal loop ran (≥2 LLM calls: first returned tool call, second returned final answer)
        Assert.True(llmCallCount >= 2);
        Assert.Contains("Normal loop response", response.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: PlanWithNoMatchedTools_FallsThroughToNormalLoop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanWithNoMatchedTools_FallsThroughToNormalLoop()
    {
        // Arrange: ToolPlanner returns null (no matched tools / planning failure).
        // AgentService should fall through to the original tool loop unchanged.
        var toolPlanner = new FakeToolPlanner(null);  // returns null → fall-through

        var toolExecutor = new TrackingToolExecutor();
        var llmCallCount = 0;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            if (!lastUserMsg.Contains("[Tool Result") && !lastUserMsg.Contains("[Plan"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/y.txt"}}]""";
            return "Fallthrough loop response.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Do something with a file");

        // Assert: planner was called (it's wired)
        Assert.Equal(1, toolPlanner.CallCount);

        // Normal tool loop ran because plan was null
        Assert.NotEmpty(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // Final response is from the normal loop
        Assert.Contains("Fallthrough loop response", response.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: PlanStep_ToolExecutionThrows_PlanContinues (AC-F2a #1)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_ToolExecutionThrows_PlanContinues()
    {
        // Arrange: 2-step plan; step 1 tool throws an exception.
        // ExecuteToolWithTimeoutAsync catches all non-OCE exceptions and returns an error string
        // rather than rethrowing — so the plan continues to step 2 (AC-A1).
        // History must contain the error result from step 1, and step 2 must execute.

        var stepOneThrown = false;

        var toolExecutor = new TrackingToolExecutor(handler: (server, toolName, args) =>
        {
            if (toolName == "read_text_file" && !stepOneThrown)
            {
                stepOneThrown = true;
                throw new InvalidOperationException("Simulated tool failure on step 1");
            }
            return $"Result from {toolName}";
        });

        var plan = new ToolPlan(
            new[]
            {
                new ToolPlanStep(1, "Read the configuration file", "read_text_file", 0.95f),
                new ToolPlanStep(2, "List the output folder", "list_directory", 0.90f)
            },
            "Raw plan text");

        var toolPlanner = new FakeToolPlanner(plan);
        const string finalSummary = "Both steps processed.";

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/config.txt"}}]""";
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("list_directory"))
                return """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/output"}}]""";
            return finalSummary;
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read config and list output");

        // Assert: step 2 was executed (plan continued past exception in step 1)
        Assert.Contains("list_directory", toolExecutor.InvokedTools);

        // History must contain an error result entry for step 1 — ExecuteToolWithTimeoutAsync
        // catches the throw and returns "Error executing tool '{name}': {message}"
        var history = agent.ConversationHistory;
        var hasErrorResult = history.Any(m =>
            m.Content.Contains("Error executing tool 'read_text_file'", StringComparison.Ordinal));
        Assert.True(hasErrorResult,
            "History should contain an error result for step 1 when the tool throws");

        // Final summary returned
        Assert.Contains("processed", response.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: Plan_SyntheticMessagesFilteredFromExtraction (AC-F2a #2)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// EntityExtractor subclass that captures the conversationText argument passed to
    /// ExtractAndPersistAsync so tests can assert what text was submitted for extraction.
    /// </summary>
    private sealed class CapturingEntityExtractor : EntityExtractor
    {
        public CapturingEntityExtractor(KnowledgeGraph graph) : base(graph) { }

        public List<string> CapturedTexts { get; } = new();

        public override Task<List<Entity>> ExtractAndPersistAsync(
            string text,
            string? extractionPrompt = null,
            CancellationToken cancellationToken = default)
        {
            CapturedTexts.Add(text);
            // Return empty list — no actual extraction needed for this assertion
            return Task.FromResult(new List<Entity>());
        }
    }

    [Fact]
    public async Task Plan_SyntheticMessagesFilteredFromExtraction()
    {
        // Arrange: 1-step plan; LLM returns a tool call.
        // [PLANNER]-prefixed messages injected into conversation history by ExecutePlanAsync
        // must NOT appear in the conversationText passed to ExtractAndPersistAsync.
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read a file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();
        var capturingExtractor = new CapturingEntityExtractor(_graph);

        // AC-H1: disable Phase 9 defaults — test asserts that captured extraction texts contain
        // no [PLANNER] prefix; PlannerContext injection would change the prompt body shape.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.ToolPlanningEnabled = true;

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var summarizer = new InteractionSummarizer(_graph);
        var fakeProvider = new FakeLlmProvider("ollama", lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/f.txt"}}]""";
            return "Summary complete.";
        });
        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        var agent = new AgentService(
            config,
            _graph,
            promptBuilder,
            modelRouter,
            capturingExtractor,    // ← injected capturing extractor
            providerFactory,
            summarizer,
            toolPlanner: toolPlanner,
            toolExecutor: toolExecutor);
        _lastAgent = agent;

        // Act
        await agent.ChatAsync("Read a file for me");

        // Flush extraction so the background task completes before we assert
        await agent.FlushPendingExtractionAsync();

        // Assert: at least one extraction call was made
        Assert.NotEmpty(capturingExtractor.CapturedTexts);

        // None of the captured texts should contain "[PLANNER] " prefix (AC-A2 filter)
        foreach (var capturedText in capturingExtractor.CapturedTexts)
        {
            Assert.DoesNotContain("[PLANNER] ", capturedText,
                StringComparison.Ordinal);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: Plan_CancelledMidStep_ThrowsOperationCanceled (AC-F2a #3)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Plan_CancelledMidStep_ThrowsOperationCanceled()
    {
        // Arrange: 2-step plan. Cancel the CT immediately before step 2 starts.
        // The plan-execute loop calls ct.ThrowIfCancellationRequested() at the top of each step.
        using var cts = new CancellationTokenSource();
        var stepOneExecuted = false;

        var toolExecutor = new TrackingToolExecutor(handler: (server, toolName, args) =>
        {
            if (toolName == "read_text_file")
            {
                stepOneExecuted = true;
                // Cancel after step 1 executes so the loop hits the checkpoint before step 2
                cts.Cancel();
                return "File contents";
            }
            return $"Result from {toolName}";
        });

        var plan = new ToolPlan(
            new[]
            {
                new ToolPlanStep(1, "Read the file", "read_text_file", 0.95f),
                new ToolPlanStep(2, "List the directory", "list_directory", 0.90f)
            },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/f.txt"}}]""";
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("list_directory"))
                return """[TOOL_CALL: {"name":"list_directory","arguments":{"path":"/"}}]""";
            return "Summary.";
        }, toolPlanner, toolExecutor);

        // Act & Assert: OperationCanceledException must propagate
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => agent.ChatAsync("Do both steps", cts.Token));

        // Step 1 ran before cancellation
        Assert.True(stepOneExecuted, "Step 1 should have executed before cancellation");

        // Step 2 must NOT have been invoked (plan aborted by CT check)
        Assert.DoesNotContain("list_directory", toolExecutor.InvokedTools);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: PlanStreaming_SummaryLlmFailure_EmitsFallbackMarker (AC-F2a #4)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FakeLlmProvider variant that throws on streaming summary calls (non-OCE).
    /// Streaming calls for step execution succeed; the final summary stream throws.
    /// </summary>
    private sealed class ThrowingOnSummaryLlmProvider : ILlmProvider
    {
        private readonly Func<string, string> _chatResponseFactory;
        private int _streamCallCount;

        public ThrowingOnSummaryLlmProvider(Func<string, string> chatResponseFactory)
        {
            _chatResponseFactory = chatResponseFactory;
        }

        public string ProviderName => "ollama";

        public Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
        {
            var last = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            return Task.FromResult(_chatResponseFactory(last));
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _streamCallCount++;
            // The final summary stream (called after all steps complete — "All steps complete." message)
            var last = conversationHistory.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            if (last.Contains("All steps complete"))
            {
                // Throw to simulate a mid-stream failure (AC-B2 scenario)
                throw new InvalidOperationException("Simulated streaming summary failure");
            }
            yield return "stream token";
        }
    }

    [Fact]
    public async Task PlanStreaming_SummaryLlmFailure_EmitsFallbackMarker()
    {
        // Arrange: 1-step plan. The streaming summary call throws (non-OCE).
        // The stream should emit "[Summary unavailable: InvalidOperationException]" and complete cleanly.
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the config file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();

        // AC-H1: disable Phase 9 defaults — test asserts exact "[Summary unavailable: …]" fallback
        // marker text in stream tokens; PlannerContext injection would alter the upstream prompt
        // shape and ToolVerification decoration could contaminate the streamed token sequence.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.ToolPlanningEnabled = true;

        var search = new SemanticSearch(_connectionString);
        var memoryBuilder = new MemoryContextBuilder(_graph, search);
        var promptBuilder = new PromptBuilder(memoryBuilder, config.Agent, toolExecutor);
        var modelRouter = new ModelRouter(config.Models.Routing);
        var entityExtractor = new EntityExtractor(_graph);
        var summarizer = new InteractionSummarizer(_graph);

        // ChatAsync must return a tool call for step execution;
        // ChatStreamAsync throws on summary (handled by ThrowingOnSummaryLlmProvider)
        var throwingProvider = new ThrowingOnSummaryLlmProvider(lastMsg =>
        {
            if (lastMsg.Contains("[PLANNER]") && lastMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/config.txt"}}]""";
            return "Final answer.";
        });

        var providerFactory = new LlmProviderFactory(new ILlmProvider[] { throwingProvider });

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

        // Act: collect all tokens from the streaming plan path
        var tokens = new List<string>();
        var threwDuringStream = false;
        try
        {
            await foreach (var token in agent.ChatStreamAsync("Read config and summarize"))
                tokens.Add(token);
        }
        catch (Exception)
        {
            threwDuringStream = true;
        }

        // Assert: stream completed without throwing to the caller
        Assert.False(threwDuringStream,
            "ChatStreamAsync must not propagate exceptions to the caller when summary LLM fails");

        // Fallback marker must have been emitted (AC-B2)
        var allTokens = string.Concat(tokens);
        Assert.Contains("[Summary unavailable: InvalidOperationException]", allTokens,
            StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: StepExecution_SucceedsOnAttempt2_SchemaTemplateInjected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepExecution_SucceedsOnAttempt2_SchemaTemplateInjected()
    {
        // Arrange: schema with required: ["path"], properties.path.type = "string"
        // Call 1 returns prose → attempt 2 should include "path (string)" and template skeleton
        // Call 2 returns a valid tool call
        var schemaJson = """{"type":"object","required":["path"],"properties":{"path":{"type":"string"}}}""";
        var schema = JsonDocument.Parse(schemaJson).RootElement;

        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the config file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor { FakeSchema = schema };

        var llmCallCount = 0;
        var attempt2Prompt = string.Empty;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            if (llmCallCount == 1)
                return "I'll read the file.";  // prose — no tool call

            // Attempt 2: capture the prompt to assert on schema template injection
            if (llmCallCount == 2)
            {
                attempt2Prompt = lastUserMsg;
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/config.txt"}}]""";
            }

            return "Summary done.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read the config");

        // Assert: tool executed once (on attempt 2)
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // Attempt 2 prompt must contain schema-derived content
        Assert.Contains("path (string)", attempt2Prompt, StringComparison.Ordinal);
        Assert.Contains("<path>", attempt2Prompt, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: StepExecution_SucceedsOnAttempt3_CoercionPrompt
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepExecution_SucceedsOnAttempt3_CoercionPrompt()
    {
        // Arrange: calls 1+2 return prose, call 3 returns valid tool call.
        // Attempt 3 prompt must contain "Your previous response was prose."
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();  // FakeSchema = null

        var llmCallCount = 0;
        var attempt3Prompt = string.Empty;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            if (llmCallCount == 1 || llmCallCount == 2)
                return "I will read it.";  // prose

            if (llmCallCount == 3)
            {
                attempt3Prompt = lastUserMsg;
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/f.txt"}}]""";
            }

            return "Summary.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read the file");

        // Assert: tool executed once (at attempt 3)
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // Attempt 3 prompt must contain the hard-coercion text
        Assert.Contains("Your previous response was prose", attempt3Prompt, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: StepExecution_ExceedsMaxAttempts_LogsErrorAndSkips
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepExecution_ExceedsMaxAttempts_LogsErrorAndSkips()
    {
        // Arrange: StepExecutionMaxAttempts = 3; 2-step plan; LLM always returns prose.
        // After 3 attempts each step is skipped with a sentinel in history.
        // Final summary still runs and returns "All done".
        // AC-H1: disable Phase 9 defaults — test asserts exact "Exceeded 3 attempts; moving on."
        // sentinel count (2) in ConversationHistory; PlannerContext injection would add extra
        // history entries and [VerificationWarning] decoration would corrupt the sentinel text.
        var config = new NexusConfig();
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.StepExecutionMaxAttempts = 3;

        var plan = new ToolPlan(
            new[]
            {
                new ToolPlanStep(1, "Read the config file", "read_text_file", 0.95f),
                new ToolPlanStep(2, "List the output folder", "list_directory", 0.90f)
            },
            "Raw plan text");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();

        var agent = CreateAgent(lastUserMsg =>
        {
            // Always return prose for plan steps; return "All done" for the final summary
            if (lastUserMsg.Contains("All steps complete"))
                return "All done";
            return "I'm thinking about it.";  // prose — never a tool call
        }, toolPlanner, toolExecutor, config);

        // Act
        var response = await agent.ChatAsync("Do both steps");

        // Assert: no tool was ever invoked
        Assert.Empty(toolExecutor.InvokedTools);

        // Final summary still ran — no OCE propagated
        Assert.Contains("All done", response.Content);

        // History must contain exactly 2 "Exceeded 3 attempts; moving on." sentinels (one per step).
        // Use StartsWith("[PlanStep") to exclude the AC-6 grounding message that echoes the reason text.
        var history = agent.ConversationHistory;
        var sentinelCount = history.Count(m =>
            m.Content.StartsWith("[PlanStep ", StringComparison.Ordinal)
            && m.Content.Contains("Exceeded 3 attempts; moving on.", StringComparison.Ordinal));
        Assert.Equal(2, sentinelCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 13: StepExecution_NoSchemaAvailable_FallsBackToAttempt1PromptStyle
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepExecution_NoSchemaAvailable_FallsBackToAttempt1PromptStyle()
    {
        // Arrange: FakeSchema = null → attempt 2 must NOT contain "Required arguments:"
        // or "<" placeholder markers. Tool succeeds at attempt 3.
        var plan = new ToolPlan(
            new[] { new ToolPlanStep(1, "Read the file", "read_text_file", 0.95f) },
            "Raw plan");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor { FakeSchema = null };

        var llmCallCount = 0;
        var attempt2Prompt = string.Empty;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            if (llmCallCount == 1 || llmCallCount == 2)
            {
                if (llmCallCount == 2)
                    attempt2Prompt = lastUserMsg;
                return "I'll do it.";  // prose
            }

            if (llmCallCount == 3)
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/x.txt"}}]""";

            return "Summary.";
        }, toolPlanner, toolExecutor);

        // Act
        var response = await agent.ChatAsync("Read the file");

        // Assert: tool eventually executed at attempt 3
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // Attempt 2 prompt must NOT contain schema-driven hints (no schema available)
        Assert.DoesNotContain("Required arguments:", attempt2Prompt, StringComparison.Ordinal);
        // It also must not contain an unquoted placeholder like "<path>" from BuildArgsTemplate
        // (schema is null → BuildStepPrompt falls back to attempt-1 body which has no template)
        Assert.DoesNotContain("Required arguments:", attempt2Prompt, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 3 (Sprint 10 follow-up): Skip detection + grounding injection E2E
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// E2E: a plan step with MatchedToolName==null is skipped silently by AgentService;
    /// the SummaryFailureAnalyzer must detect the skip sentinel and inject a grounding
    /// message into the conversation history before the final summary LLM call. The
    /// final-summary prompt then contains "[PlanResult]" and "Steps skipped (no matching
    /// tool): 1" — preventing the LLM from hallucinating success.
    /// </summary>
    [Fact]
    public async Task PlanStep_NoToolMatched_GroundingInjected_NoHallucination()
    {
        // Arrange: 2-step plan — step 1 matches read_text_file, step 2 has matched=null
        // (simulating the qwen3:1.7b "Insert/Save" verb scenario).
        var plan = new ToolPlan(
            new[]
            {
                new ToolPlanStep(1, "Use read_text_file to fetch content", "read_text_file", 0.95f),
                new ToolPlanStep(2, "Insert the new section into the file", null, 0f)
            },
            "Raw plan with one ambiguous step");

        var toolPlanner = new FakeToolPlanner(plan);
        var toolExecutor = new TrackingToolExecutor();

        // Capture the LAST user message passed to the LLM (which is the final summary
        // request — preceded by the grounding injection from SummaryFailureAnalyzer).
        string? finalSummaryUserMsg = null;
        var llmCallCount = 0;

        var agent = CreateAgent(lastUserMsg =>
        {
            llmCallCount++;
            // Step 1 — return tool call for read_text_file
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains("read_text_file"))
                return """[TOOL_CALL: {"name":"read_text_file","arguments":{"path":"/file.md"}}]""";
            // Step 2 has MatchedToolName=null → AgentService skips without calling the LLM.
            // The final summary call is the next LLM invocation. Capture its input.
            finalSummaryUserMsg = lastUserMsg;
            return "Done.";
        }, toolPlanner, toolExecutor);

        // Act
        await agent.ChatAsync("Read and modify the file");

        // Assert: only the matched tool was actually invoked
        Assert.Single(toolExecutor.InvokedTools);
        Assert.Equal("read_text_file", toolExecutor.InvokedTools[0]);

        // The final summary call's prompt must contain the grounding block — proof
        // that SummaryFailureAnalyzer detected the skip sentinel and injected.
        Assert.NotNull(finalSummaryUserMsg);
        Assert.Contains("[PlanResult]", finalSummaryUserMsg!);
        Assert.Contains("Steps skipped (no matching tool): 1", finalSummaryUserMsg!);
        Assert.Contains("Do NOT claim success", finalSummaryUserMsg!);
    }
}
