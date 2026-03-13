using Microsoft.Extensions.Logging;
using Nexus.Core.Config;
using Nexus.Memory;
using Nexus.Memory.Models;

namespace Nexus.Core;

public class ConversationMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AgentResponse
{
    public string Content { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public int DurationMs { get; set; }
    public List<Entity> ExtractedEntities { get; set; } = new();
}

public class AgentService
{
    private readonly NexusConfig _config;
    private readonly KnowledgeGraph _graph;
    private readonly PromptBuilder _promptBuilder;
    private readonly ModelRouter _modelRouter;
    private readonly EntityExtractor _entityExtractor;
    private readonly ILogger<AgentService>? _logger;
    private static readonly System.Net.Http.HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };
    private readonly List<ConversationMessage> _conversationHistory = new();
    private Task? _pendingExtraction;
    private readonly object _extractionLock = new();

    public AgentService(
        NexusConfig config,
        KnowledgeGraph graph,
        PromptBuilder promptBuilder,
        ModelRouter modelRouter,
        EntityExtractor entityExtractor,
        ILogger<AgentService>? logger = null)
    {
        _config = config;
        _graph = graph;
        _promptBuilder = promptBuilder;
        _modelRouter = modelRouter;
        _entityExtractor = entityExtractor;
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

        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(userMessage, cancellationToken);

        var useCloud = _modelRouter.IsCloud(TaskType.MemoryQueryResponse);
        var modelConfig = useCloud ? _config.Models.Cloud : _config.Models.Local;

        var response = await CallLlmAsync(systemPrompt, userMessage, modelConfig, cancellationToken);

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });

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
                    DurationMs = (int)sw.ElapsedMilliseconds
                });
                _logger?.LogInformation("Entity extraction completed: {Count} entities", extracted.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background entity extraction failed.");
            }
        });
        TrackExtraction(extractionTask);

        return agentResponse;
    }

    public void ClearHistory() => _conversationHistory.Clear();

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

        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(userMessage, cancellationToken);

        var useCloud = _modelRouter.IsCloud(TaskType.MemoryQueryResponse);
        var modelConfig = useCloud ? _config.Models.Cloud : _config.Models.Local;

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var token in StreamOllamaAsync(systemPrompt, userMessage, modelConfig, cancellationToken))
        {
            fullResponse.Append(token);
            yield return token;
        }

        var response = fullResponse.ToString();
        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });

        sw.Stop();

        // Background extraction — tracked so it completes before next message or exit
        var conversationText = $"User: {userMessage}\nAssistant: {response}";
        var extractionPrompt = _promptBuilder.BuildEntityExtractionPrompt(conversationText);
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
                    DurationMs = (int)sw.ElapsedMilliseconds
                });
                _logger?.LogInformation("Entity extraction completed: {Count} entities", extracted.Count);
                onEntitiesExtracted?.Invoke(extracted.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background entity extraction failed.");
            }
        });
        TrackExtraction(extractionTask);
    }

    private async Task<string> CallLlmAsync(
        string systemPrompt,
        string userMessage,
        ModelProviderConfig modelConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            if (modelConfig.Provider == "ollama")
                return await CallOllamaAsync(systemPrompt, userMessage, modelConfig, cancellationToken);

            _logger?.LogWarning("Cloud provider {Provider} not yet implemented, falling back to local", modelConfig.Provider);
            return await CallOllamaAsync(systemPrompt, userMessage, _config.Models.Local, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LLM call failed");
            return $"I encountered an error while processing your request. Please ensure Ollama is running with model {modelConfig.Model}.";
        }
    }

    private List<object> BuildOllamaMessages(string systemPrompt, string userMessage)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in _conversationHistory.SkipLast(1))
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        messages.Add(new { role = "user", content = userMessage });
        return messages;
    }

    private async Task<string> CallOllamaAsync(
        string systemPrompt,
        string userMessage,
        ModelProviderConfig modelConfig,
        CancellationToken cancellationToken)
    {
        var endpoint = modelConfig.Endpoint ?? "http://localhost:11434";
        var url = $"{endpoint}/api/chat";

        var request = new
        {
            model = modelConfig.Model,
            messages = BuildOllamaMessages(systemPrompt, userMessage),
            stream = false
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpResponse = await _httpClient.PostAsync(url, content, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        var doc = System.Text.Json.JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "No response from model.";
    }

    private async IAsyncEnumerable<string> StreamOllamaAsync(
        string systemPrompt,
        string userMessage,
        ModelProviderConfig modelConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoint = modelConfig.Endpoint ?? "http://localhost:11434";
        var url = $"{endpoint}/api/chat";

        var request = new
        {
            model = modelConfig.Model,
            messages = BuildOllamaMessages(systemPrompt, userMessage),
            stream = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
        {
            Content = content
        };

        using var httpResponse = await _httpClient.SendAsync(
            httpRequest, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            var (token, done) = ParseStreamLine(line);
            if (token is not null)
                yield return token;
            if (done)
                yield break;
        }
    }

    private static (string? token, bool done) ParseStreamLine(string line)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(line);
            var done = doc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean();

            string? token = null;
            if (doc.RootElement.TryGetProperty("message", out var msgEl)
                && msgEl.TryGetProperty("content", out var contentEl))
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                    token = text;
            }

            return (token, done);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, false);
        }
    }
}
