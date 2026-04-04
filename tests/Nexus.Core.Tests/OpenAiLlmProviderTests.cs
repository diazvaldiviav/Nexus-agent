using System.Net;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Tests.Fakes;
using Xunit;

namespace Nexus.Core.Tests;

public class OpenAiLlmProviderTests
{
    private static readonly ConversationMessage[] TestHistory = new[]
    {
        new ConversationMessage { Role = "user", Content = "Hello" }
    };

    [Fact]
    public async Task ChatAsync_ReturnsContent_WhenOpenAiResponds()
    {
        // Arrange
        var responseJson = """{"choices":[{"message":{"content":"Hello from GPT"}}]}""";
        var handler = new TestHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var provider = new OpenAiLlmProvider("test-api-key", httpClient);

        // Act
        var result = await provider.ChatAsync("You are helpful.", TestHistory, "gpt-4o-mini");

        // Assert
        Assert.Equal("Hello from GPT", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer test-api-key", handler.LastRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task ChatStreamAsync_YieldsTokens_WhenOpenAiStreams()
    {
        // Arrange: SSE with data: prefix lines
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                  "data: {\"choices\":[{\"delta\":{\"content\":\" GPT\"}}]}\n\n" +
                  "data: [DONE]\n\n";
        var handler = new TestHttpMessageHandler(sse);
        var httpClient = new HttpClient(handler);

        var provider = new OpenAiLlmProvider("test-api-key", httpClient);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.ChatStreamAsync("You are helpful.", TestHistory, "gpt-4o-mini"))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello GPT", string.Join("", tokens));
    }

    [Fact]
    public async Task ChatAsync_ThrowsOnUnauthorized_WhenApiKeyInvalid()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            """{"error":{"message":"Incorrect API key provided"}}""", HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);

        var provider = new OpenAiLlmProvider("bad-key", httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.ChatAsync("You are helpful.", TestHistory, "gpt-4o-mini"));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
