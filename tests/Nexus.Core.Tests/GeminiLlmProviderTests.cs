using System.Net;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Tests.Fakes;
using Xunit;

namespace Nexus.Core.Tests;

public class GeminiLlmProviderTests
{
    private static readonly ConversationMessage[] TestHistory = new[]
    {
        new ConversationMessage { Role = "user", Content = "Hello" }
    };

    [Fact]
    public async Task ChatAsync_ReturnsContent_WhenGeminiResponds()
    {
        // Arrange
        var responseJson = """{"candidates":[{"content":{"parts":[{"text":"Hello from Gemini"}]}}]}""";
        var handler = new TestHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var provider = new GeminiLlmProvider("test-api-key", httpClient);

        // Act
        var result = await provider.ChatAsync("You are helpful.", TestHistory, "gemini-2.5-flash-lite");

        // Assert
        Assert.Equal("Hello from Gemini", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("generateContent", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("key=test-api-key", handler.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task ChatStreamAsync_YieldsTokens_WhenGeminiStreams()
    {
        // Arrange: SSE format with data: prefix lines
        var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hello\"}]}}]}\n\n" +
                  "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\" Gemini\"}]}}]}\n\n";
        var handler = new TestHttpMessageHandler(sse);
        var httpClient = new HttpClient(handler);

        var provider = new GeminiLlmProvider("test-api-key", httpClient);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.ChatStreamAsync("You are helpful.", TestHistory, "gemini-2.5-flash-lite"))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello Gemini", string.Join("", tokens));
    }

    [Fact]
    public async Task ChatAsync_ThrowsOnUnauthorized_WhenApiKeyInvalid()
    {
        // Arrange
        var handler = new TestHttpMessageHandler("""{"error":{"message":"Invalid API key"}}""", HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);

        var provider = new GeminiLlmProvider("bad-key", httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.ChatAsync("You are helpful.", TestHistory, "gemini-2.5-flash-lite"));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatAsync_ThrowsOnRateLimit_When429Returned()
    {
        // Arrange
        var handler = new TestHttpMessageHandler("""{"error":{"message":"Rate limit exceeded"}}""", HttpStatusCode.TooManyRequests);
        var httpClient = new HttpClient(handler);

        var provider = new GeminiLlmProvider("test-api-key", httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.ChatAsync("You are helpful.", TestHistory, "gemini-2.5-flash-lite"));
        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
