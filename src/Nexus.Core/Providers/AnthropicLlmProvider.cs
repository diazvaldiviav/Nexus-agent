using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Core.Providers;

/// <summary>
/// LLM provider that communicates with Anthropic's Messages API.
/// Supports both synchronous and streaming (SSE with named events) endpoints.
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicLlmProvider>? _logger;

    public string ProviderName => "anthropic";

    public AnthropicLlmProvider(
        string apiKey,
        HttpClient? httpClient = null,
        string? endpoint = null,
        int maxTokens = 4096,
        ILogger<AnthropicLlmProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _endpoint = (endpoint ?? "https://api.anthropic.com").TrimEnd('/');
        _maxTokens = maxTokens;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/v1/messages";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory, model, _maxTokens, stream: false);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var httpResponse = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "No response from Anthropic.";
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse Anthropic response: {responseJson[..Math.Min(200, responseJson.Length)]}", ex);
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/v1/messages";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory, model, _maxTokens, stream: true);

        var json = JsonSerializer.Serialize(requestBody);
        var requestContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent };
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        using var httpResponse = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrEmpty(line))
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line.Substring("event: ".Length);
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (currentEvent == "message_stop")
                    yield break;

                if (currentEvent == "content_block_delta")
                {
                    var dataJson = line.Substring("data: ".Length);
                    var token = ParseSseChunk(dataJson);
                    if (token is not null)
                        yield return token;
                }
            }
        }
    }

    private static object BuildRequestBody(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        int maxTokens,
        bool stream)
    {
        var messages = new List<object>();

        foreach (var msg in conversationHistory)
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        return new
        {
            model,
            max_tokens = maxTokens,
            system = systemPrompt,
            messages,
            stream
        };
    }

    private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var statusCode = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var message = statusCode switch
        {
            401 => $"Anthropic API key is invalid. Verify your API key in nexus.yaml or ANTHROPIC_API_KEY environment variable. Status: {statusCode}",
            429 => $"Anthropic rate limit exceeded -- retry after delay. Status: {statusCode}",
            400 => $"Anthropic bad request -- check model name and parameters. Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}",
            _ => $"Anthropic API error. Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}"
        };

        _logger?.LogError("Anthropic API error: {Message}", message);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? ParseSseChunk(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "text_delta"
                && delta.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
