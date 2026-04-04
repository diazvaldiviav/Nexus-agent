using Nexus.Memory.Embedding;
using Nexus.Memory.Tests.Fakes;

namespace Nexus.Memory.Tests;

public class FallbackEmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingAsync_PrimarySucceeds_ReturnsPrimaryResult()
    {
        // Arrange
        var primaryEmbedding = new float[] { 1.0f, 2.0f, 3.0f };
        var fallbackEmbedding = new float[] { 9.0f, 8.0f, 7.0f };
        var primary = new FakeEmbeddingService(primaryEmbedding);
        var fallback = new FakeEmbeddingService(fallbackEmbedding);
        var service = new FallbackEmbeddingService(primary, fallback);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(primaryEmbedding, result);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_PrimaryFails_ReturnsFallbackResult()
    {
        // Arrange
        var fallbackEmbedding = new float[] { 9.0f, 8.0f, 7.0f };
        var primary = new FakeEmbeddingService(exception: new InvalidOperationException("Primary down"));
        var fallback = new FakeEmbeddingService(fallbackEmbedding);
        var service = new FallbackEmbeddingService(primary, fallback);

        // Act
        var result = await service.GenerateEmbeddingAsync("test input");

        // Assert
        Assert.Equal(fallbackEmbedding, result);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_BothFail_ThrowsException()
    {
        // Arrange
        var primary = new FakeEmbeddingService(exception: new InvalidOperationException("Primary down"));
        var fallback = new FakeEmbeddingService(exception: new InvalidOperationException("Fallback also down"));
        var service = new FallbackEmbeddingService(primary, fallback);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test input"));

        Assert.Contains("Fallback also down", ex.Message);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_PrimaryFailsNoFallback_ThrowsException()
    {
        // Arrange — no fallback provided
        var primary = new FakeEmbeddingService(exception: new InvalidOperationException("Primary down"));
        var service = new FallbackEmbeddingService(primary);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("test input"));

        Assert.Contains("Primary down", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ForwardsCancellationToken_ToPrimaryService()
    {
        // Arrange
        var primary = new FakeEmbeddingService(new float[] { 1.0f, 2.0f });
        var service = new FallbackEmbeddingService(primary);
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await service.GenerateEmbeddingAsync("test", token);

        // Assert
        Assert.NotNull(primary.LastCancellationToken);
        Assert.Equal(token, primary.LastCancellationToken!.Value);
    }

    [Fact]
    public void Constructor_NullPrimary_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FallbackEmbeddingService(null!));
    }
}
