using System.Text.Json;
using Nexus.Models;
using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models.Tests;

public class ModelCandidateTests
{
    private static readonly IReadOnlyList<BackendRuntime> SharedBackends =
        new List<BackendRuntime> { BackendRuntime.LlamaCpp, BackendRuntime.OllamaRuntime };

    private static readonly IReadOnlyList<ModelTaskFit> SharedTaskFit =
        new List<ModelTaskFit> { ModelTaskFit.Chat, ModelTaskFit.Coding };

    private static readonly IReadOnlyList<string> SharedLanguages =
        new List<string> { "en", "es" };

    private static readonly DistributionProfile SharedDistProfile =
        new(
            AvailableSources: DistributionSource.Ollama,
            PreferredSource: DistributionSource.Ollama,
            OllamaModelTag: "qwen3:14b",
            HuggingFaceRepoId: null,
            HuggingFaceFilename: null,
            EstimatedDownloadSize: 8_000_000_000L,
            CanBeManagedByRuntime: true,
            InstallComplexity: InstallComplexity.Low);

    private static ModelCandidate CreateCandidate() =>
        new(
            Id: "qwen3-14b-q4km",
            Family: "Qwen3",
            Variant: "14B",
            Quantization: "Q4_K_M",
            Format: ModelFormat.GGUF,
            ParameterCount: 14_000L,
            EstimatedWeightSize: 8_100_000_000L,
            ContextWindowSize: 32_768,
            SupportedBackends: SharedBackends,
            TaskFit: SharedTaskFit,
            LanguageSupport: SharedLanguages,
            DistributionProfile: SharedDistProfile);

    [Fact]
    public void Construction_WithAllProperties_SetsValues()
    {
        // Arrange & Act
        var candidate = CreateCandidate();

        // Assert
        Assert.Equal("qwen3-14b-q4km", candidate.Id);
        Assert.Equal("Qwen3", candidate.Family);
        Assert.Equal("14B", candidate.Variant);
        Assert.Equal("Q4_K_M", candidate.Quantization);
        Assert.Equal(ModelFormat.GGUF, candidate.Format);
        Assert.Equal(14_000L, candidate.ParameterCount);
        Assert.Equal(8_100_000_000L, candidate.EstimatedWeightSize);
        Assert.Equal(32_768, candidate.ContextWindowSize);
        Assert.Equal(2, candidate.SupportedBackends.Count);
        Assert.Equal(2, candidate.TaskFit.Count);
        Assert.Equal(2, candidate.LanguageSupport.Count);
        Assert.Equal(SharedDistProfile, candidate.DistributionProfile);
    }

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var candidate = CreateCandidate();

        // Act
        var result = candidate.ToString();

        // Assert
        Assert.Equal("Qwen3 14B [Q4_K_M] (14000M params, GGUF)", result);
    }

    [Fact]
    public void Immutability_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = CreateCandidate();

        // Act
        var modified = original with { Family = "Other" };

        // Assert
        Assert.Equal("Qwen3", original.Family);
        Assert.Equal("Other", modified.Family);
        Assert.NotSame(original, modified);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange — share the same list references so record structural equality holds
        var candidate1 = new ModelCandidate(
            Id: "qwen3-14b-q4km",
            Family: "Qwen3",
            Variant: "14B",
            Quantization: "Q4_K_M",
            Format: ModelFormat.GGUF,
            ParameterCount: 14_000L,
            EstimatedWeightSize: 8_100_000_000L,
            ContextWindowSize: 32_768,
            SupportedBackends: SharedBackends,
            TaskFit: SharedTaskFit,
            LanguageSupport: SharedLanguages,
            DistributionProfile: SharedDistProfile);
        var candidate2 = new ModelCandidate(
            Id: "qwen3-14b-q4km",
            Family: "Qwen3",
            Variant: "14B",
            Quantization: "Q4_K_M",
            Format: ModelFormat.GGUF,
            ParameterCount: 14_000L,
            EstimatedWeightSize: 8_100_000_000L,
            ContextWindowSize: 32_768,
            SupportedBackends: SharedBackends,
            TaskFit: SharedTaskFit,
            LanguageSupport: SharedLanguages,
            DistributionProfile: SharedDistProfile);

        // Assert
        Assert.Equal(candidate1, candidate2);
        Assert.True(candidate1 == candidate2);
    }

    [Fact]
    public void SupportedBackends_IReadOnlyList_WorksCorrectly()
    {
        // Arrange
        var candidate = CreateCandidate();

        // Act & Assert
        Assert.IsAssignableFrom<IReadOnlyList<BackendRuntime>>(candidate.SupportedBackends);
        Assert.Contains(BackendRuntime.LlamaCpp, candidate.SupportedBackends);
        Assert.Contains(BackendRuntime.OllamaRuntime, candidate.SupportedBackends);
    }

    [Fact]
    public void JsonRoundTrip_SerializeDeserialize_PreservesValues()
    {
        // Arrange
        var candidate = CreateCandidate();

        // Act
        var json = JsonSerializer.Serialize(candidate);
        var deserialized = JsonSerializer.Deserialize<ModelCandidate>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("qwen3-14b-q4km", deserialized.Id);
        Assert.Equal("Qwen3", deserialized.Family);
        Assert.Equal("14B", deserialized.Variant);
        Assert.Equal("Q4_K_M", deserialized.Quantization);
        Assert.Equal(ModelFormat.GGUF, deserialized.Format);
        Assert.Equal(14_000L, deserialized.ParameterCount);
        Assert.Equal(8_100_000_000L, deserialized.EstimatedWeightSize);
        Assert.Equal(32_768, deserialized.ContextWindowSize);
        Assert.Equal(2, deserialized.SupportedBackends.Count);
        Assert.Equal(BackendRuntime.LlamaCpp, deserialized.SupportedBackends[0]);
        Assert.Equal(BackendRuntime.OllamaRuntime, deserialized.SupportedBackends[1]);
        Assert.Equal(2, deserialized.TaskFit.Count);
        Assert.Equal(ModelTaskFit.Chat, deserialized.TaskFit[0]);
        Assert.Equal(ModelTaskFit.Coding, deserialized.TaskFit[1]);
        Assert.Equal(2, deserialized.LanguageSupport.Count);
        Assert.Equal("en", deserialized.LanguageSupport[0]);
        Assert.Equal("es", deserialized.LanguageSupport[1]);
    }

    [Fact]
    public void TaskFit_ContainsExpectedValues()
    {
        // Arrange
        var candidate = CreateCandidate();

        // Act & Assert
        Assert.Contains(ModelTaskFit.Chat, candidate.TaskFit);
        Assert.Contains(ModelTaskFit.Coding, candidate.TaskFit);
    }
}
