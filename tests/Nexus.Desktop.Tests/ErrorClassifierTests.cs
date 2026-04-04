using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Tests;

public class ErrorClassifierTests
{
    [Fact]
    public void Classify_HttpRequestException_ReturnsConnectionCategory()
    {
        // Arrange & Act
        var (category, userMessage, _) = ErrorClassifier.Classify(new HttpRequestException("Connection refused"));

        // Assert
        Assert.Equal("connection", category);
        Assert.Contains("connect", userMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_TaskCanceledException_ReturnsTimeoutCategory()
    {
        // Arrange & Act
        var (category, userMessage, _) = ErrorClassifier.Classify(new TaskCanceledException("Timed out"));

        // Assert
        Assert.Equal("timeout", category);
        Assert.Contains("timed out", userMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_UnauthorizedMessage_ReturnsApiKeyCategory()
    {
        // Arrange & Act
        var (category, userMessage, _) = ErrorClassifier.Classify(new InvalidOperationException("Unauthorized access"));

        // Assert
        Assert.Equal("apikey", category);
        Assert.Contains("API key", userMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_GenericException_ReturnsGenericCategory()
    {
        // Arrange & Act
        var (category, userMessage, _) = ErrorClassifier.Classify(new InvalidOperationException("something broke"));

        // Assert
        Assert.Equal("generic", category);
        Assert.Contains("unexpected", userMessage, StringComparison.OrdinalIgnoreCase);
    }
}
