using Nexus.Core.Config;

namespace Nexus.Core.Tests;

public class ConfigValidatorTests
{
    [Fact]
    public void ValidateDecayLambda_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.05);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateDecayLambda_BelowMin_ReturnsError()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.0001);
        Assert.NotNull(result);
        Assert.Contains("0.001", result);
    }

    [Fact]
    public void ValidateDecayLambda_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateDecayLambda(1.5);
        Assert.NotNull(result);
        Assert.Contains("1.0", result);
    }

    [Fact]
    public void ValidateDecayLambda_AtBoundaryMin_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(0.001);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateDecayLambda_AtBoundaryMax_ReturnsNull()
    {
        var result = ConfigValidator.ValidateDecayLambda(1.0);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_ValidHttp_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("http://localhost:11434");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_ValidHttps_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("https://api.example.com");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_Empty_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_Null_ReturnsNull()
    {
        var result = ConfigValidator.ValidateLocalEndpoint(null);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateLocalEndpoint_NotUri_ReturnsError()
    {
        var result = ConfigValidator.ValidateLocalEndpoint("not-a-url");
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSummarizationInterval_Valid_ReturnsNull()
    {
        var result = ConfigValidator.ValidateSummarizationInterval(10);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateSummarizationInterval_Zero_ReturnsError()
    {
        var result = ConfigValidator.ValidateSummarizationInterval(0);
        Assert.NotNull(result);
        Assert.Contains("at least 1", result);
    }

    [Fact]
    public void ValidateRecentInteractionsFetchLimit_InRange_ReturnsNull()
    {
        var result = ConfigValidator.ValidateRecentInteractionsFetchLimit(5);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateRecentInteractionsFetchLimit_AboveMax_ReturnsError()
    {
        var result = ConfigValidator.ValidateRecentInteractionsFetchLimit(51);
        Assert.NotNull(result);
        Assert.Contains("50", result);
    }

    [Fact]
    public void CheckApiKeyWarning_ProviderMissingKey_ReturnsWarning()
    {
        var result = ConfigValidator.CheckApiKeyWarning("anthropic", null, null, null);
        Assert.NotNull(result);
        Assert.Contains("anthropic", result);
        Assert.Contains("Cloud features", result);
    }

    [Fact]
    public void Validate_ValidConfig_IsValid()
    {
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";

        var result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidConfig_ReturnsSpecificErrors()
    {
        // Arrange
        var config = new NexusConfig();
        config.Memory.RelevanceDecayLambda = 0.0;
        config.Models.Local.Endpoint = "not-a-url";
        config.Memory.SummarizationInterval = 0;
        config.Memory.RecentInteractionsFetchLimit = 100;

        // Act
        var result = ConfigValidator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.GetError("DecayLambda"));
        Assert.NotNull(result.GetError("LocalEndpoint"));
        Assert.NotNull(result.GetError("SummarizationInterval"));
        Assert.NotNull(result.GetError("RecentInteractionsFetchLimit"));
        Assert.Equal(4, result.Errors.Count);
    }

}
