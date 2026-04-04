using System.Net;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Tests.Fakes;
using Xunit;

namespace Nexus.Core.Tests;

public class AnthropicLlmProviderTests
{
    private static readonly ConversationMessage[] TestHistory = new[]
    {
        new ConversationMessage { Role = "user", Content = "Hello" }
    };

    [Fact]
    public async Task ChatAsync_ReturnsContent_WhenAnthropicResponds()
    {
        // Arrange
        var responseJson = """{"content":[{"type":"text","text":"Hello from Claude"}],"stop_reason":"end_turn"}""";
        var handler = new TestHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var provider = new AnthropicLlmProvider("test-api-key", httpClient);

        // Act
        var result = await provider.ChatAsync("You are helpful.", TestHistory, "claude-sonnet-4-6");

        // Assert
        Assert.Equal("Hello from Claude", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/v1/messages", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("test-api-key", handler.LastRequest.Headers.GetValues("x-api-key").First());
        Assert.Equal("2023-06-01", handler.LastRequest.Headers.GetValues("anthropic-version").First());
    }

    [Fact]
    public async Task ChatStreamAsync_YieldsTokens_WhenAnthropicStreams()
    {
        // Arrange: SSE with named events
        var sse = "event: content_block_delta\n" +
                  "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n\n" +
                  "event: content_block_delta\n" +
                  "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\" Claude\"}}\n\n" +
                  "event: message_stop\n" +
                  "data: {}\n\n";
        var handler = new TestHttpMessageHandler(sse);
        var httpClient = new HttpClient(handler);

        var provider = new AnthropicLlmProvider("test-api-key", httpClient);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.ChatStreamAsync("You are helpful.", TestHistory, "claude-sonnet-4-6"))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello Claude", string.Join("", tokens));
    }

    [Fact]
    public async Task ChatAsync_ThrowsOnUnauthorized_WhenApiKeyInvalid()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            """{"error":{"message":"Invalid API key"}}""", HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);

        var provider = new AnthropicLlmProvider("bad-key", httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.ChatAsync("You are helpful.", TestHistory, "claude-sonnet-4-6"));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
