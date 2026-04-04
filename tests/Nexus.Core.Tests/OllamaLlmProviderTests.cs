using System.Net;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Config;
using Nexus.Core.Tests.Fakes;
using Xunit;

namespace Nexus.Core.Tests;

public class OllamaLlmProviderTests
{
    private static readonly ConversationMessage[] TestHistory = new[]
    {
        new ConversationMessage { Role = "user", Content = "Hello" }
    };

    [Fact]
    public async Task ChatAsync_ReturnsContent_WhenOllamaResponds()
    {
        // Arrange
        var responseJson = """{"message":{"role":"assistant","content":"Hello"},"done":true}""";
        var handler = new TestHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var config = new ModelProviderConfig { Endpoint = "http://localhost:11434" };
        var provider = new OllamaLlmProvider(config, httpClient);

        // Act
        var result = await provider.ChatAsync("You are helpful.", TestHistory, "qwen3:14b");

        // Assert
        Assert.Equal("Hello", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/api/chat", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ChatStreamAsync_YieldsTokens_WhenOllamaStreams()
    {
        // Arrange
        var ndjson = """
            {"message":{"content":"Hello"},"done":false}
            {"message":{"content":" world"},"done":false}
            {"message":{"content":""},"done":true}
            """;
        var handler = new TestHttpMessageHandler(ndjson);
        var httpClient = new HttpClient(handler);

        var config = new ModelProviderConfig { Endpoint = "http://localhost:11434" };
        var provider = new OllamaLlmProvider(config, httpClient);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.ChatStreamAsync("You are helpful.", TestHistory, "qwen3:14b"))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello world", string.Join("", tokens));
    }

}
