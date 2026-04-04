using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Core.Providers;

/// <summary>
/// LLM provider that communicates with Google's Gemini REST API.
/// Supports both synchronous (generateContent) and streaming (streamGenerateContent) endpoints.
/// </summary>
public class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly ILogger<GeminiLlmProvider>? _logger;

    public string ProviderName => "gemini";

    public GeminiLlmProvider(
        string apiKey,
        HttpClient? httpClient = null,
        string? endpoint = null,
        ILogger<GeminiLlmProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _endpoint = (endpoint ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/models/{model}:generateContent?key={_apiKey}";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpResponse = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "No response from Gemini.";
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/models/{model}:streamGenerateContent?alt=sse&key={_apiKey}";
        var requestBody = BuildRequestBody(systemPrompt, conversationHistory);

        var json = JsonSerializer.Serialize(requestBody);
        var requestContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = requestContent
        };

        using var httpResponse = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ThrowOnErrorAsync(httpResponse, cancellationToken).ConfigureAwait(false);

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrEmpty(line)) continue;

            // SSE format: lines starting with "data: " contain JSON chunks
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var dataJson = line.Substring("data: ".Length);
            var token = ParseSseChunk(dataJson);
            if (token is not null)
                yield return token;
        }
    }

    private static object BuildRequestBody(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory)
    {
        var contents = new List<object>();

        foreach (var msg in conversationHistory)
        {
            contents.Add(new
            {
                role = MapRole(msg.Role),
                parts = new[] { new { text = msg.Content } }
            });
        }

        return new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents
        };
    }

    private static string MapRole(string role) =>
        role == "assistant" ? "model" : role;

    private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var statusCode = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var message = statusCode switch
        {
            401 => $"Gemini API key is invalid. Verify your API key in nexus.yaml or GEMINI_API_KEY environment variable. Status: {statusCode}",
            403 => $"Gemini API key forbidden -- check API restrictions in Google Cloud Console. Status: {statusCode}",
            429 => $"Gemini rate limit exceeded -- retry after delay. Status: {statusCode}",
            _ => $"Gemini API error. Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}"
        };

        _logger?.LogError("Gemini API error: {Message}", message);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? ParseSseChunk(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
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
