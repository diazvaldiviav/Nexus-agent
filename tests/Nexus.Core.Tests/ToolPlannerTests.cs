using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Memory.Abstractions;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for ToolPlanner — AC-10, §14, §15.
/// All tests use hand-rolled fakes; no Moq/NSubstitute dependency needed in Core.Tests.
/// </summary>
public class ToolPlannerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Fakes (inner classes — keep test file self-contained)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal ILlmProvider fake. ProviderName must match config.Models.Local.Provider
    /// so that LlmProviderFactory.GetRequiredProvider resolves it correctly.
    /// Optionally delays ChatAsync by <see cref="Delay"/> before returning, enabling timeout tests.
    /// </summary>
    private sealed class FakeLlmProvider : ILlmProvider
    {
        public string ProviderName { get; }

        /// <summary>Response returned for every ChatAsync call.</summary>
        public string NextResponse { get; set; } = string.Empty;

        /// <summary>
        /// Optional delay applied inside ChatAsync before returning.
        /// Default is <see cref="TimeSpan.Zero"/> — no delay (backward-compatible).
        /// </summary>
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public FakeLlmProvider(string providerName = "ollama")
        {
            ProviderName = providerName;
        }

        public async Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            return NextResponse;
        }

        public IAsyncEnumerable<string> ChatStreamAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Not used in ToolPlanner tests.");
    }

    /// <summary>
    /// Simple ILogger implementation that records log entries for assertion in tests.
    /// Thread-safe for single-threaded test scenarios.
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

        public bool HasWarning(string substring) =>
            _entries.Any(e => e.Level == LogLevel.Warning &&
                              e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));

        public int WarningCount(string substring) =>
            _entries.Count(e => e.Level == LogLevel.Warning &&
                                e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (ToolPlanner planner, FakeLlmProvider fakeProvider) CreatePlanner(
        bool planningEnabled,
        string nextResponse = "",
        ILogger<ToolPlanner>? logger = null,
        int toolPlanningTimeoutSeconds = 30)
    {
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = planningEnabled;
        // Local provider name defaults to "ollama" — fake must match
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlanningTimeoutSeconds = toolPlanningTimeoutSeconds;

        var fakeProvider = new FakeLlmProvider("ollama") { NextResponse = nextResponse };
        var factory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        var planner = new ToolPlanner(factory, config, logger);
        return (planner, fakeProvider);
    }

    // Tool-definitions string used across multiple tests
    private const string TwoToolDefs =
        "- read_text_file: Reads a text file from disk\n" +
        "- list_directory: Lists all files in a directory";

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: ToolPlanningDisabled_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolPlanningDisabled_ReturnsNull()
    {
        // Arrange
        var (planner, _) = CreatePlanner(
            planningEnabled: false,
            nextResponse: "Step 1: Read the file");

        // Act
        var result = await planner.GeneratePlanAsync("read my file", TwoToolDefs);

        // Assert — gate 1 fires: feature disabled → null (no LLM call, no matching)
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: EmptyToolDefinitions_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyToolDefinitions_ReturnsNull()
    {
        // Arrange
        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: "Step 1: Read the file");

        // Act — pass empty string as toolDefinitions (gate 2)
        var result = await planner.GeneratePlanAsync("read my file", "");

        // Assert — gate 2 fires: null
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: ParsePlanSteps_NumberedFormat
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePlanSteps_NumberedFormat()
    {
        // Arrange: LLM returns "1. Read the file\n2. Write the file"
        // Steps don't contain tool names verbatim → MatchedToolName may be null.
        // We only care that 2 ToolPlanStep entries come back in order.
        const string llmResponse = "1. Read the file\n2. Write the file";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("read and write a file", TwoToolDefs);

        // Assert: 2 steps parsed in order
        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].StepNumber);
        Assert.Equal("Read the file", result.Steps[0].Description);
        Assert.Equal(2, result.Steps[1].StepNumber);
        Assert.Equal("Write the file", result.Steps[1].Description);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: ParsePlanSteps_StepNFormat
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePlanSteps_StepNFormat()
    {
        // Arrange: LLM returns "Step 1: Read\nStep 2: Write"
        const string llmResponse = "Step 1: Read\nStep 2: Write";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("do tasks", TwoToolDefs);

        // Assert: 2 steps parsed via "Step N:" format
        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].StepNumber);
        Assert.Equal("Read", result.Steps[0].Description);
        Assert.Equal(2, result.Steps[1].StepNumber);
        Assert.Equal("Write", result.Steps[1].Description);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: ParsePlanSteps_MoreThan5_TruncatedTo5
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePlanSteps_MoreThan5_TruncatedTo5()
    {
        // Arrange: 7 numbered steps → must be truncated to 5 (MaxSteps constant)
        const string llmResponse =
            "1. Step one\n" +
            "2. Step two\n" +
            "3. Step three\n" +
            "4. Step four\n" +
            "5. Step five\n" +
            "6. Step six\n" +
            "7. Step seven";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("do many things", TwoToolDefs);

        // Assert: exactly 5 steps (MaxSteps truncation)
        Assert.NotNull(result);
        Assert.Equal(5, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].StepNumber);
        Assert.Equal(5, result.Steps[4].StepNumber);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: ParsePlanSteps_EmptyOrGarbage_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePlanSteps_EmptyOrGarbage_ReturnsNull()
    {
        // Arrange: LLM returns garbage that matches no step patterns
        const string llmResponse = "I have no idea";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("do something", TwoToolDefs);

        // Assert: zero parseable steps → null
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: LlmCallTimeout_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmCallTimeout_ReturnsNull()
    {
        // Arrange: config timeout = 1 second; FakeLlmProvider delays 3 seconds.
        // The linked CTS fires before the fake responds → GeneratePlanAsync must return null
        // and log a warning, without cancelling the caller's CancellationToken.
        const string toolDefs =
            "- read_text_file: Reads a text file from disk\n" +
            "- list_directory: Lists all files in a directory";

        var logger = new CapturingLogger<ToolPlanner>();

        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlanningTimeoutSeconds = 1;   // 1-second timeout

        var fakeProvider = new FakeLlmProvider("ollama")
        {
            NextResponse = "1. Read the file",
            Delay = TimeSpan.FromSeconds(3)   // 3 s > 1 s timeout
        };
        var factory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var planner = new ToolPlanner(factory, config, logger: logger);

        using var callerCts = new CancellationTokenSource();

        // Act — caller's CT is never cancelled; only the internal timeout fires
        var result = await planner.GeneratePlanAsync("read a file", toolDefs, callerCts.Token);

        // Assert: graceful null return on timeout
        Assert.Null(result);

        // Caller's CT must NOT have been cancelled
        Assert.False(callerCts.Token.IsCancellationRequested,
            "Caller CancellationToken must not be cancelled when internal timeout fires");

        // Warning about timeout must be logged
        Assert.True(logger.HasWarning("timed out"),
            "Expected a warning log containing 'timed out'");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: LlmReturnsMoreThanMaxSteps_TruncatedAndLogged
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsMoreThanMaxSteps_TruncatedAndLogged()
    {
        // Arrange: LLM returns 8 numbered steps; plan must be truncated to 5 (MaxSteps constant).
        // An Info log about truncation should be emitted for the extra steps.
        const string llmResponse =
            "1. Step one\n" +
            "2. Step two\n" +
            "3. Step three\n" +
            "4. Step four\n" +
            "5. Step five\n" +
            "6. Step six\n" +
            "7. Step seven\n" +
            "8. Step eight";

        var logger = new CapturingLogger<ToolPlanner>();

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse,
            logger: logger);

        // Act
        var result = await planner.GeneratePlanAsync("do many things", TwoToolDefs);

        // Assert: exactly 5 steps (MaxSteps cap)
        Assert.NotNull(result);
        Assert.Equal(5, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].StepNumber);
        Assert.Equal(5, result.Steps[4].StepNumber);

        // Truncation must have been logged (Information level, message contains "truncat")
        var hasTruncationLog = logger.Entries.Any(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("truncat", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasTruncationLog,
            "Expected an Information log about plan truncation when LLM returns more than MaxSteps");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: FuzzyMatch_Tier1_ExactToolNameInDescription
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FuzzyMatch_Tier1_ExactToolNameInDescription()
    {
        // Arrange: step description contains the literal tool name "directory_tree"
        // → Tier 1 (exact OrdinalIgnoreCase substring) fires → Similarity = 1.0f
        const string toolDefs =
            "- directory_tree: Recursive directory tree view\n" +
            "- list_directory: Lists files";
        const string llmResponse = "1. Use directory_tree to inspect the project";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("inspect project structure", toolDefs);

        // Assert: single step matched via Tier 1 with perfect similarity
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("directory_tree", result.Steps[0].MatchedToolName);
        Assert.Equal(1.0f, result.Steps[0].Similarity);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: FuzzyMatch_Tier1_CaseInsensitive
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FuzzyMatch_Tier1_CaseInsensitive()
    {
        // Arrange: step description contains the tool name in uppercase "DIRECTORY_TREE"
        // → Tier 1 uses OrdinalIgnoreCase → still matches "directory_tree" → Similarity = 1.0f
        const string toolDefs =
            "- directory_tree: Recursive directory tree view\n" +
            "- list_directory: Lists files";
        const string llmResponse = "1. Use DIRECTORY_TREE to inspect";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("inspect project", toolDefs);

        // Assert: matched to lowercase tool name with exact similarity
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("directory_tree", result.Steps[0].MatchedToolName);
        Assert.Equal(1.0f, result.Steps[0].Similarity);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: FuzzyMatch_Tier2_NormalizedUnderscores
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FuzzyMatch_Tier2_NormalizedUnderscores()
    {
        // Arrange: step description does NOT contain literal "list_directory" (Tier 1 miss),
        // but after normalization (underscores → spaces, lowercase) "list_directory" becomes
        // "list directory" which IS a contiguous substring of "list directory to inspect the folder".
        // → Tier 2 fires → Similarity = NormalizedMatchScore (0.9f).
        //
        // NOTE: description must be "list directory to inspect the folder" — NOT
        // "list the directory …" because "list directory" would not be contiguous in that case.
        const string toolDefs = "- list_directory: Lists files";
        const string llmResponse = "1. list directory to inspect the folder";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("list directory contents", toolDefs);

        // Assert: Tier 2 normalized match
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("list_directory", result.Steps[0].MatchedToolName);
        Assert.Equal(0.9f, result.Steps[0].Similarity);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: FuzzyMatch_Tier3_TokenOverlap
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FuzzyMatch_Tier3_TokenOverlap()
    {
        // Arrange: tool name "read_text_file_lines" has 4 normalized tokens: {read, text, file, lines}.
        // Step description "read text from file" has tokens {read, text, from, file}.
        // Tier 1 miss: literal "read_text_file_lines" not in description.
        // Tier 2 miss: "read text file lines" is not a contiguous substring of "read text from file".
        // Tier 3: overlap = {read, text, file} = 3; ratio = 3/4 = 0.75 ∈ [0.7, 0.9) → match.
        //
        // Using a 4-token tool name is critical to land the ratio in [0.7, 0.9).
        // A 3-token tool would yield ratio 3/3 = 1.0, violating the < 0.9f bound.
        const string toolDefs = "- read_text_file_lines: Reads specific lines";
        const string llmResponse = "1. read text from file";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("read lines from file", toolDefs);

        // Assert: Tier 3 token-overlap match; similarity is the raw ratio (0.75f)
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("read_text_file_lines", result.Steps[0].MatchedToolName);
        Assert.True(result.Steps[0].Similarity >= 0.7f,
            $"Expected Similarity >= 0.7f but got {result.Steps[0].Similarity}");
        Assert.True(result.Steps[0].Similarity < 0.9f,
            $"Expected Similarity < 0.9f (Tier 3 bound) but got {result.Steps[0].Similarity}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 13: FuzzyMatch_NoMatch_ReturnsNullMatched
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FuzzyMatch_NoMatch_ReturnsNullMatched()
    {
        // Arrange: step description "greet the user politely" shares no tokens with
        // "directory_tree" ({directory, tree}) → all tiers miss → MatchedToolName = null, Similarity = 0f.
        const string toolDefs = "- directory_tree: Recursive directory tree view";
        const string llmResponse = "1. greet the user politely";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("say hello", toolDefs);

        // Assert: plan is non-null (step parsed), but step has no matched tool
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Null(result.Steps[0].MatchedToolName);
        Assert.Equal(0f, result.Steps[0].Similarity);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 14: FullPlanning_FakeLlm_ReturnsValidPlanWithFuzzyMatches
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullPlanning_FakeLlm_ReturnsValidPlanWithFuzzyMatches()
    {
        // Arrange: full happy path with two steps, each containing the literal tool name
        // → both matched via Tier 1 with Similarity = 1.0f.
        const string toolDefs =
            "- directory_tree: Recursive directory tree view\n" +
            "- list_directory: Lists files";
        const string llmResponse =
            "1. Use directory_tree to start\n" +
            "2. Use list_directory to enumerate";

        var (planner, _) = CreatePlanner(
            planningEnabled: true,
            nextResponse: llmResponse);

        // Act
        var result = await planner.GeneratePlanAsync("explore the project", toolDefs);

        // Assert: non-null plan, 2 matched steps, correct tool names and similarities
        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);

        Assert.Equal("directory_tree", result.Steps[0].MatchedToolName);
        Assert.Equal(1.0f, result.Steps[0].Similarity);

        Assert.Equal("list_directory", result.Steps[1].MatchedToolName);
        Assert.Equal(1.0f, result.Steps[1].Similarity);

        Assert.Equal(llmResponse, result.RawPlanText);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers for prompt-capture tests (AC-3 backward compat + context injection)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LLM provider that records every prompt sent to it.
    /// The prompt is the Content of the first (and only) history entry the planner builds.
    /// </summary>
    private sealed class CapturingLlmProvider : ILlmProvider
    {
        public string ProviderName { get; }

        /// <summary>The full prompt string from the most recent ChatAsync call.</summary>
        public string? LastPrompt { get; private set; }

        /// <summary>Fixed response returned for every call.</summary>
        public string NextResponse { get; set; } = "1. Use read_text_file to read it";

        public CapturingLlmProvider(string providerName = "ollama")
        {
            ProviderName = providerName;
        }

        public Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
        {
            // ToolPlanner always builds a single-user-message history where [0].Content is the prompt.
            LastPrompt = conversationHistory.FirstOrDefault()?.Content;
            return Task.FromResult(NextResponse);
        }

        public IAsyncEnumerable<string> ChatStreamAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversationHistory,
            string model,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Not used in ToolPlanner tests.");
    }

    private static (ToolPlanner planner, CapturingLlmProvider capturingProvider) CreateCapturingPlanner()
    {
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlanningTimeoutSeconds = 30;

        var capturingProvider = new CapturingLlmProvider("ollama");
        var factory = new LlmProviderFactory(new ILlmProvider[] { capturingProvider });
        var planner = new ToolPlanner(factory, config);
        return (planner, capturingProvider);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 15: GeneratePlanAsync_WithNullContext_ProducesIdenticalPromptToPhase8
    //          AC-3 backward compatibility guarantee
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePlanAsync_WithNullContext_ProducesIdenticalPromptToPhase8()
    {
        // Arrange
        var (planner, capturingProvider) = CreateCapturingPlanner();
        const string toolDefs = TwoToolDefs;
        const string userMsg = "read my file";

        // Compute the Phase 8 baseline prompt independently:
        // Phase 8 template (before the {context} placeholder was added) was:
        //   "... {context}Task: {userMessage} ..."
        // When {context} is "" that is byte-identical to the current template with no context.
        // We simulate by calling the 3-arg overload (forwards to 4-arg with context=null).
        _ = await planner.GeneratePlanAsync(userMsg, toolDefs);
        var baseline3ArgPrompt = capturingProvider.LastPrompt;
        Assert.NotNull(baseline3ArgPrompt);

        // Also call the 4-arg overload explicitly with null context
        _ = await planner.GeneratePlanAsync(userMsg, toolDefs, context: null);
        var nullContextPrompt = capturingProvider.LastPrompt;
        Assert.NotNull(nullContextPrompt);

        // Also call the 4-arg overload with PlannerContext.Empty
        _ = await planner.GeneratePlanAsync(userMsg, toolDefs, PlannerContext.Empty);
        var emptyContextPrompt = capturingProvider.LastPrompt;
        Assert.NotNull(emptyContextPrompt);

        // All three calls must produce byte-identical prompts
        Assert.Equal(baseline3ArgPrompt, nullContextPrompt);
        Assert.Equal(baseline3ArgPrompt, emptyContextPrompt);

        // The prompt must NOT contain the context section header
        Assert.DoesNotContain("## Conversation Context", baseline3ArgPrompt);

        // The prompt must still contain the task section
        Assert.Contains($"Task: {userMsg}", baseline3ArgPrompt);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 16: GeneratePlanAsync_WithContext_PromptIncludesContextBlock
    //          AC-3 context injection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePlanAsync_WithContext_PromptIncludesContextBlock()
    {
        // Arrange: build a non-empty PlannerContext
        var context = new PlannerContext(
            Summary: "User is editing a config file at D:\\project\\nexus.yaml",
            RecentTurns: new[] { "user: Check D:\\project\\nexus.yaml", "assistant: File opened." },
            TotalBytes: 80);

        var (planner, capturingProvider) = CreateCapturingPlanner();
        const string toolDefs = TwoToolDefs;
        const string userMsg = "read my file";

        // Act
        _ = await planner.GeneratePlanAsync(userMsg, toolDefs, context);
        var capturedPrompt = capturingProvider.LastPrompt;
        Assert.NotNull(capturedPrompt);

        // Assert: context block headers are present
        Assert.Contains("## Conversation Context", capturedPrompt);

        // The Summary is injected
        Assert.Contains("D:\\project\\nexus.yaml", capturedPrompt);

        // Recent turns section is present
        Assert.Contains("Recent turns:", capturedPrompt);

        // Individual turns are listed
        Assert.Contains("user: Check D:\\project\\nexus.yaml", capturedPrompt);
        Assert.Contains("assistant: File opened.", capturedPrompt);

        // The task section still follows the context block
        Assert.Contains($"Task: {userMsg}", capturedPrompt);

        // Context block precedes the task line
        var contextHeaderPos = capturedPrompt.IndexOf("## Conversation Context", StringComparison.Ordinal);
        var taskPos = capturedPrompt.IndexOf($"Task: {userMsg}", StringComparison.Ordinal);
        Assert.True(contextHeaderPos < taskPos,
            "Context block must appear before 'Task:' line in the prompt");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 2 (Sprint 10 follow-up) — Embedding fallback tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hand-rolled IEmbeddingService that returns deterministic per-text vectors,
    /// optionally throws on demand, and tracks call counts.
    /// </summary>
    private sealed class FakeEmbeddingService : Nexus.Memory.Abstractions.IEmbeddingService
    {
        // Pre-loaded embeddings keyed by exact input text. Tests fill this with
        // hand-tuned vectors so cosine-similarity outcomes are reproducible.
        public Dictionary<string, float[]> Vectors { get; } = new(StringComparer.Ordinal);

        public bool ShouldThrow { get; set; }
        public int CallCount { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ShouldThrow)
                throw new InvalidOperationException("simulated embedding failure");
            if (Vectors.TryGetValue(text, out var vec))
                return Task.FromResult(vec);
            // Default: zero vector so cosine sim = 0 → no match.
            return Task.FromResult(new float[8]);
        }
    }

    private static (ToolPlanner planner, FakeLlmProvider fakeProvider, FakeEmbeddingService embeddings)
        CreatePlannerWithEmbeddings(
            string nextResponse,
            bool fallbackEnabled = true,
            float threshold = 0.65f,
            IEmbeddingService? overrideEmbeddings = null)
    {
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlanningTimeoutSeconds = 30;
        config.Mcp.ToolPlannerEmbeddingFallbackEnabled = fallbackEnabled;
        config.Mcp.ToolPlannerEmbeddingMatchThreshold = threshold;

        var fakeProvider = new FakeLlmProvider("ollama") { NextResponse = nextResponse };
        var factory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });

        var embeddings = new FakeEmbeddingService();
        var planner = new ToolPlanner(
            factory, config, logger: null,
            embeddingService: overrideEmbeddings ?? embeddings);
        return (planner, fakeProvider, embeddings);
    }

    private const string EmbedTwoToolDefs =
        "- read_text_file: Reads a text file from disk\n" +
        "- write_file: Writes or overwrites file content";

    /// <summary>
    /// Layer 2 — Test A: lexical Tier 1-3 fail (no tool name + no token overlap),
    /// embedding sim ≥ threshold → matched via fallback.
    /// </summary>
    [Fact]
    public async Task EmbeddingFallback_LexicalNullButEmbeddingMatches_UsesFallback()
    {
        // Arrange — LLM uses "Save" with no tool name → lexical Tier 1-3 all miss
        var llmPlan = "Step 1: Save the modified content back to the file";
        var (planner, _, embeddings) = CreatePlannerWithEmbeddings(llmPlan);

        // Tune embeddings: step description close to write_file, far from read_text_file
        embeddings.Vectors["Save the modified content back to the file"] = new[] { 1f, 0.9f, 0f };
        embeddings.Vectors["read_text_file: Reads a text file from disk"] = new[] { 0.1f, 0f, 1f };
        embeddings.Vectors["write_file: Writes or overwrites file content"] = new[] { 0.95f, 1f, 0f };

        // Act
        var plan = await planner.GeneratePlanAsync("modify the file", EmbedTwoToolDefs);

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Equal("write_file", plan.Steps[0].MatchedToolName);
        Assert.True(plan.Steps[0].Similarity >= 0.65f,
            $"Expected similarity ≥ 0.65, got {plan.Steps[0].Similarity}");
    }

    /// <summary>
    /// Layer 2 — Test B: lexical fails, embedding similarity below threshold → no match.
    /// </summary>
    [Fact]
    public async Task EmbeddingFallback_BelowThreshold_ReturnsNull()
    {
        var llmPlan = "Step 1: Do something abstract that no tool fits";
        var (planner, _, embeddings) = CreatePlannerWithEmbeddings(llmPlan, threshold: 0.65f);

        // Step vector is roughly orthogonal to both tools → cosine ~0 < 0.65
        embeddings.Vectors["Do something abstract that no tool fits"] = new[] { 0f, 0f, 1f };
        embeddings.Vectors["read_text_file: Reads a text file from disk"] = new[] { 1f, 0f, 0f };
        embeddings.Vectors["write_file: Writes or overwrites file content"] = new[] { 0f, 1f, 0f };

        var plan = await planner.GeneratePlanAsync("do abstract", EmbedTwoToolDefs);

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Null(plan.Steps[0].MatchedToolName);
    }

    /// <summary>
    /// Layer 2 — Test C: IEmbeddingService is null → no fallback attempted; behaviour
    /// byte-equivalent to lexical-only (matched stays null when Tier 1-3 fails).
    /// </summary>
    [Fact]
    public async Task EmbeddingFallback_NullEmbeddingService_FallsBackToCurrentBehavior()
    {
        var llmPlan = "Step 1: Insert a section here";   // no tool name → lexical fails
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlannerEmbeddingFallbackEnabled = true;   // gate ON but service NULL

        var fakeProvider = new FakeLlmProvider("ollama") { NextResponse = llmPlan };
        var factory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var planner = new ToolPlanner(factory, config, logger: null, embeddingService: null);

        var plan = await planner.GeneratePlanAsync("insert", EmbedTwoToolDefs);

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Null(plan.Steps[0].MatchedToolName);
    }

    /// <summary>
    /// Layer 2 — Test D: gate disabled → fallback is skipped even when service available.
    /// </summary>
    [Fact]
    public async Task EmbeddingFallback_Disabled_SkipsEvenWhenServiceAvailable()
    {
        var llmPlan = "Step 1: Save it";
        var (planner, _, embeddings) = CreatePlannerWithEmbeddings(llmPlan, fallbackEnabled: false);

        // Even if vectors would have matched, gate=off prevents the call entirely.
        embeddings.Vectors["Save it"] = new[] { 1f, 0f, 0f };
        embeddings.Vectors["read_text_file: Reads a text file from disk"] = new[] { 0f, 1f, 0f };
        embeddings.Vectors["write_file: Writes or overwrites file content"] = new[] { 1f, 0f, 0f };

        var plan = await planner.GeneratePlanAsync("save", EmbedTwoToolDefs);

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Null(plan.Steps[0].MatchedToolName);
        Assert.Equal(0, embeddings.CallCount);  // service never invoked
    }

    /// <summary>
    /// Layer 2 — Test E: embedding service throws → graceful degradation
    /// (returns step unchanged, logs Warning).
    /// </summary>
    [Fact]
    public async Task EmbeddingFallback_ServiceThrows_ReturnsNullAndLogsWarning()
    {
        var llmPlan = "Step 1: Insert a row";
        var config = new NexusConfig();
        config.Mcp.ToolPlanningEnabled = true;
        config.Models.Local.Provider = "ollama";
        config.Models.Local.Model = "test-model";
        config.Mcp.ToolPlannerEmbeddingFallbackEnabled = true;

        var fakeProvider = new FakeLlmProvider("ollama") { NextResponse = llmPlan };
        var factory = new LlmProviderFactory(new ILlmProvider[] { fakeProvider });
        var embeddings = new FakeEmbeddingService { ShouldThrow = true };
        var capturingLogger = new CapturingLogger<ToolPlanner>();
        var planner = new ToolPlanner(factory, config, capturingLogger, embeddings);

        var plan = await planner.GeneratePlanAsync("insert", EmbedTwoToolDefs);

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Null(plan.Steps[0].MatchedToolName);
        Assert.True(capturingLogger.HasWarning("embedding fallback failed"),
            "Expected Warning log mentioning 'embedding fallback failed'");
    }
}
