using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;
using Nexus.Memory.Models;
using Nexus.Memory.Processing;

namespace Nexus.Core.Services;

public class AgentService : IAgentService
{
    private readonly NexusConfig _config;
    private readonly IKnowledgeGraph _graph;
    private readonly PromptBuilder _promptBuilder;
    private readonly ModelRouter _modelRouter;
    private readonly EntityExtractor _entityExtractor;
    private readonly LlmProviderFactory _providerFactory;
    private readonly IInteractionSummarizer _summarizer;
    private readonly IToolPlanner? _toolPlanner;
    private readonly IPlannerContextBuilder? _plannerContextBuilder;
    private readonly IToolVerifier? _toolVerifier;
    private readonly IPermissionGate? _permissionGate;
    private readonly IVerificationCatalog? _verificationCatalog;
    private readonly OutputFidelityVerifier? _outputFidelityVerifier;
    private readonly IToolExecutor? _toolExecutor;
    private readonly IToolArgumentValidator? _argumentValidator;
    private readonly ISchemaValidator? _schemaValidator;
    private readonly EntityResolver? _entityResolver;
    private readonly MemoryCompressor? _compressor;
    private readonly ContextWindowManager? _contextWindowManager;
    private readonly ILogger<AgentService>? _logger;
    private readonly List<ConversationMessage> _conversationHistory = new();
    private Task? _pendingExtraction;
    private readonly object _extractionLock = new();
    private int _turnCount;

    // Plan-trail window for background entity extraction.
    // PlanTrailExtractionWindow = messages per step (user instruction + LLM reply + tool result = 3).
    // PlanTrailHeaderSize = fixed overhead (original user message + plan header + final summary + slop = 4).
    private const int PlanTrailExtractionWindow = 3;
    private const int PlanTrailHeaderSize = 4;

    // ChatOnly tier detection (cross-layer constraint: must stay in sync with
    // Nexus.Connectors.ToolFiltering.ToolCapabilityResolver thresholds).
    // Models below 4B parameters cannot reliably emit valid [TOOL_CALL: {...}] JSON,
    // so the planner is short-circuited and the legacy chat loop runs without tools.
    private const double ChatOnlyParamThreshold = 4.0;
    private static readonly Regex ModelParamRegex = new(
        @"(\d+(?:\.\d+)?)\s*b(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsChatOnlyModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        var match = ModelParamRegex.Match(modelName);
        if (!match.Success) return false;
        if (!double.TryParse(
                match.Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var b))
            return false;
        return b < ChatOnlyParamThreshold;
    }

    private static string BuildStepPrompt(int attempt, ToolPlanStep step, JsonElement? schema)
    {
        var tool = step.MatchedToolName!;  // caller guards null

        if (attempt == 1)
            return $"[PLANNER] Execute ONLY this step: use {tool} to {step.Description}. " +
                   $"Call the tool now with [TOOL_CALL: ...]";

        if (attempt == 2)
        {
            if (schema is null)
            {
                // Fall back to attempt-1 body
                return $"[PLANNER] Execute ONLY this step: use {tool} to {step.Description}. " +
                       $"Call the tool now with [TOOL_CALL: ...]";
            }

            var requiredText = DescribeRequired(schema.Value);
            var argsTemplate = BuildArgsTemplate(schema.Value);

            return
                $"[PLANNER] Execute step {step.StepNumber} using the `{tool}` tool.\n\n" +
                $"{step.Description}\n\n" +
                $"Required arguments: {requiredText}\n\n" +
                $"Respond with EXACTLY this line (fill in <placeholders>, no other text):\n" +
                $"[TOOL_CALL: {{\"name\": \"{tool}\", \"arguments\": {argsTemplate}}}]";
        }

        // attempt >= 3: hard coercion (verbatim from AC-4)
        return
            "[PLANNER] Your previous response was prose. This is wrong.\n\n" +
            "Output ONLY the tool call line. No prose. No explanations. No markdown. Just:\n" +
            $"[TOOL_CALL: {{\"name\": \"{tool}\", \"arguments\": {{...}}}}]";
    }

    /// <summary>
    /// Emits a comma-separated list like "path (string), offset (integer)" derived from
    /// the schema's required[] array crossed with properties[name].type. Returns "(none)"
    /// if schema lacks required or properties, or if required is empty.
    /// </summary>
    private static string DescribeRequired(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return "(none)";

        if (!schema.TryGetProperty("required", out var req) || req.ValueKind != JsonValueKind.Array)
            return "(none)";

        schema.TryGetProperty("properties", out var props);
        var hasProps = props.ValueKind == JsonValueKind.Object;

        var parts = new List<string>();
        foreach (var name in req.EnumerateArray())
        {
            var paramName = name.GetString();
            if (string.IsNullOrEmpty(paramName)) continue;

            string typeName = "any";
            if (hasProps && props.TryGetProperty(paramName, out var propDef)
                && propDef.ValueKind == JsonValueKind.Object
                && propDef.TryGetProperty("type", out var t)
                && t.ValueKind == JsonValueKind.String)
            {
                typeName = t.GetString() ?? "any";
            }
            parts.Add($"{paramName} ({typeName})");
        }

        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }

    /// <summary>
    /// Builds a concrete arguments template from a tool's input schema.
    /// Behavior contract:
    /// - schema null → not called (caller falls back to attempt-1 prompt body).
    /// - root not <see cref="JsonValueKind.Object"/> → returns "{...}".
    /// - "required" missing or not <see cref="JsonValueKind.Array"/> → returns "{...}".
    /// - empty "required" array → returns "{...}".
    /// - non-empty "required" → emits {"name1": "&lt;name1&gt;", "name2": &lt;name2&gt;}
    ///   where string-typed fields are quoted placeholders and other types are unquoted.
    /// </summary>
    private static string BuildArgsTemplate(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return "{...}";
        if (!schema.TryGetProperty("required", out var req) || req.ValueKind != JsonValueKind.Array)
            return "{...}";

        schema.TryGetProperty("properties", out var props);
        var hasProps = props.ValueKind == JsonValueKind.Object;

        var parts = new List<string>();
        foreach (var name in req.EnumerateArray())
        {
            var paramName = name.GetString();
            if (string.IsNullOrEmpty(paramName)) continue;

            string typeName = "any";
            if (hasProps && props.TryGetProperty(paramName, out var propDef)
                && propDef.ValueKind == JsonValueKind.Object
                && propDef.TryGetProperty("type", out var t)
                && t.ValueKind == JsonValueKind.String)
            {
                typeName = t.GetString() ?? "any";
            }

            var placeholder = typeName switch
            {
                "string" => $"\"<{paramName}>\"",
                _        => $"<{paramName}>"
            };
            parts.Add($"\"{paramName}\": {placeholder}");
        }

        return parts.Count == 0 ? "{...}" : "{" + string.Join(", ", parts) + "}";
    }

    public AgentService(
        NexusConfig config,
        IKnowledgeGraph graph,
        PromptBuilder promptBuilder,
        ModelRouter modelRouter,
        EntityExtractor entityExtractor,
        LlmProviderFactory providerFactory,
        IInteractionSummarizer summarizer,
        IToolPlanner? toolPlanner = null,
        IPlannerContextBuilder? plannerContextBuilder = null,
        IToolVerifier? toolVerifier = null,
        IPermissionGate? permissionGate = null,
        IVerificationCatalog? verificationCatalog = null,
        OutputFidelityVerifier? outputFidelityVerifier = null,
        IToolExecutor? toolExecutor = null,
        IToolArgumentValidator? argumentValidator = null,
        ISchemaValidator? schemaValidator = null,
        EntityResolver? entityResolver = null,
        MemoryCompressor? compressor = null,
        ContextWindowManager? contextWindowManager = null,
        ILogger<AgentService>? logger = null)
    {
        _config = config;
        _graph = graph;
        _promptBuilder = promptBuilder;
        _modelRouter = modelRouter;
        _entityExtractor = entityExtractor;
        _providerFactory = providerFactory;
        _summarizer = summarizer;
        _toolPlanner = toolPlanner;
        _plannerContextBuilder = plannerContextBuilder;
        _toolVerifier = toolVerifier;
        _permissionGate = permissionGate;
        _verificationCatalog = verificationCatalog;
        _outputFidelityVerifier = outputFidelityVerifier;
        _toolExecutor = toolExecutor;
        _argumentValidator = argumentValidator;
        _schemaValidator = schemaValidator;
        _entityResolver = entityResolver;
        _compressor = compressor;
        _contextWindowManager = contextWindowManager;
        _logger = logger;
    }

    public IReadOnlyList<ConversationMessage> ConversationHistory => _conversationHistory.AsReadOnly();

    public async Task<AgentResponse> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        // Ensure previous extraction completes before processing new message
        await FlushPendingExtractionAsync().ConfigureAwait(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger?.LogInformation("Processing user message: {Message}", userMessage[..Math.Min(100, userMessage.Length)]);

        _conversationHistory.Add(new ConversationMessage { Role = "user", Content = userMessage });

        var useCloud = _modelRouter.IsCloud(TaskType.MemoryQueryResponse);
        var modelConfig = useCloud ? _config.Models.Cloud : _config.Models.Local;

        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(userMessage, modelConfig.Model, cancellationToken);
        _logger?.LogDebug("System prompt ({Length} chars), tools available: {HasTools}",
            systemPrompt.Length, _toolExecutor?.HasTools ?? false);

        // Thread safety: _conversationHistory is mutated in-place by CompactIfNeededAsync.
        // This is safe because background extraction uses historySnapshot (a copy via ToList()),
        // and FlushPendingExtractionAsync is awaited at the start of each ChatAsync call.
        if (_contextWindowManager is not null)
            await _contextWindowManager.CompactIfNeededAsync(
                systemPrompt, _conversationHistory, modelConfig, cancellationToken)
                .ConfigureAwait(false);

        // ChatOnly tier short-circuit: models <4B can't reliably emit tool-call JSON,
        // so we skip the planner entirely and let the legacy chat loop run with no tools.
        var isChatOnly = IsChatOnlyModel(modelConfig.Model);
        if (isChatOnly)
        {
            _logger?.LogInformation(
                "[ChatOnly] Skipping planner + tool execution for model '{Model}' (< 4B params)",
                modelConfig.Model);
        }

        // Plan-then-execute path (opt-in; falls through to normal loop on null plan)
        // [DIAG-P9] log entry preconditions
        _logger?.LogInformation(
            "[DIAG-P9] ChatAsync plan-gate: plannerNotNull={P} toolExecNotNull={T} HasTools={H} plannerCtxBuilderNotNull={B} PlannerCtxEnabled={E} HistoryCount={Hist}",
            _toolPlanner is not null,
            _toolExecutor is not null,
            _toolExecutor?.HasTools ?? false,
            _plannerContextBuilder is not null,
            _config.Mcp.PlannerContextEnabled,
            _conversationHistory.Count);

        if (!isChatOnly && _toolPlanner is not null && _toolExecutor is not null && _toolExecutor.HasTools)
        {
            var heuristicAllow = true;
            if (_config.Mcp.PlannerHeuristicEnabled)
            {
                var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(userMessage, _config);
                _logger?.LogInformation("[Planner] heuristic: shouldPlan={ShouldPlan} reason={Reason}", shouldPlan, reason);
                heuristicAllow = shouldPlan;
            }

            if (heuristicAllow)
            {
                var toolDefs = _toolExecutor.GetToolDefinitionsForPrompt(modelConfig.Model) ?? string.Empty;

                PlannerContext? plannerContext = null;
                if (_config.Mcp.PlannerContextEnabled && _plannerContextBuilder is not null)
                {
                    plannerContext = await _plannerContextBuilder
                        .BuildAsync(_conversationHistory, userMessage, cancellationToken)
                        .ConfigureAwait(false);

                    // [DIAG-P9] log built context
                    _logger?.LogInformation(
                        "[DIAG-P9] PlannerContext built: IsEmpty={Empty} Summary='{Summary}' RecentTurns={Count}",
                        plannerContext?.IsEmpty ?? true,
                        plannerContext?.Summary ?? "<null>",
                        plannerContext?.RecentTurns.Count ?? 0);
                }

                var plan = await _toolPlanner.GeneratePlanAsync(
                        userMessage, toolDefs, plannerContext, cancellationToken)
                    .ConfigureAwait(false);

                // [DIAG-P9] log gate decision
                _logger?.LogInformation(
                    "[DIAG-P9] ChatAsync plan result: planNull={N} stepsCount={S} → {Action}",
                    plan is null,
                    plan?.Steps.Count ?? -1,
                    (plan is not null && plan.Steps.Count > 0) ? "ENTER plan-execute" : "FALL THROUGH to legacy loop");

                if (plan is not null && plan.Steps.Count > 0)
                {
                    _logger?.LogInformation("ToolPlanner returned {Count} steps — entering plan-execute path", plan.Steps.Count);
                    return await ExecutePlanAsync(plan, userMessage, modelConfig, sw, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        var response = await CallLlmAsync(systemPrompt, modelConfig, cancellationToken);

        // Tool call loop: detect tool calls, execute, feed result back to LLM
        var maxIterations = _config.Mcp.MaxToolCallIterations;
        string? previousToolSignature = null;
        for (int i = 0; i < maxIterations; i++)
        {
            if (_toolExecutor is null || !_toolExecutor.HasTools)
                break;

            var toolCall = ToolCallParser.TryParse(response);
            if (toolCall is null)
                break;

            _logger?.LogInformation("Tool call detected: {Name} (iteration {Iteration})", toolCall.Name, i + 1);

            var toolResult = await ExecuteToolWithTimeoutAsync(toolCall, cancellationToken).ConfigureAwait(false);

            var truncated = OutputTruncator.Truncate(toolResult, _config.Mcp.MaxOutputLines, _config.Mcp.MaxOutputBytes);
            if (truncated.WasTruncated)
                _logger?.LogInformation("Tool output truncated: {OriginalLines} lines / {OriginalBytes} bytes → {TruncatedLength} chars",
                    truncated.OriginalLines, truncated.OriginalBytes, truncated.Content.Length);
            toolResult = truncated.Content;

            var signature = BuildToolSignature(toolCall);
            if (previousToolSignature is not null && signature == previousToolSignature)
            {
                _logger?.LogWarning("Doom loop detected: tool '{ToolName}' called with identical arguments twice consecutively", toolCall.Name);
                _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[DoomLoop] You have called '{toolCall.Name}' with identical arguments twice consecutively. Do NOT call this tool again. Provide your best answer with the information you have."
                });
                response = await CallLlmAsync(systemPrompt, modelConfig, cancellationToken);
                break;
            }
            previousToolSignature = signature;

            _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
            _conversationHistory.Add(new ConversationMessage { Role = "user", Content = $"[Tool Result for {toolCall.Name}]:\n{toolResult}" });

            if (_contextWindowManager is not null)
                await _contextWindowManager.CompactIfNeededAsync(
                    systemPrompt, _conversationHistory, modelConfig, cancellationToken)
                    .ConfigureAwait(false);

            response = await CallLlmAsync(systemPrompt, modelConfig, cancellationToken);
        }

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
        _turnCount++;

        sw.Stop();

        var agentResponse = new AgentResponse
        {
            Content = response,
            ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        var conversationText = $"User: {userMessage}\nAssistant: {response}";
        RunBackgroundExtraction(conversationText, userMessage, response, modelConfig, sw, systemPrompt, agentResponse);

        return agentResponse;
    }

    public async Task ClearHistoryAsync()
    {
        if (_conversationHistory.Count > 0)
        {
            try
            {
                var convText = string.Join("\n", _conversationHistory.Select(m => $"{m.Role}: {m.Content}"));
                var summaryPrompt = _promptBuilder.BuildInteractionSummaryPrompt(convText);
                await _summarizer.SummarizeAsync(convText, summaryPrompt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Summarization failed during ClearHistoryAsync");
            }
        }
        _conversationHistory.Clear();
        _turnCount = 0;
    }

    /// <summary>
    /// Waits for any in-progress background entity extraction to complete.
    /// Call this before exiting the application to avoid losing extracted entities.
    /// </summary>
    public async Task FlushPendingExtractionAsync()
    {
        Task? pending;
        lock (_extractionLock)
        {
            pending = _pendingExtraction;
        }
        if (pending is not null)
        {
            await pending.ConfigureAwait(false);
        }
    }

    private void TrackExtraction(Task extractionTask)
    {
        lock (_extractionLock)
        {
            _pendingExtraction = extractionTask;
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string userMessage,
        Action<int>? onEntitiesExtracted = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Ensure previous extraction completes before processing new message
        await FlushPendingExtractionAsync().ConfigureAwait(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _conversationHistory.Add(new ConversationMessage { Role = "user", Content = userMessage });

        var useCloud = _modelRouter.IsCloud(TaskType.MemoryQueryResponse);
        var modelConfig = useCloud ? _config.Models.Cloud : _config.Models.Local;

        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(userMessage, modelConfig.Model, cancellationToken);
        _logger?.LogDebug("Stream system prompt ({Length} chars), tools available: {HasTools}",
            systemPrompt.Length, _toolExecutor?.HasTools ?? false);

        if (_contextWindowManager is not null)
            await _contextWindowManager.CompactIfNeededAsync(
                systemPrompt, _conversationHistory, modelConfig, cancellationToken)
                .ConfigureAwait(false);

        // ChatOnly tier short-circuit: models <4B can't reliably emit tool-call JSON,
        // so we skip the planner entirely and let the legacy stream loop run with no tools.
        var isChatOnlyStream = IsChatOnlyModel(modelConfig.Model);
        if (isChatOnlyStream)
        {
            _logger?.LogInformation(
                "[ChatOnly] Skipping planner + tool execution for model '{Model}' (< 4B params)",
                modelConfig.Model);
        }

        // Plan-then-execute path (opt-in; falls through to normal streaming loop on null plan)
        // [DIAG-P9] log entry preconditions
        _logger?.LogInformation(
            "[DIAG-P9] ChatStreamAsync plan-gate: plannerNotNull={P} toolExecNotNull={T} HasTools={H} plannerCtxBuilderNotNull={B} PlannerCtxEnabled={E} HistoryCount={Hist}",
            _toolPlanner is not null,
            _toolExecutor is not null,
            _toolExecutor?.HasTools ?? false,
            _plannerContextBuilder is not null,
            _config.Mcp.PlannerContextEnabled,
            _conversationHistory.Count);

        if (!isChatOnlyStream && _toolPlanner is not null && _toolExecutor is not null && _toolExecutor.HasTools)
        {
            var heuristicAllow = true;
            if (_config.Mcp.PlannerHeuristicEnabled)
            {
                var (shouldPlan, reason) = PlannerInvocationHeuristic.ShouldInvokePlanner(userMessage, _config);
                _logger?.LogInformation("[Planner] heuristic: shouldPlan={ShouldPlan} reason={Reason}", shouldPlan, reason);
                heuristicAllow = shouldPlan;
            }

            if (heuristicAllow)
            {
                var toolDefs = _toolExecutor.GetToolDefinitionsForPrompt(modelConfig.Model) ?? string.Empty;

                PlannerContext? plannerContext = null;
                if (_config.Mcp.PlannerContextEnabled && _plannerContextBuilder is not null)
                {
                    plannerContext = await _plannerContextBuilder
                        .BuildAsync(_conversationHistory, userMessage, cancellationToken)
                        .ConfigureAwait(false);

                    // [DIAG-P9] log built context
                    _logger?.LogInformation(
                        "[DIAG-P9] PlannerContext built: IsEmpty={Empty} Summary='{Summary}' RecentTurns={Count}",
                        plannerContext?.IsEmpty ?? true,
                        plannerContext?.Summary ?? "<null>",
                        plannerContext?.RecentTurns.Count ?? 0);
                }

                var plan = await _toolPlanner.GeneratePlanAsync(
                        userMessage, toolDefs, plannerContext, cancellationToken)
                    .ConfigureAwait(false);

                // [DIAG-P9] log gate decision
                _logger?.LogInformation(
                    "[DIAG-P9] ChatStreamAsync plan result: planNull={N} stepsCount={S} → {Action}",
                    plan is null,
                    plan?.Steps.Count ?? -1,
                    (plan is not null && plan.Steps.Count > 0) ? "ENTER plan-execute stream" : "FALL THROUGH to legacy stream loop");

                if (plan is not null && plan.Steps.Count > 0)
                {
                    _logger?.LogInformation("ToolPlanner returned {Count} steps — entering plan-execute stream path", plan.Steps.Count);
                    await foreach (var tok in ExecutePlanStreamAsync(plan, userMessage, modelConfig, onEntitiesExtracted, cancellationToken))
                        yield return tok;
                    yield break;
                }
            }
        }

        var fullResponse = new System.Text.StringBuilder();

        var provider = _providerFactory.GetRequiredProvider(modelConfig.Provider);
        await foreach (var token in provider.ChatStreamAsync(
            systemPrompt, _conversationHistory, modelConfig.Model, cancellationToken))
        {
            fullResponse.Append(token);
            yield return token;
        }

        var response = fullResponse.ToString();

        // Tool call loop for streaming: detect tool calls, execute, re-stream follow-up
        var maxIterations = _config.Mcp.MaxToolCallIterations;
        string? previousToolSignature = null;
        for (int i = 0; i < maxIterations; i++)
        {
            if (_toolExecutor is null || !_toolExecutor.HasTools)
                break;

            var toolCall = ToolCallParser.TryParse(response);
            if (toolCall is null)
            {
                if (response.Contains("[TOOL_CALL:"))
                {
                    // Dump first 600 bytes as hex to capture invisible/control characters
                    var bytes = System.Text.Encoding.UTF8.GetBytes(response);
                    var hexLen = Math.Min(600, bytes.Length);
                    var hex = string.Join(" ", bytes.Take(hexLen).Select(b => b.ToString("X2")));
                    _logger?.LogWarning("TryParse null. Len={Length}, Hex({HexLen}b): {Hex}", response.Length, hexLen, hex);
                }
                break;
            }

            _logger?.LogInformation("Tool call detected in stream: {Name} (iteration {Iteration})", toolCall.Name, i + 1);

            yield return $"\n[Executing tool: {toolCall.Name}...]\n";

            var toolResult = await ExecuteToolWithTimeoutAsync(toolCall, cancellationToken).ConfigureAwait(false);

            var truncated = OutputTruncator.Truncate(toolResult, _config.Mcp.MaxOutputLines, _config.Mcp.MaxOutputBytes);
            if (truncated.WasTruncated)
                _logger?.LogInformation("Tool output truncated: {OriginalLines} lines / {OriginalBytes} bytes → {TruncatedLength} chars",
                    truncated.OriginalLines, truncated.OriginalBytes, truncated.Content.Length);
            toolResult = truncated.Content;

            var signature = BuildToolSignature(toolCall);
            if (previousToolSignature is not null && signature == previousToolSignature)
            {
                _logger?.LogWarning("Doom loop detected: tool '{ToolName}' called with identical arguments twice consecutively", toolCall.Name);
                _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[DoomLoop] You have called '{toolCall.Name}' with identical arguments twice consecutively. Do NOT call this tool again. Provide your best answer with the information you have."
                });
                fullResponse.Clear();
                await foreach (var lastChanceToken in provider.ChatStreamAsync(
                    systemPrompt, _conversationHistory, modelConfig.Model, cancellationToken))
                {
                    fullResponse.Append(lastChanceToken);
                    yield return lastChanceToken;
                }
                response = fullResponse.ToString();
                break;
            }
            previousToolSignature = signature;

            _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
            _conversationHistory.Add(new ConversationMessage { Role = "user", Content = $"[Tool Result for {toolCall.Name}]:\n{toolResult}" });

            if (_contextWindowManager is not null)
                await _contextWindowManager.CompactIfNeededAsync(
                    systemPrompt, _conversationHistory, modelConfig, cancellationToken)
                    .ConfigureAwait(false);

            // Re-stream the follow-up LLM call
            fullResponse.Clear();
            await foreach (var followUpToken in provider.ChatStreamAsync(
                systemPrompt, _conversationHistory, modelConfig.Model, cancellationToken))
            {
                fullResponse.Append(followUpToken);
                yield return followUpToken;
            }

            response = fullResponse.ToString();
        }

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });
        _turnCount++;

        sw.Stop();

        var conversationText = $"User: {userMessage}\nAssistant: {response}";
        RunBackgroundExtraction(conversationText, userMessage, response, modelConfig, sw, systemPrompt, agentResponse: null, onEntitiesExtracted: onEntitiesExtracted);
    }

    /// <summary>
    /// Executes a tool plan step-by-step, reusing existing validation, timeout, and truncation logic.
    /// Returns an AgentResponse with the final summary as content. Also fires background extraction
    /// including the full plan trail (Risk #8 mitigation).
    /// </summary>
    /// <remarks>
    /// Invariants enforced:
    /// - Falls through to return a minimal response when the plan is null or empty (caller guards).
    /// - Per-step tool execution failures are caught and logged as Warning; the step appends a
    ///   "[Tool {name} failed: ...]" history marker and continues — the plan is never aborted by
    ///   a transient tool failure (AC-A1).
    /// - Synthetic [PLANNER] messages are filtered from conversationText before background extraction
    ///   so that entity extraction does not process internal orchestration noise (AC-A2).
    /// - The final-summary LLM call is wrapped in a linked CTS; on timeout a minimal response is
    ///   returned rather than propagating an OperationCanceledException (AC-A3).
    /// </remarks>
    private async Task<AgentResponse> ExecutePlanAsync(
        ToolPlan plan,
        string userMessage,
        ModelProviderConfig modelConfig,
        System.Diagnostics.Stopwatch sw,
        CancellationToken ct)
    {
        // 1. Alternate system prompt for plan-execution mode (AC-6)
        var systemPrompt = await _promptBuilder
            .BuildPlanExecutionSystemPromptAsync(userMessage, ct)
            .ConfigureAwait(false);

        // 2. Append plan header to history as a synthetic user message
        var header = new System.Text.StringBuilder();
        header.AppendLine("[Plan]");
        foreach (var s in plan.Steps)
        {
            var toolHint = s.MatchedToolName is not null
                ? $" (tool: {s.MatchedToolName})"
                : " (no tool matched)";
            header.AppendLine($"Step {s.StepNumber}: {s.Description}{toolHint}");
        }
        header.AppendLine("Execute each step in order. Output only one tool call per turn.");
        _conversationHistory.Add(new ConversationMessage { Role = "user", Content = header.ToString() });

        // 3. Per-step execution
        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            if (step.MatchedToolName is null)
            {
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[PlanStep {step.StepNumber}] No tool matched; skipping."
                });
                continue;
            }

            // _toolExecutor is non-null here — guarded at ChatAsync entry gate (line 105/256).
            // Use null-forgiving to match the existing pattern throughout this method.
            var schema = _toolExecutor!.GetToolSchema(step.MatchedToolName);
            var maxAttempts = _config.Mcp.StepExecutionMaxAttempts;
            var attempt = 1;
            var stepCompleted = false;

            while (attempt <= maxAttempts && !stepCompleted)
            {
                ct.ThrowIfCancellationRequested();
                var stepInstruction = BuildStepPrompt(attempt, step, schema);
                _conversationHistory.Add(new ConversationMessage { Role = "user", Content = stepInstruction });
                _logger?.LogInformation("Plan step {n}/{total} attempt {attempt}/{max}: tool={tool}",
                    step.StepNumber, plan.Steps.Count, attempt, maxAttempts, step.MatchedToolName);

                var reply = await CallLlmAsync(systemPrompt, modelConfig, ct).ConfigureAwait(false);
                var toolCall = ToolCallParser.TryParse(reply);

                if (toolCall is null)
                {
                    // Diagnostic at Debug level: parser rejection of an attempt is rare in normal
                    // operation. Per-attempt markers ("[Planning Step N/M attempt A/B: ...]") at
                    // Information already signal exhaustion; the reply body is only useful when
                    // diagnosing why a step actually failed, so it's gated behind LogLevel.Debug.
                    _logger?.LogDebug(
                        "[PlanStep {N}] attempt {A} parser rejected reply (length={Len}, first 200 chars): {Reply}",
                        step.StepNumber, attempt, reply.Length,
                        reply.Length > 200 ? reply[..200] + "..." : reply);
                    _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                    attempt++;
                    continue;
                }

                if (!string.Equals(toolCall.Name, step.MatchedToolName, StringComparison.OrdinalIgnoreCase))
                    _logger?.LogWarning("PlanStep {N}: model called '{Actual}' instead of planned '{Planned}'",
                        step.StepNumber, toolCall.Name, step.MatchedToolName);

                string toolResult;
                try { toolResult = await ExecuteToolWithTimeoutAsync(toolCall, ct).ConfigureAwait(false); }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger?.LogWarning(ex, "Plan step {n} tool {tool} execution failed; continuing",
                        step.StepNumber, step.MatchedToolName);
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = $"[Tool {step.MatchedToolName} failed: {ex.Message}]"
                    });
                    stepCompleted = true;
                    break;
                }

                // ── AC-9: verification retry — counts against StepExecutionMaxAttempts budget ──
                if (toolResult.StartsWith(SyntheticMarkers.VerificationWarningMarker, StringComparison.Ordinal)
                    && _config.Mcp.ToolVerificationEnabled
                    && attempt < maxAttempts)
                {
                    var reason = ExtractVerificationReason(toolResult);
                    _logger?.LogWarning("Plan step {n} attempt {attempt}: verification failed; retrying",
                        step.StepNumber, attempt);
                    _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = $"[PlanStep {step.StepNumber}] Previous attempt unverified: {reason}. Retry with explicit content."
                    });
                    attempt++;
                    continue;
                }

                var truncated = OutputTruncator.Truncate(toolResult, _config.Mcp.MaxOutputLines, _config.Mcp.MaxOutputBytes);
                if (truncated.WasTruncated)
                    _logger?.LogInformation("Tool output truncated: {OriginalLines} lines / {OriginalBytes} bytes → {TruncatedLength} chars",
                        truncated.OriginalLines, truncated.OriginalBytes, truncated.Content.Length);
                toolResult = truncated.Content;

                _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[Tool result for step {step.StepNumber}]\n{toolResult}"
                });

                // Fix I: schema rejection retry. The schema validator returned an error string
                // instead of executing the tool. Counts against the per-step attempt budget
                // and lets SummaryFailureAnalyzer (Fix H) tally [SchemaValidationError] separately.
                if (toolResult.StartsWith("[SchemaValidationError]", StringComparison.Ordinal)
                    && attempt < maxAttempts)
                {
                    _logger?.LogInformation(
                        "Plan step {n} attempt {attempt}: schema rejected; retrying with corrective grounding",
                        step.StepNumber, attempt);
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = $"[PlanStep {step.StepNumber}] Previous tool call had wrong arguments. " +
                                  $"The schema validator returned: {toolResult}\n" +
                                  $"Re-emit the [TOOL_CALL: ...] line with the correct argument names and types."
                    });
                    attempt++;
                    continue;
                }

                stepCompleted = true;
            }

            if (!stepCompleted)
            {
                _logger?.LogError("PlanStep {n}: exceeded {max} attempts; moving on",
                    step.StepNumber, maxAttempts);
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[PlanStep {step.StepNumber}] Exceeded {maxAttempts} attempts; moving on."
                });
            }
        }

        // 4. Final summary LLM call (AC-A3: linked CTS guards against timeout; graceful fallback on timeout)
        var languageName = _config.Agent.Language;

        // AC-6: inject grounding message when failures were detected so the LLM cannot claim success.
        var findings = SummaryFailureAnalyzer.Analyze(_conversationHistory);
        if (findings.HasFailures)
        {
            var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);
            _conversationHistory.Add(new ConversationMessage
            {
                Role = "user",
                Content = grounding
            });
            _logger?.LogInformation(
                "[PlanResult] {V} verification, {R} retries, {T} tool errors, {P} permission, {D} doom, {S} skipped, {F} fidelity, {SR} schema",
                findings.VerificationWarnings, findings.RetriesExhausted, findings.ToolErrors,
                findings.PermissionDenials, findings.DoomLoops, findings.StepsSkippedNoToolMatch,
                findings.FidelityWarnings, findings.SchemaRejections);
        }

        _conversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = $"All steps complete. Summarize the results for the user in {languageName}."
        });

        string finalResponse;
        using (var summaryCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            summaryCts.CancelAfter(TimeSpan.FromSeconds(_config.Mcp.ToolPlanningTimeoutSeconds));
            try
            {
                finalResponse = await CallLlmAsync(systemPrompt, modelConfig, summaryCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger?.LogWarning("Plan final-summary LLM call timed out after {Timeout}s",
                    _config.Mcp.ToolPlanningTimeoutSeconds);
                sw.Stop();
                return new AgentResponse
                {
                    Content = "Plan complete (summary unavailable — LLM timed out).",
                    ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
                    DurationMs = (int)sw.ElapsedMilliseconds
                };
            }
        }

        // Honesty filter: if the LLM emitted a [TOOL_CALL: ...] literal as its final summary,
        // it confused "describe what happened" with "execute the action again". The tool was NOT
        // invoked (the parser only runs inside the per-step attempt loop), so presenting the
        // tool-call text to the user as if it were a result would be a lie. Replace the response
        // with an honest failure note before Layer 4 runs (Layer 4 would otherwise see the file
        // content embedded in the tool-call's `content` field and pass spuriously).
        if (finalResponse.Contains("[TOOL_CALL:", StringComparison.Ordinal))
        {
            _logger?.LogWarning(
                "[FinalSummary] LLM emitted [TOOL_CALL:] as text — replacing with honest failure note (response length: {Len})",
                finalResponse.Length);
            finalResponse =
                "I attempted this operation but was unable to invoke the tool through the MCP " +
                "protocol — my response contained a tool-call format as text rather than an actual " +
                "invocation. The action did NOT complete. No file was written, modified, or deleted. " +
                "You may retry, or rephrase the request.";
        }

        // AC-L4-2: Layer 4 output fidelity verification + bounded retry.
        // Runs BEFORE committing finalResponse to history so a corrected summary replaces the
        // original. Only fires when verifier is registered AND read tools were invoked AND summary ≥ 50 chars.
        if (_outputFidelityVerifier is not null && _config.Mcp.OutputFidelityVerificationEnabled)
        {
            var readResults = ExtractReadToolResults(plan);
            if (readResults.Count > 0 && finalResponse.Length >= 50)
            {
                var maxRetries = Math.Max(0, _config.Mcp.OutputFidelityMaxRetries);
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    FidelityResult? fidelityResult;
                    try
                    {
                        fidelityResult = await _outputFidelityVerifier
                            .VerifyAsync(readResults, finalResponse, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[FidelityVerifier] threw — skipping verification");
                        break;
                    }

                    if (fidelityResult is null)
                    {
                        _logger?.LogInformation("[FidelityVerifier] skipped (returned null — pre-flight guard or scoring exception)");
                        break;
                    }

                    _logger?.LogInformation(
                        "[FidelityVerifier] attempt {Attempt}/{Max} hybrid={Hybrid:F2} sub={Sub:F2} emb={Emb:F2} threshold={Threshold:F2} passed={Passed}",
                        attempt + 1, maxRetries + 1,
                        fidelityResult.HybridScore, fidelityResult.SubstringScore, fidelityResult.EmbeddingScore,
                        _config.Mcp.OutputFidelityMinScore, fidelityResult.Passed);

                    if (fidelityResult.Passed)
                        break;

                    if (attempt < maxRetries)
                    {
                        var grounding = BuildFidelityGroundingMessage(fidelityResult, readResults);
                        _conversationHistory.Add(new ConversationMessage
                        {
                            Role = "user",
                            Content = grounding
                        });
                        finalResponse = await CallLlmAsync(systemPrompt, modelConfig, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        _conversationHistory.Add(new ConversationMessage
                        {
                            Role = "user",
                            Content = $"{SyntheticMarkers.FidelityWarningPrefix}Final summary still diverges from tool results after {maxRetries} retries (score={fidelityResult.HybridScore:F2})."
                        });
                        finalResponse += "\n\n[FidelityWarning] This summary may contain content not present in the actual file. Verify against the source.";
                    }
                }
            }
        }

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = finalResponse });
        _turnCount++;

        sw.Stop();
        var agentResponse = new AgentResponse
        {
            Content = finalResponse,
            ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        // 5. Background extraction — pass full plan trail (Risk #8 mitigation); AC-E2 named constants
        var planTrail = string.Join("\n",
            _conversationHistory
                .Skip(Math.Max(0, _conversationHistory.Count - (plan.Steps.Count * PlanTrailExtractionWindow + PlanTrailHeaderSize)))
                .Select(m => $"{m.Role}: {m.Content}"));
        var conversationText = $"User: {userMessage}\n{planTrail}\nAssistant: {finalResponse}";
        RunBackgroundExtraction(conversationText, userMessage, finalResponse, modelConfig, sw, systemPrompt, agentResponse);

        return agentResponse;
    }

    /// <summary>
    /// Streaming variant of ExecutePlanAsync. Yields progress tokens per step and
    /// streams the final summary. Fires background extraction with the full plan trail.
    /// </summary>
    /// <remarks>
    /// Invariants enforced:
    /// - Falls through to yield nothing when the plan is null or empty (caller guards).
    /// - Per-step tool execution failures are caught and logged as Warning; the step appends a
    ///   "[Tool {name} failed: ...]" history marker and continues — the plan is never aborted by
    ///   a transient tool failure (AC-A1).
    /// - Synthetic [PLANNER] messages are filtered from conversationText before background extraction
    ///   so that entity extraction does not process internal orchestration noise (AC-A2).
    /// - The streaming summary is drained via manual IAsyncEnumerator; on mid-stream failure a
    ///   "[Summary unavailable: {TypeName}]" marker is yielded and the stream closes cleanly (AC-B2).
    /// </remarks>
    private async IAsyncEnumerable<string> ExecutePlanStreamAsync(
        ToolPlan plan,
        string userMessage,
        ModelProviderConfig modelConfig,
        Action<int>? onEntitiesExtracted,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Alternate system prompt for plan-execution mode
        var systemPrompt = await _promptBuilder
            .BuildPlanExecutionSystemPromptAsync(userMessage, ct)
            .ConfigureAwait(false);

        // 2. Append plan header to history
        var header = new System.Text.StringBuilder();
        header.AppendLine("[Plan]");
        foreach (var s in plan.Steps)
        {
            var toolHint = s.MatchedToolName is not null
                ? $" (tool: {s.MatchedToolName})"
                : " (no tool matched)";
            header.AppendLine($"Step {s.StepNumber}: {s.Description}{toolHint}");
        }
        header.AppendLine("Execute each step in order. Output only one tool call per turn.");
        _conversationHistory.Add(new ConversationMessage { Role = "user", Content = header.ToString() });

        var totalSteps = plan.Steps.Count;

        // Emit a single compact plan summary at the start so the user sees what will happen.
        // Per-step attempt markers are suppressed (each attempt is logged at Information level
        // instead) — the user only sees the plan once and then the natural assistant answer.
        var planSummary = new System.Text.StringBuilder();
        planSummary.AppendLine("**Plan:**");
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var s = plan.Steps[i];
            var toolName = s.MatchedToolName ?? "(no tool matched)";
            planSummary.AppendLine($"{i + 1}. `{toolName}` — {s.Description}");
        }
        planSummary.AppendLine();
        yield return planSummary.ToString();

        // 3. Per-step execution
        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (step.MatchedToolName is null)
            {
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[PlanStep {step.StepNumber}] No tool matched; skipping."
                });
                continue;
            }

            // _toolExecutor is non-null here — guarded at ChatStreamAsync entry gate (line 105/256).
            // Use null-forgiving to match the existing pattern throughout this method.
            var schema = _toolExecutor!.GetToolSchema(step.MatchedToolName);
            var maxAttempts = _config.Mcp.StepExecutionMaxAttempts;
            var attempt = 1;
            var stepCompleted = false;

            while (attempt <= maxAttempts && !stepCompleted)
            {
                ct.ThrowIfCancellationRequested();
                var stepInstruction = BuildStepPrompt(attempt, step, schema);
                _conversationHistory.Add(new ConversationMessage { Role = "user", Content = stepInstruction });
                _logger?.LogInformation("Plan step {n}/{total} attempt {attempt}/{max}: tool={tool}",
                    step.StepNumber, plan.Steps.Count, attempt, maxAttempts, step.MatchedToolName);

                var reply = await CallLlmAsync(systemPrompt, modelConfig, ct).ConfigureAwait(false);
                var toolCall = ToolCallParser.TryParse(reply);

                if (toolCall is null)
                {
                    // Diagnostic at Debug level: parser rejection of an attempt is rare in normal
                    // operation. Per-attempt markers ("[Planning Step N/M attempt A/B: ...]") at
                    // Information already signal exhaustion; the reply body is only useful when
                    // diagnosing why a step actually failed, so it's gated behind LogLevel.Debug.
                    _logger?.LogDebug(
                        "[PlanStep {N}] attempt {A} parser rejected reply (length={Len}, first 200 chars): {Reply}",
                        step.StepNumber, attempt, reply.Length,
                        reply.Length > 200 ? reply[..200] + "..." : reply);
                    _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                    attempt++;
                    continue;
                }

                if (!string.Equals(toolCall.Name, step.MatchedToolName, StringComparison.OrdinalIgnoreCase))
                    _logger?.LogWarning("PlanStep {N}: model called '{Actual}' instead of planned '{Planned}'",
                        step.StepNumber, toolCall.Name, step.MatchedToolName);

                string toolResult;
                try { toolResult = await ExecuteToolWithTimeoutAsync(toolCall, ct).ConfigureAwait(false); }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger?.LogWarning(ex, "Plan step {n} tool {tool} execution failed; continuing",
                        step.StepNumber, step.MatchedToolName);
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = $"[Tool {step.MatchedToolName} failed: {ex.Message}]"
                    });
                    stepCompleted = true;
                    break;
                }

                // ── AC-9: verification retry — counts against StepExecutionMaxAttempts budget ──
                if (toolResult.StartsWith(SyntheticMarkers.VerificationWarningMarker, StringComparison.Ordinal)
                    && _config.Mcp.ToolVerificationEnabled
                    && attempt < maxAttempts)
                {
                    var reason = ExtractVerificationReason(toolResult);
                    _logger?.LogWarning("Plan step {n} attempt {attempt}: verification failed; retrying",
                        step.StepNumber, attempt);
                    _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = $"[PlanStep {step.StepNumber}] Previous attempt unverified: {reason}. Retry with explicit content."
                    });
                    attempt++;
                    continue;
                }

                var truncated = OutputTruncator.Truncate(toolResult, _config.Mcp.MaxOutputLines, _config.Mcp.MaxOutputBytes);
                if (truncated.WasTruncated)
                    _logger?.LogInformation("Tool output truncated: {OriginalLines} lines / {OriginalBytes} bytes → {TruncatedLength} chars",
                        truncated.OriginalLines, truncated.OriginalBytes, truncated.Content.Length);
                toolResult = truncated.Content;

                _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = reply });
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[Tool result for step {step.StepNumber}]\n{toolResult}"
                });

                // Fix I: schema rejection retry. The schema validator returned an error string
                // instead of executing the tool. Counts against the per-step attempt budget
                // and lets SummaryFailureAnalyzer (Fix H) tally [SchemaValidationError] separately.
                if (toolResult.StartsWith("[SchemaValidationError]", StringComparison.Ordinal)
                    && attempt < maxAttempts)
                {
                    _logger?.LogInformation(
                        "Plan step {n} attempt {attempt}: schema rejected; retrying with corrective grounding",
                        step.StepNumber, attempt);
                    _conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = $"[PlanStep {step.StepNumber}] Previous tool call had wrong arguments. " +
                                  $"The schema validator returned: {toolResult}\n" +
                                  $"Re-emit the [TOOL_CALL: ...] line with the correct argument names and types."
                    });
                    attempt++;
                    continue;
                }

                stepCompleted = true;
            }

            if (!stepCompleted)
            {
                _logger?.LogError("PlanStep {n}: exceeded {max} attempts; moving on",
                    step.StepNumber, maxAttempts);
                _conversationHistory.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"[PlanStep {step.StepNumber}] Exceeded {maxAttempts} attempts; moving on."
                });
            }
        }

        // 4. Final summary — stream tokens via manual enumerator drain (AC-B2)
        // Using IAsyncEnumerator manually so we can yield a fallback marker inside the catch block.
        // yield return cannot appear inside a catch directly, so we split the try/catch around MoveNextAsync.
        var languageName = _config.Agent.Language;

        // AC-6: inject grounding message when failures were detected so the LLM cannot claim success.
        var findings = SummaryFailureAnalyzer.Analyze(_conversationHistory);
        if (findings.HasFailures)
        {
            var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);
            _conversationHistory.Add(new ConversationMessage
            {
                Role = "user",
                Content = grounding
            });
            _logger?.LogInformation(
                "[PlanResult] {V} verification, {R} retries, {T} tool errors, {P} permission, {D} doom, {S} skipped, {F} fidelity, {SR} schema",
                findings.VerificationWarnings, findings.RetriesExhausted, findings.ToolErrors,
                findings.PermissionDenials, findings.DoomLoops, findings.StepsSkippedNoToolMatch,
                findings.FidelityWarnings, findings.SchemaRejections);
        }

        _conversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = $"All steps complete. Summarize the results for the user in {languageName}."
        });

        var finalResponseBuilder = new System.Text.StringBuilder();
        var summaryProvider = _providerFactory.GetRequiredProvider(modelConfig.Provider);

        // AC-B2: manual enumerator drain so we can yield a fallback marker on mid-stream failure.
        // C# forbids yield return inside a catch block, so we use a pendingFallback sentinel that
        // is set in the catch and yielded after the try/catch exits.
        var streamEnumerator = summaryProvider.ChatStreamAsync(
            systemPrompt, _conversationHistory, modelConfig.Model, ct).GetAsyncEnumerator(ct);
        try
        {
            string? pendingFallback = null;
            while (pendingFallback is null)
            {
                bool hasNext;
                string? currentToken = null;
                try
                {
                    hasNext = await streamEnumerator.MoveNextAsync().ConfigureAwait(false);
                    if (hasNext)
                        currentToken = streamEnumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    // Caller cancellation — propagate; let finally dispose the enumerator
                    throw;
                }
                catch (Exception ex)
                {
                    // Mid-stream failure — set fallback, break inner loop; yielded below (AC-B2)
                    pendingFallback = $"[Summary unavailable: {ex.GetType().Name}]";
                    break;
                }

                if (!hasNext)
                    break;

                finalResponseBuilder.Append(currentToken);
                yield return currentToken!;
            }

            // Yield the fallback marker outside the catch block (C# constraint)
            if (pendingFallback is not null)
            {
                finalResponseBuilder.Append(pendingFallback);
                yield return pendingFallback;
            }
        }
        finally
        {
            await streamEnumerator.DisposeAsync().ConfigureAwait(false);
        }

        var finalResponse = finalResponseBuilder.ToString();

        // Honesty filter: if the LLM emitted a [TOOL_CALL: ...] literal as its final summary,
        // it confused "describe what happened" with "execute the action again". The tool was NOT
        // invoked (the parser only runs inside the per-step attempt loop), so presenting the
        // tool-call text to the user as if it were a result would be a lie. Replace the response
        // with an honest failure note before Layer 4 runs (Layer 4 would otherwise see the file
        // content embedded in the tool-call's `content` field and pass spuriously). The user has
        // already seen the streamed [TOOL_CALL:...] tokens, so we yield a correction notice.
        if (finalResponse.Contains("[TOOL_CALL:", StringComparison.Ordinal))
        {
            _logger?.LogWarning(
                "[FinalSummary] LLM emitted [TOOL_CALL:] as text — replacing with honest failure note (response length: {Len})",
                finalResponse.Length);
            const string honestFailure =
                "\n\n[CORRECTION] The previous output contained a tool-call format as text rather " +
                "than an actual invocation. The action did NOT complete. No file was written, " +
                "modified, or deleted. You may retry, or rephrase the request.";
            yield return honestFailure;
            finalResponse =
                "I attempted this operation but was unable to invoke the tool through the MCP " +
                "protocol — my response contained a tool-call format as text rather than an actual " +
                "invocation. The action did NOT complete. No file was written, modified, or deleted. " +
                "You may retry, or rephrase the request.";
        }

        // AC-L4-2 (streaming): Layer 4 output fidelity verification + bounded retry.
        // Per architecture decision #8: retry uses non-streaming CallLlmAsync; the corrected text
        // is emitted as a single non-streamed chunk after the original stream completes. This avoids
        // re-streaming complexity while preserving the honesty contract.
        if (_outputFidelityVerifier is not null && _config.Mcp.OutputFidelityVerificationEnabled)
        {
            var readResults = ExtractReadToolResults(plan);
            if (readResults.Count > 0 && finalResponse.Length >= 50)
            {
                var maxRetries = Math.Max(0, _config.Mcp.OutputFidelityMaxRetries);
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    FidelityResult? fidelityResult;
                    try
                    {
                        fidelityResult = await _outputFidelityVerifier
                            .VerifyAsync(readResults, finalResponse, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[FidelityVerifier] threw — skipping verification");
                        break;
                    }

                    if (fidelityResult is null)
                    {
                        _logger?.LogInformation("[FidelityVerifier] skipped (returned null — pre-flight guard or scoring exception)");
                        break;
                    }

                    _logger?.LogInformation(
                        "[FidelityVerifier] attempt {Attempt}/{Max} hybrid={Hybrid:F2} sub={Sub:F2} emb={Emb:F2} threshold={Threshold:F2} passed={Passed}",
                        attempt + 1, maxRetries + 1,
                        fidelityResult.HybridScore, fidelityResult.SubstringScore, fidelityResult.EmbeddingScore,
                        _config.Mcp.OutputFidelityMinScore, fidelityResult.Passed);

                    if (fidelityResult.Passed)
                        break;

                    if (attempt < maxRetries)
                    {
                        var grounding = BuildFidelityGroundingMessage(fidelityResult, readResults);
                        _conversationHistory.Add(new ConversationMessage
                        {
                            Role = "user",
                            Content = grounding
                        });
                        // Non-streaming re-summary call (architecture decision #8)
                        finalResponse = await CallLlmAsync(systemPrompt, modelConfig, ct).ConfigureAwait(false);
                        yield return finalResponse;
                    }
                    else
                    {
                        _conversationHistory.Add(new ConversationMessage
                        {
                            Role = "user",
                            Content = $"{SyntheticMarkers.FidelityWarningPrefix}Final summary still diverges from tool results after {maxRetries} retries (score={fidelityResult.HybridScore:F2})."
                        });
                        var warningSuffix = "\n\n[FidelityWarning] This summary may contain content not present in the actual file. Verify against the source.";
                        finalResponse += warningSuffix;
                        yield return warningSuffix;
                    }
                }
            }
        }

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = finalResponse });
        _turnCount++;

        sw.Stop();

        // 5. Background extraction — pass full plan trail (Risk #8 mitigation); AC-E2 named constants
        var planTrail = string.Join("\n",
            _conversationHistory
                .Skip(Math.Max(0, _conversationHistory.Count - (plan.Steps.Count * PlanTrailExtractionWindow + PlanTrailHeaderSize)))
                .Select(m => $"{m.Role}: {m.Content}"));
        var conversationText = $"User: {userMessage}\n{planTrail}\nAssistant: {finalResponse}";
        RunBackgroundExtraction(conversationText, userMessage, finalResponse, modelConfig, sw, systemPrompt, agentResponse: null, onEntitiesExtracted: onEntitiesExtracted);
    }

    /// <summary>
    /// Fires background entity extraction, action logging, deduplication, summarization,
    /// and archival. Shared by ChatAsync, ChatStreamAsync, and ExecutePlanAsync to avoid duplication (SRP).
    /// When <paramref name="agentResponse"/> is non-null, sets its <see cref="AgentResponse.ExtractedEntities"/>
    /// after extraction completes (only ChatAsync path — streaming callers pass null).
    /// </summary>
    /// <remarks>
    /// Invariants enforced:
    /// - The <paramref name="conversationText"/> parameter is accepted from callers for minimal-ripple
    ///   compatibility, but when the history snapshot contains [PLANNER]-prefixed messages the method
    ///   recomputes conversationText internally from a filtered snapshot to exclude synthetic orchestration
    ///   messages before passing to the entity extractor (AC-A2). The parameter name is preserved to
    ///   avoid cascading signature changes across callers.
    /// - All background failures (extraction, dedup, summarization, archival) are caught and logged
    ///   as Warning; they never surface to the caller.
    /// </remarks>
    private void RunBackgroundExtraction(
        string conversationText,
        string userMessage,
        string finalResponse,
        ModelProviderConfig modelConfig,
        System.Diagnostics.Stopwatch sw,
        string systemPrompt,
        AgentResponse? agentResponse,
        Action<int>? onEntitiesExtracted = null)
    {
        var currentTurn = _turnCount;
        var historySnapshot = _conversationHistory.ToList();

        // AC-A2: filter synthetic messages before building conversationText for extraction
        var filteredSnapshot = historySnapshot
            .Where(m => !SyntheticMarkers.IsSynthetic(m.Content))
            .ToList();
        if (filteredSnapshot.Count < historySnapshot.Count)
        {
            conversationText = string.Join("\n",
                filteredSnapshot.Select(m => $"{m.Role}: {m.Content}"));
        }

        var extractionPrompt = _promptBuilder.BuildEntityExtractionPrompt(conversationText);

        var extractionTask = Task.Run(async () =>
        {
            try
            {
                var extracted = await _entityExtractor.ExtractAndPersistAsync(
                    conversationText, extractionPrompt);

                if (agentResponse is not null)
                    agentResponse.ExtractedEntities = extracted;

                await _graph.LogActionAsync(new AgentAction
                {
                    ActionType = "chat",
                    Detail = userMessage[..Math.Min(200, userMessage.Length)],
                    ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
                    TokensIn = (systemPrompt.Length + userMessage.Length) / 4,
                    TokensOut = finalResponse.Length / 4,
                    DurationMs = (int)sw.ElapsedMilliseconds
                });
                _logger?.LogInformation("Entity extraction completed: {Count} entities", extracted.Count);
                onEntitiesExtracted?.Invoke(extracted.Count);

                if (_entityResolver is not null)
                {
                    try
                    {
                        await _entityResolver.FindAndMergeAsync(useLlmConfirmation: false);
                        _logger?.LogInformation("Background deduplication completed");
                    }
                    catch (Exception dedupEx)
                    {
                        _logger?.LogWarning(dedupEx, "Background deduplication failed");
                    }
                }

                if (currentTurn > 0 && currentTurn % _config.Memory.SummarizationInterval == 0)
                {
                    try
                    {
                        var convText = string.Join("\n", historySnapshot.Select(m => $"{m.Role}: {m.Content}"));
                        var summaryPrompt = _promptBuilder.BuildInteractionSummaryPrompt(convText);
                        var entityIds = extracted.Select(e => e.Id).ToList();
                        await _summarizer.SummarizeAsync(convText, summaryPrompt, entityIds);
                        _logger?.LogInformation("Interaction summarized at turn {Turn}", currentTurn);
                    }
                    catch (Exception sumEx)
                    {
                        _logger?.LogWarning(sumEx, "Background summarization failed at turn {Turn}", currentTurn);
                    }
                }

                if (_compressor is not null && _config.Memory.CompressionEnabled)
                {
                    try
                    {
                        await _compressor.ArchiveStaleEntitiesAsync();
                        _logger?.LogInformation("Background archival completed");
                    }
                    catch (Exception archiveEx)
                    {
                        _logger?.LogWarning(archiveEx, "Background archival failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background processing failed (extraction or dedup).");
            }
        });
        TrackExtraction(extractionTask);
    }

    private async Task<string> ExecuteToolWithTimeoutAsync(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (_toolExecutor is null)
            return "Error: No tool executor available.";

        // Schema validation: check required args, coerce types, strip unknown
        var effectiveArguments = toolCall.Arguments;
        if (_schemaValidator is not null && _config.Mcp.SchemaValidationEnabled)
        {
            var schemaResult = _schemaValidator.Validate(toolCall.Name, effectiveArguments);
            if (!schemaResult.IsValid)
            {
                var errorMsg = string.Join("; ", schemaResult.Errors);
                _logger?.LogWarning("[SchemaValidation] Tool '{Tool}' rejected: {Error}", toolCall.Name, errorMsg);
                return $"[SchemaValidationError] {errorMsg}";
            }
            effectiveArguments = schemaResult.CoercedArgs;
        }

        // Semantic validation: normalize paths, check existence, fuzzy-correct
        if (_argumentValidator is not null)
        {
            var outcome = await _argumentValidator.ValidateAsync(
                toolCall.Name, toolCall.Arguments, cancellationToken).ConfigureAwait(false);

            if (!outcome.IsValid)
            {
                _logger?.LogWarning("[PathValidator] Tool '{Tool}' rejected: {Error}", toolCall.Name, outcome.ErrorMessage);
                return $"[PathValidationError] {outcome.ErrorMessage}";
            }

            if (outcome.WasCorrected)
            {
                _logger?.LogDebug("[PathValidator] Tool '{Tool}' corrected: {Note}", toolCall.Name, outcome.ErrorMessage);
            }

            effectiveArguments = outcome.CorrectedArguments;
        }

        // Resolve the server name early — used by both the permission gate and the verifier.
        var serverName = _toolExecutor.GetToolServerName(toolCall.Name);

        // Permission gate: consult IPermissionGate for destructive tools or config-flagged tools.
        // Gate is only invoked when enabled and the tool is marked destructive (or config says "ask").
        // If the gate DENIES: return a PermissionDenied sentinel immediately (no tool execution, no snapshot).
        // If the gate THROWS (implementation bug): log Warning and fall through to allow — gate bugs must
        // not silently lock up the agent (§11.7 safety-by-default for unrelated impl bugs).
        if (_permissionGate is not null && _config.Permission.Enabled)
        {
            var catalogRule = _verificationCatalog?.GetRule(serverName, toolCall.Name);
            var requiresAsk = catalogRule?.Destructive == true || ResolveConfigAction(toolCall.Name) == "ask";
            if (requiresAsk)
            {
                var patterns = PermissionPatternExtractor.Extract(toolCall.Name, effectiveArguments, catalogRule);
                var request = new PermissionRequest(
                    serverName,
                    toolCall.Name,
                    effectiveArguments,
                    patterns,
                    catalogRule?.Destructive == true ? "destructive operation" : "config requires confirmation");
                try
                {
                    var gateResponse = await _permissionGate
                        .RequestAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    if (gateResponse.Decision is PermissionDecision.Deny or PermissionDecision.DenyWithFeedback)
                    {
                        var reason = !string.IsNullOrWhiteSpace(gateResponse.Feedback)
                            ? gateResponse.Feedback!
                            : "user denied";
                        _logger?.LogWarning("[PermissionGate] {Tool} denied: {Reason}", toolCall.Name, reason);
                        return $"{SyntheticMarkers.PermissionDeniedPrefix}{reason}";
                    }
                    // Allow / AllowForSession / AllowPersisted: gate impl persists state internally.
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "[PermissionGate] {Tool} gate threw — defaulting to Allow (safety-by-default for unrelated impl bugs)",
                        toolCall.Name);
                    // Fall through to tool execution — ALLOWS the tool.
                }
            }
        }

        // Pre-snapshot capture for SnapshotDiff verification.
        // Snapshot tool calls are wrapped internally in a linked CTS with VerificationSnapshotTimeoutSeconds.
        // OperationCanceledException is rethrown unconditionally; other exceptions are logged as Warning
        // and verification is skipped (not fatal — the tool execution still proceeds).
        IReadOnlyDictionary<string, object>? preSnapshot = null;
        if (_toolVerifier is not null && _config.Mcp.ToolVerificationEnabled)
        {
            try
            {
                preSnapshot = await _toolVerifier
                    .CapturePreSnapshotAsync(serverName, toolCall.Name, effectiveArguments, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Verifier] pre-snapshot for {Tool} failed; continuing", toolCall.Name);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.Mcp.ToolCallTimeoutSeconds));

        string rawResult;
        try
        {
            rawResult = await _toolExecutor.InvokeToolAsync("", toolCall.Name, effectiveArguments, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutSec = _config.Mcp.ToolCallTimeoutSeconds;
            _logger?.LogWarning("Tool '{ToolName}' timed out after {Timeout} seconds", toolCall.Name, timeoutSec);
            return $"Error: Tool '{toolCall.Name}' timed out after {timeoutSec} seconds.";
        }
        catch (KeyNotFoundException)
        {
            _logger?.LogWarning("Tool '{ToolName}' not found", toolCall.Name);
            var availableTools = _toolExecutor?.GetToolDefinitionsForPrompt();
            if (string.IsNullOrEmpty(availableTools))
                return $"[InvalidTool] Tool '{toolCall.Name}' is not registered. No tools are currently available — MCP server may be disconnected.";
            return $"[InvalidTool] Tool '{toolCall.Name}' was not found. Please use one of the available tools:\n{availableTools}";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Tool '{ToolName}' execution failed", toolCall.Name);
            return $"Error executing tool '{toolCall.Name}': {ex.Message}";
        }

        // ── AC-8: verify post-execution ──
        if (_toolVerifier is not null && _config.Mcp.ToolVerificationEnabled)
        {
            try
            {
                var outcome = await _toolVerifier
                    .VerifyAsync(serverName, toolCall.Name, effectiveArguments, preSnapshot, rawResult, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.RuleMatched && !outcome.IsVerified)
                {
                    _logger?.LogWarning("[Verifier] {Tool} unverified: {Reason}", toolCall.Name, outcome.Reason);
                    return $"{SyntheticMarkers.VerificationWarningPrefix}{outcome.Reason}\n{rawResult}";
                }
                if (outcome.RuleMatched && outcome.IsVerified)
                {
                    _logger?.LogInformation("[Verifier] {Tool} verified", toolCall.Name);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Verifier] verify post-call for {Tool} failed; continuing", toolCall.Name);
            }
        }

        return rawResult;
    }

    /// <summary>
    /// Extracts the {reason} portion from a "[VerificationWarning] {reason}\n{rawResult}" string.
    /// Returns "unverified" if the format is not recognized.
    /// </summary>
    private static string ExtractVerificationReason(string warningResult)
    {
        if (!warningResult.StartsWith(SyntheticMarkers.VerificationWarningPrefix, StringComparison.Ordinal)) return "unverified";
        var afterPrefix = warningResult.Substring(SyntheticMarkers.VerificationWarningPrefix.Length);
        var newlineIdx = afterPrefix.IndexOf('\n');
        return newlineIdx > 0 ? afterPrefix.Substring(0, newlineIdx) : afterPrefix;
    }

    /// <summary>
    /// Extracts the textual content of read-tool invocations from <see cref="_conversationHistory"/>
    /// that belong to steps in <paramref name="plan"/> whose <c>MatchedToolName</c> starts with
    /// <c>"read_"</c>. Looks for the well-known "[Tool result for step N]" envelope format.
    /// </summary>
    private List<string> ExtractReadToolResults(ToolPlan plan)
    {
        var results = new List<string>();

        var readSteps = plan.Steps
            .Where(s => s.MatchedToolName is not null
                     && s.MatchedToolName.StartsWith("read_", StringComparison.Ordinal))
            .Select(s => s.StepNumber)
            .ToHashSet();

        if (readSteps.Count == 0)
            return results;

        foreach (var msg in _conversationHistory)
        {
            if (string.IsNullOrEmpty(msg.Content))
                continue;

            if (!msg.Content.StartsWith("[Tool result for step ", StringComparison.Ordinal))
                continue;

            var bracketEnd = msg.Content.IndexOf(']');
            if (bracketEnd < 0)
                continue;

            var header = msg.Content[..(bracketEnd + 1)];
            var match = System.Text.RegularExpressions.Regex.Match(header, @"step (\d+)");
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, out var stepNum))
                continue;

            if (!readSteps.Contains(stepNum))
                continue;

            var newlineIdx = msg.Content.IndexOf('\n');
            var body = newlineIdx >= 0 ? msg.Content[(newlineIdx + 1)..] : "";
            if (!string.IsNullOrWhiteSpace(body))
                results.Add(body);
        }

        return results;
    }

    /// <summary>
    /// Builds a grounding message injected into conversation history when fidelity verification
    /// fails. Includes score breakdown, actual tool result(s), and instructions to re-summarize.
    /// </summary>
    private static string BuildFidelityGroundingMessage(
        FidelityResult result,
        IReadOnlyList<string> readResults)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{SyntheticMarkers.FidelityWarningPrefix}Your previous summary diverges from the actual tool results.");
        sb.AppendLine($"Hybrid fidelity score: {result.HybridScore:F2} (substring={result.SubstringScore:F2}, embedding={result.EmbeddingScore:F2}). Threshold: 0.30.");
        sb.AppendLine();
        sb.AppendLine("Actual tool result(s):");
        sb.AppendLine("---");
        var combined = string.Join("\n---\n", readResults);
        sb.AppendLine(combined.Length > 2000 ? combined[..2000] + "...[truncated]" : combined);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Re-summarize using ONLY the content above. Quote exact phrases when possible.");
        sb.AppendLine("Do NOT add facts inferred from prior conversation context.");
        sb.Append("If the file is short or empty, say so explicitly.");
        return sb.ToString();
    }

    /// <summary>
    /// Resolves the configured permission action ("allow", "ask", or "deny") for a tool from
    /// <see cref="PermissionConfig.Tools"/>. Returns null when no rule is configured for the tool
    /// or when the action value is not one of the three known values.
    /// </summary>
    private string? ResolveConfigAction(string toolName)
    {
        if (_config.Permission?.Tools is not null
            && _config.Permission.Tools.TryGetValue(toolName, out var rule)
            && !string.IsNullOrWhiteSpace(rule?.Action))
        {
            var action = rule.Action.ToLowerInvariant();
            return action is "allow" or "ask" or "deny" ? action : null;
        }
        return null;
    }

    private static string BuildToolSignature(ToolCallRequest toolCall)
    {
        var args = JsonSerializer.Serialize(toolCall.Arguments ?? new Dictionary<string, object>());
        var raw = $"{toolCall.Name}:{args}";
        return raw.Length > 200 ? raw[..200] : raw;
    }

    private async Task<string> CallLlmAsync(
        string systemPrompt,
        ModelProviderConfig modelConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = _providerFactory.GetRequiredProvider(modelConfig.Provider);
            return await provider.ChatAsync(
                systemPrompt, _conversationHistory, modelConfig.Model, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Primary provider {Provider} failed, attempting fallback", modelConfig.Provider);

            // Fallback: swap local <-> cloud
            var fallbackConfig = modelConfig == _config.Models.Local
                ? _config.Models.Cloud
                : _config.Models.Local;

            var fallback = _providerFactory.GetProvider(fallbackConfig.Provider);
            if (fallback is not null)
            {
                try
                {
                    return await fallback.ChatAsync(
                        systemPrompt, _conversationHistory, fallbackConfig.Model, cancellationToken);
                }
                catch (Exception fallbackEx)
                {
                    _logger?.LogError(fallbackEx, "Fallback provider {Provider} also failed", fallbackConfig.Provider);
                }
            }

            return $"I encountered an error while processing your request. " +
                   $"Both {modelConfig.Provider} and {fallbackConfig.Provider} are unavailable.";
        }
    }
}
