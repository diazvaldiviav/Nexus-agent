using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Memory.Abstractions;

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
    /// Planning prompt template.
    /// Placeholders: {toolDefinitionsForPrompt}, {context}, {userMessage}.
    /// When {context} is substituted with an empty string the resulting prompt is
    /// byte-identical to the Phase 8 baseline (no extra whitespace is introduced).
    /// </summary>
    private const string PlanningPromptTemplate = """
        You are a task planner. You have these tools available:
        {toolDefinitionsForPrompt}

        Create a step-by-step plan to complete this task.

        FORMATTING RULE — every step MUST follow this exact pattern:
            Step N: Use `tool_name` to <do something specific>

        The tool name MUST be one of the tools listed above, wrapped in backticks.
        Do NOT use natural-language verbs like "Insert", "Save", "Add", "Modify",
        "Update", or "Edit" without explicitly naming the tool. Every step is one
        tool call.

        READ-ONLY INTENT RULE — if the user is only asking to view, read, show,
        list, find, search, describe, summarize, or check something (verbs like
        "ver", "leer", "mostrar", "listar", "qué dice", "qué contiene", "buscar",
        "show", "read", "view", "list", "find", "search", "describe", "what does",
        "what is in"), the plan MUST consist EXCLUSIVELY of read-only tools
        (typically prefixed `read_`, `list_`, `get_`, `search_`, or
        `directory_tree`). NEVER include `write_*`, `edit_*`, `delete_*`, `move_*`,
        `create_directory`, or any tool that modifies state. If a write/delete
        tool is needed to fulfill the user's request, the user must have
        explicitly asked to write/edit/delete/move — do not infer it.

        GOOD examples:
          (write intent — user said "modify config.yaml to set port=80")
          Step 1: Use `read_text_file` to retrieve the current content of config.yaml
          Step 2: Use `write_file` to save the modified content back to config.yaml

          (read intent — user said "what does index.html say")
          Step 1: Use `read_text_file` to retrieve the content of index.html

        BAD examples (DO NOT WRITE — these will be rejected):
          Step 1: Read the file                  ← missing tool name
          Step 2: Insert the new section         ← natural verb, no tool
          Step 3: Save the changes               ← natural verb, no tool

          (read intent — user said "puedes ver lo que dice index.html")
          Step 1: Use `read_text_file` to retrieve the content of index.html
          Step 2: Use `write_file` to save the modified content back to index.html
                                                  ← write_file unsolicited;
                                                    user only asked to read

        Output between 1 and 5 steps. Each step is exactly one tool invocation.

        {context}Task: {userMessage}
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
    private readonly IEmbeddingService? _embeddings;

    // Lazy cache of tool embeddings keyed by tool name. Populated once per planner
    // instance under _cacheLock; reads after the warm-up are lock-free since each
    // entry is an immutable float[].
    private readonly Dictionary<string, float[]> _toolEmbeddingCache =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="ToolPlanner"/>.
    /// </summary>
    /// <param name="providerFactory">Factory used to resolve the local LLM provider.</param>
    /// <param name="config">Application configuration (reads <c>Models.Local</c> and <c>Mcp.ToolPlanningEnabled</c>).</param>
    /// <param name="logger">Optional structured logger; <see langword="null"/> is safe.</param>
    /// <param name="embeddingService">
    /// Optional embedding service used as a Tier-4 semantic fallback when the lexical
    /// 3-tier matcher returns <see langword="null"/>. Gated by
    /// <c>McpConfig.ToolPlannerEmbeddingFallbackEnabled</c>. When the service is absent
    /// or the gate is off, behaviour is byte-equivalent to the lexical-only matcher.
    /// </param>
    public ToolPlanner(
        LlmProviderFactory providerFactory,
        NexusConfig config,
        ILogger<ToolPlanner>? logger = null,
        IEmbeddingService? embeddingService = null)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _embeddings = embeddingService;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IToolPlanner
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ToolPlan?> GeneratePlanAsync(
        string userMessage,
        string toolDefinitionsForPrompt,
        CancellationToken ct = default)
        => GeneratePlanAsync(userMessage, toolDefinitionsForPrompt, context: null, ct);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Cancellation propagates unconditionally: if <paramref name="cancellationToken"/> is
    /// cancelled at any await point, <see cref="OperationCanceledException"/> is re-thrown.
    /// </para>
    /// <para>
    /// LLM call timeout is handled internally via a linked <see cref="CancellationTokenSource"/>
    /// seeded with <c>McpConfig.ToolPlanningTimeoutSeconds</c>.  When the deadline fires,
    /// the timeout OCE is caught here and the method returns <see langword="null"/> — the
    /// caller's <paramref name="cancellationToken"/> is never cancelled.
    /// </para>
    /// <para>
    /// When <paramref name="context"/> is <see langword="null"/> or
    /// <see cref="PlannerContext.IsEmpty"/> the generated prompt is byte-identical to the
    /// Phase 8 baseline because <see cref="PlannerContext.ToPromptBlock"/> returns an empty
    /// string and the <c>{context}</c> placeholder is substituted with <c>""</c>.
    /// </para>
    /// </remarks>
    public async Task<ToolPlan?> GeneratePlanAsync(
        string userMessage,
        string toolDefinitionsForPrompt,
        PlannerContext? context,
        CancellationToken cancellationToken = default)
    {
        // [DIAG-P9] entry log
        _logger?.LogInformation(
            "[DIAG-P9] GeneratePlanAsync ENTRY enabled={Enabled} toolDefsLen={Len} contextNull={Ctx} contextEmpty={Empty} userMsgLen={UMsg}",
            _config.Mcp.ToolPlanningEnabled,
            toolDefinitionsForPrompt?.Length ?? 0,
            context is null,
            context?.IsEmpty ?? true,
            userMessage?.Length ?? 0);

        // Gate 1: feature disabled
        if (!_config.Mcp.ToolPlanningEnabled)
        {
            _logger?.LogInformation("[DIAG-P9] EXIT-NULL gate1 ToolPlanningEnabled=false");
            return null;
        }

        // Gate 2: no tools to plan with
        if (string.IsNullOrWhiteSpace(toolDefinitionsForPrompt))
        {
            _logger?.LogInformation("[DIAG-P9] EXIT-NULL gate2 empty toolDefinitionsForPrompt");
            return null;
        }

        // [DIAG-P9] log context summary if present
        if (context is not null && !context.IsEmpty)
        {
            _logger?.LogInformation(
                "[DIAG-P9] context Summary='{Summary}' RecentTurnsCount={Count} TotalBytes={Bytes}",
                context.Summary, context.RecentTurns.Count, context.TotalBytes);
        }

        // Alias for readability inside the existing body
        var ct = cancellationToken;

        try
        {
            // 1. Build planning prompt (context block is "" when null/empty — byte-identical baseline)
            var contextBlock = context?.ToPromptBlock() ?? string.Empty;
            var prompt = PlanningPromptTemplate
                .Replace("{toolDefinitionsForPrompt}", toolDefinitionsForPrompt)
                .Replace("{context}", contextBlock)
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

            // [DIAG-P9] log raw plan from LLM (truncated)
            var rawPreview = rawPlan.Length > 500 ? rawPlan.Substring(0, 500) + "...[truncated]" : rawPlan;
            _logger?.LogInformation(
                "[DIAG-P9] raw LLM plan ({Len} chars):\n----RAW----\n{Preview}\n----END----",
                rawPlan.Length, rawPreview);

            // 3. Parse steps from LLM output (capped at MaxSteps)
            var rawSteps = ParseSteps(rawPlan);
            if (rawSteps.Count == 0)
            {
                _logger?.LogWarning(
                    "[DIAG-P9] EXIT-NULL gate-parse no steps parsed from LLM output ({Len} chars). FULL OUTPUT:\n{Full}",
                    rawPlan.Length, rawPlan);
                return null;
            }

            // 4. Extract tool names + descriptions from the prompt-formatted string
            var tools = ExtractTools(toolDefinitionsForPrompt);
            _logger?.LogInformation(
                "[DIAG-P9] parsed {Steps} step(s); extracted {Tools} tool(s) from defs",
                rawSteps.Count, tools.Count);

            // 5. Match each step to a tool via deterministic fuzzy cascade,
            //    then (when configured) fall through to embedding-based semantic match.
            var matchedSteps = new List<ToolPlanStep>(rawSteps.Count);
            var embeddingFallbackEnabled =
                _embeddings is not null
                && _config.Mcp.ToolPlannerEmbeddingFallbackEnabled;

            foreach (var step in rawSteps)
            {
                // AC-C3: cancellation checkpoint at the top of each step iteration
                ct.ThrowIfCancellationRequested();
                var matched = MatchStepFuzzy(step, tools);

                // Layer 2 (Sprint 10 follow-up): semantic fallback when lexical fails.
                // Only fires when the lexical matcher returned null AND the service is
                // available AND the gate is on — otherwise behaviour is byte-equivalent.
                if (matched.MatchedToolName is null && embeddingFallbackEnabled)
                {
                    matched = await MatchStepWithEmbeddingsAsync(step, tools, ct).ConfigureAwait(false);
                }

                _logger?.LogInformation(
                    "[DIAG-P9] step {N} desc='{Desc}' → matched={Match} similarity={Sim:F2}",
                    matched.StepNumber,
                    matched.Description.Length > 80 ? matched.Description.Substring(0, 80) + "…" : matched.Description,
                    matched.MatchedToolName ?? "<NULL>",
                    matched.Similarity);
                matchedSteps.Add(matched);
            }

            var unmatched = matchedSteps.Count(s => s.MatchedToolName is null);
            _logger?.LogInformation(
                "[DIAG-P9] PLAN OK: total={Total} matched={Matched} unmatched={Unmatched}",
                matchedSteps.Count, matchedSteps.Count - unmatched, unmatched);

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

    // ──────────────────────────────────────────────────────────────────────────
    // Layer 2 (Sprint 10 follow-up): Semantic / embedding-based fallback matcher.
    // Runs ONLY when MatchStepFuzzy returns null AND the embedding service is
    // available AND the gate is enabled. The lexical 3-tier matcher remains the
    // happy path (zero added cost when the LLM names the tool literally).
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the embedding of <paramref name="step"/>'s description and matches it
    /// against the cached tool embeddings. Returns the step with
    /// <see cref="ToolPlanStep.MatchedToolName"/> set when the best cosine similarity
    /// reaches <c>McpConfig.ToolPlannerEmbeddingMatchThreshold</c>; otherwise returns
    /// the step unchanged.
    /// </summary>
    /// <remarks>
    /// Cancellation propagates as <see cref="OperationCanceledException"/>. Any other
    /// exception is logged at Warning level and degrades gracefully to "no match".
    /// </remarks>
    private async Task<ToolPlanStep> MatchStepWithEmbeddingsAsync(
        ToolPlanStep step,
        IReadOnlyList<(string Name, string FullLine)> tools,
        CancellationToken ct)
    {
        try
        {
            // Step embedding — single call per ambiguous step.
            var stepVec = await _embeddings!
                .GenerateEmbeddingAsync(step.Description, ct)
                .ConfigureAwait(false);
            if (stepVec is null || stepVec.Length == 0)
                return step;

            // Tool embeddings — populated lazily on first use, cached for the lifetime
            // of this ToolPlanner instance (tools don't change in a session).
            await EnsureToolEmbeddingsAsync(tools, ct).ConfigureAwait(false);

            var threshold = _config.Mcp.ToolPlannerEmbeddingMatchThreshold;
            string? bestName = null;
            float bestSim = 0f;

            foreach (var (name, _) in tools)
            {
                if (!_toolEmbeddingCache.TryGetValue(name, out var toolVec))
                    continue;

                var sim = CosineSimilarity(stepVec, toolVec);
                if (sim >= threshold && sim > bestSim)
                {
                    bestSim = sim;
                    bestName = name;
                }
            }

            if (bestName is not null)
            {
                _logger?.LogInformation(
                    "[DIAG-P9] embedding fallback matched step {N} → {Tool} (sim={Sim:F2}, threshold={Thr:F2})",
                    step.StepNumber, bestName, bestSim, threshold);
                return step with { MatchedToolName = bestName, Similarity = bestSim };
            }

            return step;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "[DIAG-P9] embedding fallback failed for step {N}; degrading to no-match",
                step.StepNumber);
            return step;
        }
    }

    /// <summary>
    /// Populates <see cref="_toolEmbeddingCache"/> with embeddings for every tool whose
    /// name is not yet cached. Guarded by <see cref="_cacheLock"/>; idempotent — safe
    /// to call from concurrent plan invocations.
    /// </summary>
    private async Task EnsureToolEmbeddingsAsync(
        IReadOnlyList<(string Name, string FullLine)> tools,
        CancellationToken ct)
    {
        // Fast path: all tools already cached.
        var allCached = true;
        foreach (var (name, _) in tools)
        {
            if (!_toolEmbeddingCache.ContainsKey(name))
            {
                allCached = false;
                break;
            }
        }
        if (allCached) return;

        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (name, fullLine) in tools)
            {
                if (_toolEmbeddingCache.ContainsKey(name)) continue;

                ct.ThrowIfCancellationRequested();
                var vec = await _embeddings!
                    .GenerateEmbeddingAsync(fullLine, ct)
                    .ConfigureAwait(false);
                if (vec is not null && vec.Length > 0)
                    _toolEmbeddingCache[name] = vec;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Cosine similarity in [0, 1] between two vectors of equal length. Returns 0
    /// when either vector is zero-norm or lengths differ. Local copy of the helper
    /// in <c>Nexus.Memory.Graph.SemanticSearch</c> (avoids cross-layer cycle).
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0f || normB == 0f)
            return 0f;
        return dot / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
