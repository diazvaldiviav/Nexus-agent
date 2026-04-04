using Nexus.Core.Config;
using Xunit;

namespace Nexus.Core.Tests;

public class ModelsConfigGetApiKeyTests
{
    [Fact]
    public void GetApiKey_ReturnsDedicatedSectionKey_WhenPresent()
    {
        // Arrange
        var config = new ModelsConfig
        {
            Gemini = new ProviderKeyConfig { ApiKey = "gemini-dedicated-key" }
        };

        // Act
        var result = config.GetApiKey("gemini");

        // Assert
        Assert.Equal("gemini-dedicated-key", result);
    }

    [Fact]
    public void GetApiKey_FallsBackToCloudApiKey_WhenProviderMatches()
    {
        // Arrange
        var config = new ModelsConfig
        {
            Cloud = new ModelProviderConfig { Provider = "gemini", ApiKey = "cloud-key" }
        };

        // Act
        var result = config.GetApiKey("gemini");

        // Assert
        Assert.Equal("cloud-key", result);
    }

    [Fact]
    public void GetApiKey_DoesNotReturnCloudApiKey_WhenProviderDiffers()
    {
        // Arrange
        var config = new ModelsConfig
        {
            Cloud = new ModelProviderConfig { Provider = "gemini", ApiKey = "cloud-key" }
        };

        // Act
        var result = config.GetApiKey("anthropic");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetApiKey_ReturnsNull_WhenNoKeyAvailable()
    {
        // Arrange
        var config = new ModelsConfig();

        // Act
        var result = config.GetApiKey("gemini");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEndpoint_ReturnsDedicatedEndpoint_WhenPresent()
    {
        // Arrange
        var config = new ModelsConfig
        {
            Anthropic = new ProviderKeyConfig { Endpoint = "https://custom.anthropic.com" }
        };

        // Act
        var result = config.GetEndpoint("anthropic");

        // Assert
        Assert.Equal("https://custom.anthropic.com", result);
    }

    [Fact]
    public void GetApiKey_DedicatedTakesPriority_OverCloudAndEnv()
    {
        // Arrange
        var config = new ModelsConfig
        {
            Gemini = new ProviderKeyConfig { ApiKey = "dedicated-key" },
            Cloud = new ModelProviderConfig { Provider = "gemini", ApiKey = "cloud-key" }
        };

        // Act
        var result = config.GetApiKey("gemini");

        // Assert
        Assert.Equal("dedicated-key", result);
    }
}
