using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Memory;

public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly HttpClient _http;

    public OllamaEmbeddingService(EmbeddingOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        var endpoint = _options.Endpoint.TrimEnd('/');
        var requestUrl = $"{endpoint}/api/embeddings";
        var payload = new { model = _options.Model, prompt = text };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(requestUrl, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Ollama not available at {endpoint}. Ensure Ollama is running.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Ollama request timed out after 30 seconds.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Embedding model '{_options.Model}' not found. Run: ollama pull {_options.Model}");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (result?.Embedding is null || result.Embedding.Length == 0)
        {
            throw new InvalidOperationException("Invalid response from Ollama embedding API.");
        }

        return result.Embedding;
    }

    private sealed class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
