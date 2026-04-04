using Nexus.Memory.Embedding;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nexus.Memory.Tests;

public class GeminiEmbeddingServiceTests
{
    private const string DefaultModel = "text-embedding-004";
    private const string TestApiKey = "test-gemini-api-key";

    [Fact]
    public async Task GenerateEmbeddingAsync_ValidText_ReturnsCorrectDimensions()
    {
        // Arrange
        var embedding = new float[768];
        for (var i = 0; i < embedding.Length; i++)
            embedding[i] = i * 0.001f;

        var responseJson = JsonSerializer.Serialize(new
        {
            embedding = new { values = embedding }
        });
        var handler = new MockHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(TestApiKey, DefaultModel, httpClient);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(768, result.Length);
        Assert.Equal(0.001f, result[1], precision: 5);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("embedContent", handler.LastRequestUri!.ToString());
        Assert.Contains(DefaultModel, handler.LastRequestUri.ToString());
        Assert.Contains($"key={TestApiKey}", handler.LastRequestUri.ToString());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var handler = new MockHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(TestApiKey, DefaultModel, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateEmbeddingAsync(""));

        Assert.Equal("text", ex.ParamName);
        Assert.Contains("Text cannot be null or empty", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_Unauthorized_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new MockHandler("{\"error\": {\"message\": \"Invalid API key\"}}", HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(TestApiKey, DefaultModel, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("API key is invalid", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RateLimited_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new MockHandler("{\"error\": {\"message\": \"Rate limit exceeded\"}}", HttpStatusCode.TooManyRequests);
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(TestApiKey, DefaultModel, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_Forbidden_ThrowsInvalidOperationWithApiKeyMessage()
    {
        // Arrange
        var handler = new MockHandler(
            """{"error": {"message": "Forbidden"}}""", HttpStatusCode.Forbidden);
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(TestApiKey, DefaultModel, httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_MissingApiKey_ThrowsInvalidOperationException()
    {
        // Arrange & Act & Assert — null key
        var ex1 = Assert.Throws<InvalidOperationException>(
            () => new GeminiEmbeddingService(null!));

        Assert.Contains("API key is required", ex1.Message);

        // Empty string key
        var ex2 = Assert.Throws<InvalidOperationException>(
            () => new GeminiEmbeddingService(""));

        Assert.Contains("API key is required", ex2.Message);

        // Whitespace key
        var ex3 = Assert.Throws<InvalidOperationException>(
            () => new GeminiEmbeddingService("   "));

        Assert.Contains("API key is required", ex3.Message);
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string? _responseContent;
        private readonly HttpStatusCode _statusCode;
        private readonly Exception? _exception;

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_exception is not null)
            {
                throw _exception;
            }

            return new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_responseContent!, Encoding.UTF8, "application/json")
            };
        }
    }
}
