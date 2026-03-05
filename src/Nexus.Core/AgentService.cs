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
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger?.LogInformation("Processing user message: {Message}", userMessage[..Math.Min(100, userMessage.Length)]);

        _conversationHistory.Add(new ConversationMessage { Role = "user", Content = userMessage });

        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(userMessage);

        var useCloud = _modelRouter.IsCloud(TaskType.MemoryQueryResponse);
        var modelConfig = useCloud ? _config.Models.Cloud : _config.Models.Local;

        var response = await CallLlmAsync(systemPrompt, userMessage, modelConfig, cancellationToken);

        _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response });

        var extractedEntities = await _entityExtractor.ExtractAndPersistAsync(
            $"User: {userMessage}\nAssistant: {response}");

        sw.Stop();
        await _graph.LogActionAsync(new AgentAction
        {
            ActionType = "chat",
            Detail = userMessage[..Math.Min(200, userMessage.Length)],
            ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
            DurationMs = (int)sw.ElapsedMilliseconds
        });

        return new AgentResponse
        {
            Content = response,
            ModelUsed = $"{modelConfig.Provider}/{modelConfig.Model}",
            DurationMs = (int)sw.ElapsedMilliseconds,
            ExtractedEntities = extractedEntities
        };
    }

    public void ClearHistory() => _conversationHistory.Clear();

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
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
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
}
