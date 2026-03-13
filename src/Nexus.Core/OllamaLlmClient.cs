using System.Text;
using System.Text.Json;
using Nexus.Core.Config;
using Nexus.Memory;

namespace Nexus.Core;

public class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly ModelProviderConfig _config;

    public OllamaLlmClient(ModelProviderConfig config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var endpoint = (_config.Endpoint ?? "http://localhost:11434").TrimEnd('/');
        var url = $"{endpoint}/api/chat";

        var request = new
        {
            model = _config.Model,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
