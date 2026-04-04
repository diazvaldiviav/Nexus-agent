using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Nexus.Memory.Abstractions;

namespace Nexus.Memory.Embedding;

public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly string _model;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public GeminiEmbeddingService(string apiKey, string model = "text-embedding-004", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Google API key is required for Gemini embeddings. " +
                "Set it in nexus.yaml (models.cloud.api_key) or via GOOGLE_API_KEY environment variable.");
        }
        _apiKey = apiKey;
        _model = model;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent?key={_apiKey}";
        var payload = new
        {
            content = new { parts = new[] { new { text } } }
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Gemini embedding API not available. Verify your network connection.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Gemini embedding request timed out after 30 seconds.", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Google API key is invalid. Check your API key.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "Gemini rate limit exceeded. Please retry later.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (result?.Embedding?.Values is null || result.Embedding.Values.Length == 0)
        {
            throw new InvalidOperationException("Invalid response from Gemini embedding API.");
        }

        return result.Embedding.Values;
    }

    private sealed class GeminiEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public EmbeddingValues? Embedding { get; set; }
    }

    private sealed class EmbeddingValues
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
