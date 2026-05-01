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
/// Integration tests that verify the IToolVerifier is wired into
/// ExecuteToolWithTimeoutAsync (AC-8) via the AgentService plan-execute path.
/// </summary>
public class AgentServiceVerificationTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServiceVerificationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"verif_test_{Guid.NewGuid():N}.db");
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
    /// Scripted IToolVerifier: callers set CaptureResult and VerifyResult
    /// to control what is returned per method. Tracks call counts.
    /// </summary>
    private sealed class ScriptedToolVerifier : IToolVerifier
    {
        public IReadOnlyDictionary<string, object>? CaptureResult { get; set; }
        public VerificationOutcome VerifyResult { get; set; } = VerificationOutcome.NoRule();

        public int CaptureCallCount { get; private set; }
        public int VerifyCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
        {
            CaptureCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CaptureResult);
        }

        public Task<VerificationOutcome> VerifyAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            IReadOnlyDictionary<string, object>? preSnapshot,
            string toolResult,
            CancellationToken cancellationToken = default)
        {
            VerifyCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(VerifyResult);
        }
    }

    /// <summary>
    /// IToolVerifier that blocks on CapturePreSnapshotAsync for a configurable duration.
    /// Used to test snapshot timeout handling.
    /// </summary>
    private sealed class SlowCaptureVerifier : IToolVerifier
    {
        private readonly TimeSpan _captureDelay;

        public SlowCaptureVerifier(TimeSpan captureDelay)
        {
            _captureDelay = captureDelay;
        }

        public async Task<IReadOnlyDictionary<string, object>?> CapturePreSnapshotAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_captureDelay, cancellationToken);
            return null;
        }

        public Task<VerificationOutcome> VerifyAsync(
            string serverName,
            string toolName,
            IReadOnlyDictionary<string, object>? arguments,
            IReadOnlyDictionary<string, object>? preSnapshot,
            string toolResult,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VerificationOutcome.NoRule());
        }
    }

    /// <summary>
    /// Single-step fake planner that always returns the same plan.
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
    /// Fake IToolExecutor that returns a configurable result from InvokeToolAsync
    /// and reports a fixed server name via GetToolServerName.
    /// </summary>
    private sealed class FakeVerifiableToolExecutor : IToolExecutor
    {
        private readonly string _toolResult;
        private readonly string _serverName;
        private readonly string _toolName;

        public FakeVerifiableToolExecutor(string toolName, string serverName, string toolResult)
        {
            _toolName = toolName;
            _serverName = serverName;
            _toolResult = toolResult;
        }

        public bool HasTools => true;

        public string GetToolDefinitionsForPrompt() =>
            $"- {_toolName}: Fake tool for testing";

        public string GetToolDefinitionsForPrompt(string? modelName) =>
            GetToolDefinitionsForPrompt();

        public string GetToolServerName(string toolName) =>
            string.Equals(toolName, _toolName, StringComparison.OrdinalIgnoreCase)
                ? _serverName
                : string.Empty;

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_toolResult);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private AgentService CreateAgent(
        Func<string, string> llmResponseFactory,
        IToolPlanner? toolPlanner = null,
        IToolExecutor? toolExecutor = null,
        IToolVerifier? toolVerifier = null,
        NexusConfig? config = null)
    {
        var isCallerConfig = config is not null;
        config ??= new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        // Only set ToolVerificationEnabled when using the default config; callers that
        // supply a config own the value (e.g. VerificationDisabled test sets it to false).
        if (!isCallerConfig)
            config.Mcp.ToolVerificationEnabled = true;

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
    // Test 1: WriteFile_PostReadEmpty_ReturnsVerificationWarning
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteFile_PostReadEmpty_ReturnsVerificationWarning()
    {
        // Arrange: verifier returns Failed (empty post-snapshot = no change)
        var verifier = new ScriptedToolVerifier
        {
            CaptureResult = new Dictionary<string, object> { ["value"] = string.Empty },
            VerifyResult = VerificationOutcome.Failed("snapshot not_equal comparison failed")
        };

        const string toolName = "write_file";
        var toolExecutor = new FakeVerifiableToolExecutor(toolName, "filesystem", "File written.");
        var planner = new SingleStepPlanner(toolName);

        // The LLM emits the tool call on the step instruction, then returns a summary
        var callCount = 0;
        var agent = CreateAgent(lastUserMsg =>
        {
            callCount++;
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains(toolName))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"hello\"}}}}]";
            return "Summary complete.";
        }, planner, toolExecutor, verifier);

        // Act
        var response = await agent.ChatAsync("Write hello to test.txt");

        // Assert: plan history contains a [VerificationWarning] message from the tool result
        // (the verifier returned Failed, so ExecuteToolWithTimeoutAsync decorates the result)
        var history = agent.ConversationHistory;
        var hasWarning = history.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.True(hasWarning,
            "History should contain a [VerificationWarning] entry when verification fails");

        // Verifier was called at least once for VerifyAsync
        Assert.True(verifier.VerifyCallCount >= 1,
            "VerifyAsync should have been called at least once");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: WriteFile_PostReadDiffers_ReturnsCleanResult
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteFile_PostReadDiffers_ReturnsCleanResult()
    {
        // Arrange: verifier returns Verified (post-snapshot differs from pre)
        var verifier = new ScriptedToolVerifier
        {
            CaptureResult = new Dictionary<string, object> { ["value"] = string.Empty },
            VerifyResult = VerificationOutcome.Verified()
        };

        const string toolName = "write_file";
        var toolExecutor = new FakeVerifiableToolExecutor(toolName, "filesystem", "File written.");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains(toolName))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"hello\"}}}}]";
            return "All done.";
        }, planner, toolExecutor, verifier);

        // Act
        await agent.ChatAsync("Write hello to test.txt");

        // Assert: no [VerificationWarning] in history — result passes cleanly
        var history = agent.ConversationHistory;
        var hasWarning = history.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.False(hasWarning,
            "History should NOT contain [VerificationWarning] when verification succeeds");

        // Verifier was called
        Assert.True(verifier.VerifyCallCount >= 1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: NoRuleMatch_NoVerificationEffect_ReturnsRawResult
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoRuleMatch_NoVerificationEffect_ReturnsRawResult()
    {
        // Arrange: verifier returns NoRule (list_directory: mutates=false, no rule)
        var verifier = new ScriptedToolVerifier
        {
            CaptureResult = null,               // no pre-snapshot for non-diff rules
            VerifyResult = VerificationOutcome.NoRule()
        };

        const string toolName = "list_directory";
        var toolExecutor = new FakeVerifiableToolExecutor(toolName, "filesystem", "file1.txt\nfile2.txt");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains(toolName))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/\"}}}}]";
            return "Listed files.";
        }, planner, toolExecutor, verifier);

        // Act
        await agent.ChatAsync("List the root directory");

        // Assert: no [VerificationWarning] — NoRule means pass-through
        var history = agent.ConversationHistory;
        var hasWarning = history.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.False(hasWarning,
            "History should NOT contain [VerificationWarning] when no rule matches");

        // VerifyAsync was still called (we call it regardless and check outcome)
        Assert.True(verifier.VerifyCallCount >= 1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: VerificationDisabled_PassThrough_VerifyNeverCalled
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerificationDisabled_PassThrough_VerifyNeverCalled()
    {
        // Arrange: ToolVerificationEnabled = false
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.ToolVerificationEnabled = false;   // disabled

        var verifier = new ScriptedToolVerifier
        {
            VerifyResult = VerificationOutcome.Failed("should not be called")
        };

        const string toolName = "write_file";
        var toolExecutor = new FakeVerifiableToolExecutor(toolName, "filesystem", "File written.");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains(toolName))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\",\"content\":\"x\"}}}}]";
            return "Done.";
        }, planner, toolExecutor, verifier, config);

        // Act
        await agent.ChatAsync("Write to file");

        // Assert: VerifyAsync never called because feature is disabled
        Assert.Equal(0, verifier.VerifyCallCount);
        Assert.Equal(0, verifier.CaptureCallCount);

        // No warning in history
        var hasWarning = agent.ConversationHistory.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.False(hasWarning);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: VerifierNull_NoVerification_ReturnsRawResult
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifierNull_NoVerification_ReturnsRawResult()
    {
        // Arrange: no verifier wired (toolVerifier = null, simulates non-MCP setup)
        const string toolName = "read_text_file";
        var toolExecutor = new FakeVerifiableToolExecutor(toolName, "filesystem", "File content here.");
        var planner = new SingleStepPlanner(toolName);

        // toolVerifier: null (default) → verification gate bypassed entirely
        var agent = CreateAgent(lastUserMsg =>
        {
            if (lastUserMsg.Contains("[PLANNER]") && lastUserMsg.Contains(toolName))
                return $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/test.txt\"}}}}]";
            return "Content summarized.";
        }, planner, toolExecutor, toolVerifier: null);

        // Act
        await agent.ChatAsync("Read the file");

        // Assert: no [VerificationWarning] — verifier is absent
        var hasWarning = agent.ConversationHistory.Any(m =>
            m.Content.Contains("[VerificationWarning]", StringComparison.Ordinal));
        Assert.False(hasWarning,
            "History should not contain [VerificationWarning] when no verifier is registered");
    }
}
