using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Core.Providers;

/// <summary>
/// LLM provider that communicates with OpenAI's Chat Completions API.
/// Supports both synchronous and streaming (SSE) endpoints.
/// </summary>
public class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly ILogger<OpenAiLlmProvider>? _logger;

    public string ProviderName => "openai";

    public OpenAiLlmProvider(
        string apiKey,
        HttpClient? httpClient = null,
        string? endpoint = null,
        ILogger<OpenAiLlmProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _endpoint = (endpoint ?? "https://api.openai.com").TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/v1/chat/completions";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory, model, stream: false);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var httpResponse = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "No response from OpenAI.";
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse OpenAI response: {responseJson[..Math.Min(200, responseJson.Length)]}", ex);
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/v1/chat/completions";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory, model, stream: true);

        var json = JsonSerializer.Serialize(requestBody);
        var requestContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        using var httpResponse = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrEmpty(line)) continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line.Substring("data: ".Length);

            if (data == "[DONE]")
                yield break;

            var token = ParseSseChunk(data);
            if (token is not null)
                yield return token;
        }
    }

    private static object BuildRequestBody(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        bool stream)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in conversationHistory)
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        return new
        {
            model,
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
            401 => $"OpenAI API key is invalid. Verify your API key in nexus.yaml or OPENAI_API_KEY environment variable. Status: {statusCode}",
            429 => $"OpenAI rate limit exceeded -- retry after delay. Status: {statusCode}",
            _ => $"OpenAI API error. Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}"
        };

        _logger?.LogError("OpenAI API error: {Message}", message);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? ParseSseChunk(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var contentEl))
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
