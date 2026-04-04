using Nexus.Memory.Embedding;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nexus.Memory.Tests;

public class OllamaEmbeddingServiceTests
{
    private static readonly EmbeddingOptions DefaultOptions = new(
        Endpoint: "http://localhost:11434",
        Model: "nomic-embed-text",
        Dimensions: 768);

    [Fact]
    public async Task GenerateEmbeddingAsync_ValidText_ReturnsCorrectDimensions()
    {
        // Arrange
        var embedding = new float[768];
        for (var i = 0; i < embedding.Length; i++)
            embedding[i] = i * 0.001f;

        var responseJson = JsonSerializer.Serialize(new { embedding });
        var handler = new MockHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new OllamaEmbeddingService(DefaultOptions, httpClient);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(768, result.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_OllamaDown_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new MockHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var service = new OllamaEmbeddingService(DefaultOptions, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("http://localhost:11434", ex.Message);
        Assert.Contains("Ensure Ollama is running", ex.Message);
        // Verify the original exception is preserved as InnerException per error handling contract
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ModelNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new MockHandler("{\"error\": \"model not found\"}", HttpStatusCode.NotFound);
        var httpClient = new HttpClient(handler);
        var service = new OllamaEmbeddingService(DefaultOptions, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("nomic-embed-text", ex.Message);
        Assert.Contains("ollama pull", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var handler = new MockHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new OllamaEmbeddingService(DefaultOptions, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateEmbeddingAsync(""));

        Assert.Equal("text", ex.ParamName);
        Assert.Contains("Text cannot be null or empty", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_CustomEndpoint_UsesConfiguredEndpoint()
    {
        // Arrange
        var customOptions = new EmbeddingOptions(
            Endpoint: "http://my-ollama:9999",
            Model: "custom-model",
            Dimensions: 384);

        var embedding = new float[384];
        var responseJson = JsonSerializer.Serialize(new { embedding });
        var handler = new MockHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new OllamaEmbeddingService(customOptions, httpClient);

        // Act
        var result = await service.GenerateEmbeddingAsync("test");

        // Assert
        Assert.Equal(384, result.Length);
        Assert.Contains("/api/embeddings", handler.LastRequestUri?.ToString());
        Assert.Contains("my-ollama:9999", handler.LastRequestUri?.ToString());
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string? _responseContent;
        private readonly HttpStatusCode _statusCode;
        private readonly Exception? _exception;

        public Uri? LastRequestUri { get; private set; }

        public MockHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        public MockHandler(Exception exception)
        {
            _exception = exception;
            _statusCode = HttpStatusCode.InternalServerError;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_responseContent!, Encoding.UTF8, "application/json")
            });
        }
    }
}
