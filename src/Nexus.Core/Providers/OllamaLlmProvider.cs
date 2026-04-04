using System.Runtime.CompilerServices;
using System.Text.Json;
using Nexus.Core.Config;

using Nexus.Core.Abstractions;
using Nexus.Core.Models;

namespace Nexus.Core.Providers;

/// <summary>
/// LLM provider that communicates with a local Ollama instance via its HTTP API.
/// Extracted from AgentService to support the ILlmProvider abstraction.
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _endpoint;

    public string ProviderName => "ollama";

    public OllamaLlmProvider(ModelProviderConfig config, HttpClient? httpClient = null)
    {
        _endpoint = (config.Endpoint ?? "http://localhost:11434").TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/api/chat";

        var request = new
        {
            model,
            messages = BuildMessages(systemPrompt, conversationHistory),
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpResponse = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "No response from model.";
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"{_endpoint}/api/chat";

        var request = new
        {
            model,
            messages = BuildMessages(systemPrompt, conversationHistory),
            stream = true
        };

        var json = JsonSerializer.Serialize(request);
        var requestContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = requestContent
        };

        using var httpResponse = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrEmpty(line)) continue;

            var (token, done) = ParseStreamLine(line);
            if (token is not null)
                yield return token;
            if (done)
                yield break;
        }
    }

    private static List<object> BuildMessages(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversationHistory)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in conversationHistory)
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        return messages;
    }

    private static (string? token, bool done) ParseStreamLine(string line)
    {
        try
        {
            var doc = JsonDocument.Parse(line);
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
        catch (JsonException)
        {
            return (null, false);
        }
    }
}
