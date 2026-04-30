using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;

namespace Nexus.Core.Services;

/// <summary>
/// Generates a step-by-step <see cref="ToolPlan"/> for a user message by consulting
/// the local LLM and matching each plan step to the available tool definitions.
/// </summary>
/// <remarks>
/// <para>
/// This implementation follows the same strict graceful-degradation contract as
/// <see cref="IToolPlanner"/>. Specifically, <see langword="null"/> is returned in
/// every non-cancellation failure case so the caller falls through to the existing
/// tool-call loop unchanged:
/// <list type="bullet">
///   <item><description>
///     <c>McpConfig.ToolPlanningEnabled</c> is <see langword="false"/> (feature gate off).
///   </description></item>
///   <item><description>
///     <paramref name="toolDefinitionsForPrompt"/> is <see langword="null"/>, empty, or whitespace
///     (no tools to plan with).
///   </description></item>
///   <item><description>
///     LLM failure (HTTP error, provider exception — logs a warning).
///   </description></item>
///   <item><description>
///     LLM call timeout: the internal <c>ToolPlanningTimeoutSeconds</c> deadline fires before
///     the provider responds.  The timeout is fully internal — the caller's
///     <see cref="CancellationToken"/> is not cancelled.
///   </description></item>
///   <item><description>
///     No valid steps can be parsed from the LLM output.
///   </description></item>
///   <item><description>
///     All matched steps have <c>MatchedToolName == null</c> (no tool could be identified).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <see cref="OperationCanceledException"/> is always re-thrown — cancellation propagates
/// unconditionally to the caller.
/// </para>
/// </remarks>
public sealed class ToolPlanner : IToolPlanner
{
    // ──────────────────────────────────────────────────────────────────────────
    // Constants
    // ──────────────────────────────────────────────────────────────────────────

    private const int MaxSteps = 5;
    private const float NormalizedMatchScore = 0.9f;
    private const float TokenOverlapThreshold = 0.7f;

    /// <summary>
    /// Planning prompt template.  Placeholders: {toolDefinitionsForPrompt}, {userMessage}.
    /// </summary>
    private const string PlanningPromptTemplate = """
        You are a task planner. You have these tools available:
        {toolDefinitionsForPrompt}

        Create a step-by-step plan to complete this task. Each step must use exactly one tool.
        Format each step as: Step N: description of what to do with tool_name
        You MUST output between 1 and 5 steps. Be specific about which tool to use.

        Task: {userMessage}
        """;

    // ──────────────────────────────────────────────────────────────────────────
    // Static readonly fields
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly Regex StepRegex = new(
        @"^\s*(?:step\s*)?(\d+)[.):]\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ToolLineRegex = new(
        @"^-\s+([A-Za-z0-9_]+):\s*(.*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Token separator set — single allocation, shared across all Tokenize calls.
    /// </summary>
    private static readonly char[] TokenSeparators =
        { ' ', '_', '-', '.', ':', '`', '(', ')', '[', ']', '\'', '"', ',', ';' };

    // ──────────────────────────────────────────────────────────────────────────
    // Instance fields
    // ──────────────────────────────────────────────────────────────────────────

    private readonly LlmProviderFactory _providerFactory;
    private readonly NexusConfig _config;
    private readonly ILogger<ToolPlanner>? _logger;

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="ToolPlanner"/>.
    /// </summary>
    /// <param name="providerFactory">Factory used to resolve the local LLM provider.</param>
    /// <param name="config">Application configuration (reads <c>Models.Local</c> and <c>Mcp.ToolPlanningEnabled</c>).</param>
    /// <param name="logger">Optional structured logger; <see langword="null"/> is safe.</param>
    public ToolPlanner(
        LlmProviderFactory providerFactory,
        NexusConfig config,
        ILogger<ToolPlanner>? logger = null)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IToolPlanner
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Cancellation propagates unconditionally: if <paramref name="ct"/> is cancelled at any
    /// await point, <see cref="OperationCanceledException"/> is re-thrown to the caller.
    /// </para>
    /// <para>
    /// LLM call timeout is handled internally via a linked <see cref="CancellationTokenSource"/>
    /// seeded with <c>McpConfig.ToolPlanningTimeoutSeconds</c>.  When the deadline fires,
    /// the timeout OCE is caught here and the method returns <see langword="null"/> — the
    /// caller's <paramref name="ct"/> is never cancelled.
    /// </para>
    /// </remarks>
    public async Task<ToolPlan?> GeneratePlanAsync(
        string userMessage,
        string toolDefinitionsForPrompt,
        CancellationToken ct = default)
    {
        // Gate 1: feature disabled
        if (!_config.Mcp.ToolPlanningEnabled)
            return null;

        // Gate 2: no tools to plan with
        if (string.IsNullOrWhiteSpace(toolDefinitionsForPrompt))
            return null;

        try
        {
            // 1. Build planning prompt
            var prompt = PlanningPromptTemplate
                .Replace("{toolDefinitionsForPrompt}", toolDefinitionsForPrompt)
                .Replace("{userMessage}", userMessage);

            // 2. Resolve local LLM provider and call it
            var localProviderName = _config.Models.Local.Provider;
            var provider = _providerFactory.GetRequiredProvider(localProviderName);

            var history = new List<ConversationMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            // AC-A3: apply per-plan LLM timeout; distinguish it from caller cancellation so
            // a timeout returns null (graceful) while caller cancellation re-throws (propagates).
            string rawPlan;
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_config.Mcp.ToolPlanningTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                rawPlan = await provider.ChatAsync(
                    systemPrompt: string.Empty,
                    conversationHistory: history,
                    model: _config.Models.Local.Model,
                    cancellationToken: linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout fired, not caller cancellation — degrade gracefully.
                _logger?.LogWarning(
                    "ToolPlanner LLM call timed out after {Seconds}s; falling back to normal loop",
                    _config.Mcp.ToolPlanningTimeoutSeconds);
                return null;
            }

            // AC-C3: checkpoint between the LLM response and the parsing phase
            ct.ThrowIfCancellationRequested();

            // 3. Parse steps from LLM output (capped at MaxSteps)
            var rawSteps = ParseSteps(rawPlan);
            if (rawSteps.Count == 0)
            {
                _logger?.LogInformation(
                    "ToolPlanner: no steps parsed from LLM output ({Len} chars)", rawPlan.Length);
                return null;
            }

            // 4. Extract tool names + descriptions from the prompt-formatted string
            var tools = ExtractTools(toolDefinitionsForPrompt);

            // 5. Match each step to a tool via deterministic fuzzy cascade
            var matchedSteps = new List<ToolPlanStep>(rawSteps.Count);
            foreach (var step in rawSteps)
            {
                // AC-C3: cancellation checkpoint at the top of each step iteration
                ct.ThrowIfCancellationRequested();
                matchedSteps.Add(MatchStepFuzzy(step, tools));
            }

            return new ToolPlan(matchedSteps, rawPlan);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ToolPlanner: graceful degradation — returning null");
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses numbered steps from raw LLM output.  Caps output at <see cref="MaxSteps"/>.
    /// </summary>
    private List<ToolPlanStep> ParseSteps(string raw)
    {
        var matches = StepRegex.Matches(raw);
        var steps = new List<ToolPlanStep>(Math.Min(matches.Count, MaxSteps));

        foreach (Match m in matches)
        {
            if (steps.Count >= MaxSteps)
            {
                _logger?.LogInformation(
                    "ToolPlanner: truncating plan — {Extra} step(s) beyond MaxSteps={Max} dropped",
                    matches.Count - MaxSteps, MaxSteps);
                break;
            }

            if (!int.TryParse(m.Groups[1].Value, out var stepNumber))
                continue;

            var description = m.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(description))
                continue;

            // Unmatched placeholder values — MatchStepFuzzy fills them in
            steps.Add(new ToolPlanStep(stepNumber, description, null, 0f));
        }

        return steps;
    }

    /// <summary>
    /// Extracts (Name, FullLine) tuples from a tool-definitions prompt string.
    /// Expects lines of the form <c>- toolName: description</c>.
    /// </summary>
    private static List<(string Name, string FullLine)> ExtractTools(string toolDefinitionsForPrompt)
    {
        var toolMatches = ToolLineRegex.Matches(toolDefinitionsForPrompt);
        var tools = new List<(string Name, string FullLine)>(toolMatches.Count);

        foreach (Match m in toolMatches)
        {
            var name = m.Groups[1].Value.Trim();
            var description = m.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                tools.Add((name, $"{name}: {description}"));
        }

        return tools;
    }

    /// <summary>
    /// Matches a parsed <see cref="ToolPlanStep"/> to the best-fitting tool via a 3-tier
    /// deterministic fuzzy cascade. Preferred over embedding similarity for small-model
    /// plans, which name tools literally in their step descriptions.
    /// </summary>
    /// <remarks>
    /// Cascade:
    /// <list type="number">
    ///   <item><description>
    ///     Tier 1 — exact tool-name substring (case-insensitive) in description → Similarity = 1.0f
    ///   </description></item>
    ///   <item><description>
    ///     Tier 2 — normalized (underscore/hyphen → space, lowercase) substring → Similarity = 0.9f
    ///   </description></item>
    ///   <item><description>
    ///     Tier 3 — token overlap ratio >= <see cref="TokenOverlapThreshold"/> → Similarity = ratio (strict &gt; argmax; earlier tool in list wins on ties)
    ///   </description></item>
    ///   <item><description>
    ///     None of the above → <see cref="ToolPlanStep.MatchedToolName"/> remains null
    ///   </description></item>
    /// </list>
    /// </remarks>
    private static ToolPlanStep MatchStepFuzzy(
        ToolPlanStep step,
        IReadOnlyList<(string Name, string FullLine)> tools)
    {
        if (tools.Count == 0)
            return step;

        // Tier 1: exact substring (case-insensitive)
        foreach (var (name, _) in tools)
        {
            if (step.Description.Contains(name, StringComparison.OrdinalIgnoreCase))
                return step with { MatchedToolName = name, Similarity = 1.0f };
        }

        // Tier 2: normalized substring (underscore/hyphen → space, lowercase)
        var descNormalized = Normalize(step.Description);
        foreach (var (name, _) in tools)
        {
            var nameNormalized = Normalize(name);
            if (descNormalized.Contains(nameNormalized, StringComparison.Ordinal))
                return step with { MatchedToolName = name, Similarity = NormalizedMatchScore };
        }

        // Tier 3: token overlap ratio ≥ TokenOverlapThreshold (strict > argmax)
        // Ordinal comparer is correct here — Normalize() already applied ToLowerInvariant()
        var descTokens = Tokenize(Normalize(step.Description)).ToHashSet(StringComparer.Ordinal);
        string? bestName = null;
        float bestRatio = 0f;

        foreach (var (name, _) in tools)
        {
            // Materialize tokens once to avoid double-enumeration (R-7)
            var nameTokens = Tokenize(Normalize(name)).ToArray();
            if (nameTokens.Length == 0) continue;

            var overlap = nameTokens.Count(t => descTokens.Contains(t));
            var ratio = (float)overlap / nameTokens.Length;

            // Only tools meeting the threshold participate in the argmax;
            // strict > ensures earlier tool wins on ties.
            if (ratio >= TokenOverlapThreshold && ratio > bestRatio)
            {
                bestRatio = ratio;
                bestName = name;
            }
        }

        if (bestName is not null)
            return step with { MatchedToolName = bestName, Similarity = bestRatio };

        return step;
    }

    private static string Normalize(string s) =>
        s.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

    private static IEnumerable<string> Tokenize(string s) =>
        s.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
