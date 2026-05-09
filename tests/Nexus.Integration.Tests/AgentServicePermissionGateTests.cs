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
using Nexus.Memory.Processing;

namespace Nexus.Integration.Tests;

/// <summary>
/// Integration tests that verify IPermissionGate is correctly wired into
/// ExecuteToolWithTimeoutAsync (AC-5) via the AgentService plan-execute path.
/// </summary>
public class AgentServicePermissionGateTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly KnowledgeGraph _graph;
    private readonly string _connectionString;
    private AgentService? _lastAgent;

    public AgentServicePermissionGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"perm_gate_test_{Guid.NewGuid():N}.db");
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
    /// Scripted IPermissionGate that records every RequestAsync call.
    /// Responder controls the response; defaults to Allow.
    /// </summary>
    private sealed class FakePermissionGate : IPermissionGate
    {
        public List<PermissionRequest> Requests { get; } = new();
        public Func<PermissionRequest, PermissionGateResponse>? Responder { get; set; }

        public Task<PermissionGateResponse> RequestAsync(PermissionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var resp = Responder?.Invoke(request) ?? new PermissionGateResponse(PermissionDecision.Allow);
            return Task.FromResult(resp);
        }
    }

    /// <summary>
    /// IPermissionGate that throws an exception — used for the gate-throws safety test.
    /// </summary>
    private sealed class ThrowingPermissionGate : IPermissionGate
    {
        public Task<PermissionGateResponse> RequestAsync(PermissionRequest request, CancellationToken ct)
            => throw new InvalidOperationException("gate crashed");
    }

    /// <summary>
    /// IVerificationCatalog stub backed by a fixed set of rules.
    /// </summary>
    private sealed class StubVerificationCatalog : IVerificationCatalog
    {
        private readonly Dictionary<string, VerificationRule> _rules;

        public StubVerificationCatalog(params VerificationRule[] rules)
        {
            _rules = new Dictionary<string, VerificationRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
                _rules[$"{rule.Server}:{rule.Tool}"] = rule;
        }

        public int Count => _rules.Count;

        public VerificationRule? GetRule(string server, string tool)
            => _rules.TryGetValue($"{server}:{tool}", out var rule) ? rule : null;
    }

    /// <summary>
    /// Fake IToolExecutor that reports a fixed server name and tracks invocations.
    /// </summary>
    private sealed class FakePermissionToolExecutor : IToolExecutor
    {
        private readonly string _toolName;
        private readonly string _serverName;
        private readonly string _toolResult;

        public FakePermissionToolExecutor(string toolName, string serverName = "filesystem", string toolResult = "OK")
        {
            _toolName = toolName;
            _serverName = serverName;
            _toolResult = toolResult;
        }

        public bool HasTools => true;
        public int InvokeCount { get; private set; }

        public string GetToolDefinitionsForPrompt() => $"- {_toolName}: Test tool";
        public string GetToolDefinitionsForPrompt(string? modelName) => GetToolDefinitionsForPrompt();

        public string GetToolServerName(string toolName) =>
            string.Equals(toolName, _toolName, StringComparison.OrdinalIgnoreCase)
                ? _serverName
                : string.Empty;

        public Task<string> InvokeToolAsync(
            string serverName,
            string toolName,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            return Task.FromResult(_toolResult);
        }
    }

    /// <summary>
    /// Single-step planner that always returns a plan with one step for the specified tool.
    /// </summary>
    private sealed class SingleStepPlanner : IToolPlanner
    {
        private readonly ToolPlan _plan;

        public SingleStepPlanner(string toolName)
        {
            _plan = new ToolPlan(
                new[] { new ToolPlanStep(1, $"Execute {toolName}", toolName, 0.95f) },
                "Scripted permission gate test plan");
        }

        public Task<ToolPlan?> GeneratePlanAsync(
            string userMessage,
            string toolDefinitionsForPrompt,
            PlannerContext? context,
            CancellationToken ct = default)
            => Task.FromResult<ToolPlan?>(_plan);
    }

    /// <summary>
    /// Capturing ILogger for AgentService — records entries for assertion.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Agent factory helper
    // ──────────────────────────────────────────────────────────────────────────

    private AgentService CreateAgent(
        Func<string, string> llmResponseFactory,
        IToolPlanner? toolPlanner = null,
        IToolExecutor? toolExecutor = null,
        IPermissionGate? permissionGate = null,
        IVerificationCatalog? verificationCatalog = null,
        NexusConfig? config = null,
        ILogger<AgentService>? logger = null)
    {
        var callerSuppliedConfig = config is not null;
        config ??= new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Mcp.PlannerContextEnabled = false;
        config.Mcp.ToolVerificationEnabled = false;
        config.Mcp.PlannerHeuristicEnabled = false;
        // Only set Permission.Enabled when using the default config; callers that supply a
        // config own the value (e.g. GateDisabled_Bypasses sets it to false).
        if (!callerSuppliedConfig)
            config.Permission.Enabled = true;

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
            permissionGate: permissionGate,
            verificationCatalog: verificationCatalog,
            toolExecutor: toolExecutor,
            logger: logger);

        _lastAgent = agent;
        return agent;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: DestructiveTool_TriggersGate
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DestructiveTool_TriggersGate()
    {
        // Arrange
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate();

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog);

        // Act
        await agent.ChatAsync("Delete the test file");

        // Assert: gate was consulted exactly once with correct info
        var req = Assert.Single(gate.Requests);
        Assert.Equal(toolName, req.ToolName);
        Assert.Equal(serverName, req.ServerName);
        Assert.Equal("destructive operation", req.Rationale);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: NonDestructiveTool_BypassesGate
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonDestructiveTool_BypassesGate()
    {
        // Arrange
        const string toolName = "read_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate();

        // Catalog rule exists but Destructive = false
        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = false
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File content.");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog);

        // Act
        await agent.ChatAsync("Read the test file");

        // Assert: gate NOT consulted (non-destructive, no config "ask")
        Assert.Empty(gate.Requests);
        Assert.Equal(1, toolExecutor.InvokeCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Deny_Returns_PermissionDeniedSentinel
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deny_Returns_PermissionDeniedSentinel()
    {
        // Arrange
        const string toolName = "move_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate
        {
            Responder = _ => new PermissionGateResponse(PermissionDecision.Deny)
        };

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File moved.");
        var planner = new SingleStepPlanner(toolName);

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/a.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog);

        // Act
        var response = await agent.ChatAsync("Move the file");

        // Assert: tool was NOT invoked; the history contains the PermissionDenied sentinel
        Assert.Equal(0, toolExecutor.InvokeCount);
        var history = agent.ConversationHistory;
        var hasDenialSentinel = history.Any(m =>
            m.Content?.Contains("[PermissionDenied]", StringComparison.Ordinal) == true);
        Assert.True(hasDenialSentinel, "Expected [PermissionDenied] in conversation history");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: GateDisabled_Bypasses
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GateDisabled_Bypasses()
    {
        // Arrange
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate();

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var config = new NexusConfig();
        config.Permission.Enabled = false;  // gate is DISABLED

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog, config);

        // Act
        await agent.ChatAsync("Delete the file");

        // Assert: gate never consulted when Permission.Enabled = false
        Assert.Empty(gate.Requests);
        Assert.Equal(1, toolExecutor.InvokeCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: SmallModel_DowngradesAllowForSession_ToOneShot
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SmallModel_DowngradesAllowForSession_ToOneShot()
    {
        // Arrange: gate returns AllowForSession — AgentService treats this as Allow (one execution)
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate
        {
            Responder = _ => new PermissionGateResponse(PermissionDecision.AllowForSession)
        };

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var config = new NexusConfig();
        config.Models.Local.Model = "Qwen3.5:4B";  // Limited tier (small but ≥4B; ChatOnly is bypassed entirely)

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog, config);

        // Act
        await agent.ChatAsync("Delete the test file");

        // Assert: AllowForSession is an Allow-class decision — tool executed once, no PermissionDenied
        Assert.Equal(1, toolExecutor.InvokeCount);
        var history = agent.ConversationHistory;
        var hasDenialSentinel = history.Any(m =>
            m.Content?.Contains("[PermissionDenied]", StringComparison.Ordinal) == true);
        Assert.False(hasDenialSentinel, "AllowForSession should not produce [PermissionDenied]");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: SmallModel_IgnoresPersistedAllowances
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SmallModel_IgnoresPersistedAllowances()
    {
        // Arrange: small-model + gate configured to Allow (as if persisted allow exists)
        // AgentService should still call the gate (not short-circuit) — gate decides.
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate
        {
            Responder = _ => new PermissionGateResponse(PermissionDecision.Allow)
        };

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var config = new NexusConfig();
        config.Models.Local.Model = "Qwen3.5:4B";  // Limited tier (small but ≥4B; ChatOnly is bypassed entirely)

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog, config);

        // Act
        await agent.ChatAsync("Delete the test file");

        // Assert: AgentService DID consult the gate (not short-circuited based on model tier)
        Assert.True(gate.Requests.Count > 0, "AgentService must call the gate even for small models");
        Assert.Equal(1, toolExecutor.InvokeCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: SmallModel_NonInteractive_DenyByDefault
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SmallModel_NonInteractive_DenyByDefault()
    {
        // Arrange: AutoApprovePermissionGate + small model → denies destructive tool
        const string toolName = "delete_file";
        const string serverName = "filesystem";

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var config = new NexusConfig();
        config.Models.Local.Model = "Qwen3.5:4B";  // Limited tier (small but ≥4B; ChatOnly is bypassed entirely) → AutoApprovePermissionGate denies

        var autoApproveGate = new AutoApprovePermissionGate(config);

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, autoApproveGate, catalog, config);

        // Act
        await agent.ChatAsync("Delete the test file");

        // Assert: tool was NOT invoked; PermissionDenied sentinel is in history
        Assert.Equal(0, toolExecutor.InvokeCount);
        var history = agent.ConversationHistory;
        var hasDenialSentinel = history.Any(m =>
            m.Content?.Contains("[PermissionDenied]", StringComparison.Ordinal) == true);
        Assert.True(hasDenialSentinel, "AutoApprovePermissionGate should deny small-model destructive tools");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: LargeModel_HonorsAllPersistAndSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LargeModel_HonorsAllPersistAndSession()
    {
        // Arrange: gate returns AllowPersisted for large model → tool executes
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var gate = new FakePermissionGate
        {
            Responder = _ => new PermissionGateResponse(PermissionDecision.AllowPersisted)
        };

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var config = new NexusConfig();
        config.Models.Local.Model = "qwen3:14b";  // full-tier large model

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, gate, catalog, config);

        // Act
        await agent.ChatAsync("Delete the test file");

        // Assert: gate was consulted exactly once; tool executed (AllowPersisted is Allow-class)
        Assert.Single(gate.Requests);
        Assert.Equal(1, toolExecutor.InvokeCount);
        var history = agent.ConversationHistory;
        var hasDenialSentinel = history.Any(m =>
            m.Content?.Contains("[PermissionDenied]", StringComparison.Ordinal) == true);
        Assert.False(hasDenialSentinel, "AllowPersisted should not produce [PermissionDenied]");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: GateThrows_DefaultsToAllow_AndLogsWarning
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GateThrows_DefaultsToAllow_AndLogsWarning()
    {
        // Arrange: gate throws — AgentService must still execute the tool (safety-by-default)
        const string toolName = "delete_file";
        const string serverName = "filesystem";
        var throwingGate = new ThrowingPermissionGate();

        var catalog = new StubVerificationCatalog(new VerificationRule
        {
            Server = serverName,
            Tool = toolName,
            Destructive = true
        });

        var toolExecutor = new FakePermissionToolExecutor(toolName, serverName, "File deleted.");
        var planner = new SingleStepPlanner(toolName);

        var logger = new CapturingLogger<AgentService>();

        var agent = CreateAgent(
            lastUserMsg => lastUserMsg.Contains("[PLANNER]")
                ? $"[TOOL_CALL: {{\"name\":\"{toolName}\",\"arguments\":{{\"path\":\"/tmp/test.txt\"}}}}]"
                : "Summary complete.",
            planner, toolExecutor, throwingGate, catalog,
            logger: logger);

        // Act — must not throw despite gate crashing
        await agent.ChatAsync("Delete the test file");

        // Assert: tool executed (allow-by-default when gate throws)
        Assert.Equal(1, toolExecutor.InvokeCount);

        // Assert: logger captured a Warning with "[PermissionGate]" and "gate threw"
        var warningEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Warning)
            .Select(e => e.Message)
            .ToList();
        Assert.True(
            warningEntries.Any(m =>
                m.Contains("[PermissionGate]", StringComparison.Ordinal) &&
                m.Contains("gate threw", StringComparison.Ordinal)),
            $"Expected a Warning log containing '[PermissionGate]' and 'gate threw'. Logged warnings: {string.Join("; ", warningEntries)}");

        // Assert: no [PermissionDenied] sentinel in history
        var history = agent.ConversationHistory;
        var hasDenialSentinel = history.Any(m =>
            m.Content?.Contains("[PermissionDenied]", StringComparison.Ordinal) == true);
        Assert.False(hasDenialSentinel, "Gate-throws path must not produce [PermissionDenied]");
    }
}
