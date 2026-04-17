using System.Text.Json;
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

    public AgentService(
        NexusConfig config,
        IKnowledgeGraph graph,
        PromptBuilder promptBuilder,
        ModelRouter modelRouter,
        EntityExtractor entityExtractor,
        LlmProviderFactory providerFactory,
        IInteractionSummarizer summarizer,
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

        // Background extraction — tracked so it completes before next message or exit
        var conversationText = $"User: {userMessage}\nAssistant: {response}";
        var extractionPrompt = _promptBuilder.BuildEntityExtractionPrompt(conversationText);
        var currentTurn = _turnCount;
        var historySnapshot = _conversationHistory.ToList();
        var extractionTask = Task.Run(async () =>
        {
            try
            {
                var extracted = await _entityExtractor.ExtractAndPersistAsync(
                    conversationText, extractionPrompt);
                agentResponse.ExtractedEntities = extracted;

                await _graph.LogActionAsync(new AgentAction
                {
                    ActionType = "chat",
                    Detail = userMessage[..Math.Min(200, userMessage.Length)],
                    ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
                    TokensIn = (systemPrompt.Length + userMessage.Length) / 4,
                    TokensOut = response.Length / 4,
                    DurationMs = (int)sw.ElapsedMilliseconds
                });
                _logger?.LogInformation("Entity extraction completed: {Count} entities", extracted.Count);

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

        // Background extraction — tracked so it completes before next message or exit
        var conversationText = $"User: {userMessage}\nAssistant: {response}";
        var extractionPrompt = _promptBuilder.BuildEntityExtractionPrompt(conversationText);
        var currentTurn = _turnCount;
        var historySnapshot = _conversationHistory.ToList();
        var extractionTask = Task.Run(async () =>
        {
            try
            {
                var extracted = await _entityExtractor.ExtractAndPersistAsync(conversationText, extractionPrompt);
                await _graph.LogActionAsync(new AgentAction
                {
                    ActionType = "chat",
                    Detail = userMessage[..Math.Min(200, userMessage.Length)],
                    ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
                    TokensIn = (systemPrompt.Length + userMessage.Length) / 4,
                    TokensOut = response.Length / 4,
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
                _logger?.LogInformation("[PathValidator] Tool '{Tool}' corrected: {Note}", toolCall.Name, outcome.ErrorMessage);
                Console.Error.WriteLine($"[PathValidator] Corrected: {outcome.ErrorMessage}");
            }

            effectiveArguments = outcome.CorrectedArguments;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.Mcp.ToolCallTimeoutSeconds));

        try
        {
            return await _toolExecutor.InvokeToolAsync("", toolCall.Name, effectiveArguments, cts.Token).ConfigureAwait(false);
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
