using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;

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
}
