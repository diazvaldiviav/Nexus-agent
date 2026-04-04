using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Nexus.Memory.Abstractions;

namespace Nexus.Memory.Embedding;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public OpenAiEmbeddingService(EmbeddingOptions options, string apiKey, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is required. Set it in nexus.yaml (embeddings.api_key) " +
                "or via OPENAI_API_KEY environment variable.");
        }
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        var endpoint = _options.Endpoint.TrimEnd('/');
        var requestUrl = $"{endpoint}/v1/embeddings";
        var payload = new { model = _options.Model, input = text };

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"OpenAI API not available at {endpoint}. Verify your network connection and endpoint.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "OpenAI request timed out after 30 seconds.", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "OpenAI API key is invalid. Check your API key in nexus.yaml (embeddings.api_key) " +
                "or OPENAI_API_KEY environment variable.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "OpenAI rate limit exceeded. Please retry later.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (result?.Data is null || result.Data.Count == 0 || result.Data[0].Embedding is null)
        {
            throw new InvalidOperationException("Invalid response from OpenAI embedding API.");
        }

        return result.Data[0].Embedding!;
    }

    private sealed class OpenAiEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
