using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models.Tests;

public class DistributionProfileTests
{
    private static DistributionProfile CreateProfile() =>
        new(
            AvailableSources: DistributionSource.Ollama | DistributionSource.HuggingFace,
            PreferredSource: DistributionSource.Ollama,
            OllamaModelTag: "llama3.2:3b",
            HuggingFaceRepoId: "meta-llama/Llama-3.2-3B",
            HuggingFaceFilename: "llama-3.2-3b.gguf",
            EstimatedDownloadSize: 2_000_000_000L,
            CanBeManagedByRuntime: true,
            InstallComplexity: InstallComplexity.Low);

    [Fact]
    public void Construction_WithAllProperties_SetsValues()
    {
        // Arrange & Act
        var profile = CreateProfile();

        // Assert
        Assert.Equal(DistributionSource.Ollama | DistributionSource.HuggingFace, profile.AvailableSources);
        Assert.Equal(DistributionSource.Ollama, profile.PreferredSource);
        Assert.Equal("llama3.2:3b", profile.OllamaModelTag);
        Assert.Equal("meta-llama/Llama-3.2-3B", profile.HuggingFaceRepoId);
        Assert.Equal("llama-3.2-3b.gguf", profile.HuggingFaceFilename);
        Assert.Equal(2_000_000_000L, profile.EstimatedDownloadSize);
        Assert.True(profile.CanBeManagedByRuntime);
        Assert.Equal(InstallComplexity.Low, profile.InstallComplexity);
    }

    [Fact]
    public void Immutability_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = CreateProfile();

        // Act
        var modified = original with { PreferredSource = DistributionSource.HuggingFace, InstallComplexity = InstallComplexity.Medium };

        // Assert
        Assert.Equal(DistributionSource.Ollama, original.PreferredSource);
        Assert.Equal(InstallComplexity.Low, original.InstallComplexity);
        Assert.Equal(DistributionSource.HuggingFace, modified.PreferredSource);
        Assert.Equal(InstallComplexity.Medium, modified.InstallComplexity);
        Assert.NotSame(original, modified);
    }

    [Fact]
    public void FlagsEnum_OllamaAndHuggingFace_CombinesCorrectly()
    {
        // Arrange
        var combined = DistributionSource.Ollama | DistributionSource.HuggingFace;

        // Act & Assert
        Assert.Equal(3, (int)combined);
        Assert.True(combined.HasFlag(DistributionSource.Ollama));
        Assert.True(combined.HasFlag(DistributionSource.HuggingFace));
    }

    [Fact]
    public void NullableProperties_WhenNull_AreNull()
    {
        // Arrange & Act
        var profile = new DistributionProfile(
            AvailableSources: DistributionSource.Ollama,
            PreferredSource: DistributionSource.Ollama,
            OllamaModelTag: null,
            HuggingFaceRepoId: null,
            HuggingFaceFilename: null,
            EstimatedDownloadSize: 0L,
            CanBeManagedByRuntime: false,
            InstallComplexity: InstallComplexity.High);

        // Assert
        Assert.Null(profile.OllamaModelTag);
        Assert.Null(profile.HuggingFaceRepoId);
        Assert.Null(profile.HuggingFaceFilename);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var profile1 = CreateProfile();
        var profile2 = CreateProfile();

        // Assert
        Assert.Equal(profile1, profile2);
        Assert.True(profile1 == profile2);
    }
}
