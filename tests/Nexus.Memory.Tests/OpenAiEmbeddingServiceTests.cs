using System.Net;
using System.Text;
using System.Text.Json;

namespace Nexus.Memory.Tests;

public class OpenAiEmbeddingServiceTests
{
    private static readonly EmbeddingOptions DefaultOptions = new(
        Endpoint: "https://api.openai.com",
        Model: "text-embedding-3-small",
        Dimensions: 1536);

    [Fact]
    public async Task GenerateEmbeddingAsync_ValidText_ReturnsEmbeddingWithCorrectAuth()
    {
        // Arrange
        var embedding = new float[1536];
        for (var i = 0; i < embedding.Length; i++)
            embedding[i] = i * 0.001f;

        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[] { new { embedding } }
        });
        var handler = new MockHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new OpenAiEmbeddingService(DefaultOptions, "sk-test-key-123", httpClient);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(1536, result.Length);
        Assert.NotNull(handler.LastRequestHeaders);
        Assert.Equal("Bearer", handler.LastRequestHeaders!.Authorization?.Scheme);
        Assert.Equal("sk-test-key-123", handler.LastRequestHeaders.Authorization?.Parameter);
        Assert.Contains("/v1/embeddings", handler.LastRequestUri?.ToString());

        // Verify request body contains model
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("text-embedding-3-small", handler.LastRequestBody);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var handler = new MockHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new OpenAiEmbeddingService(DefaultOptions, "sk-test-key", httpClient);

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
        var service = new OpenAiEmbeddingService(DefaultOptions, "sk-invalid-key", httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test"));

        Assert.Contains("API key is invalid", ex.Message);
        Assert.Contains("Check your API key", ex.Message);
    }

    [Fact]
    public void Constructor_MissingApiKey_ThrowsInvalidOperationException()
    {
        // Arrange & Act & Assert — null key
        var ex1 = Assert.Throws<InvalidOperationException>(
            () => new OpenAiEmbeddingService(DefaultOptions, null!));

        Assert.Contains("API key is required", ex1.Message);
        Assert.Contains("OPENAI_API_KEY", ex1.Message);

        // Empty string key
        var ex2 = Assert.Throws<InvalidOperationException>(
            () => new OpenAiEmbeddingService(DefaultOptions, ""));

        Assert.Contains("API key is required", ex2.Message);

        // Whitespace key
        var ex3 = Assert.Throws<InvalidOperationException>(
            () => new OpenAiEmbeddingService(DefaultOptions, "   "));

        Assert.Contains("API key is required", ex3.Message);
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string? _responseContent;
        private readonly HttpStatusCode _statusCode;
        private readonly Exception? _exception;

        public Uri? LastRequestUri { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? LastRequestHeaders { get; private set; }
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
            LastRequestHeaders = request.Headers;

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
