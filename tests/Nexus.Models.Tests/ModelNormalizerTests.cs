using Nexus.Models;
using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models.Tests;

public class ModelNormalizerTests
{
    private static readonly IReadOnlyList<BackendRuntime> SharedBackends =
        new[] { BackendRuntime.LlamaCpp, BackendRuntime.OllamaRuntime };

    private static readonly IReadOnlyList<ModelTaskFit> SharedTaskFit =
        new[] { ModelTaskFit.Chat };

    private static readonly IReadOnlyList<string> SharedLanguages =
        new[] { "en" };

    private static readonly DistributionProfile SharedDistProfile = new(
        AvailableSources: DistributionSource.Ollama,
        PreferredSource: DistributionSource.Ollama,
        OllamaModelTag: "llama3.2:3b-q4km",
        HuggingFaceRepoId: null,
        HuggingFaceFilename: null,
        EstimatedDownloadSize: 2_000_000_000L,
        CanBeManagedByRuntime: true,
        InstallComplexity: InstallComplexity.Low);

    // Default: Llama 3.2 3B Q4_K_M GGUF, ctx=4096
    private static ModelCandidate CreateCandidate() =>
        new(
            Id: "llama3.2-3b-q4km",
            Family: "Llama",
            Variant: "3.2-3B",
            Quantization: "Q4_K_M",
            Format: ModelFormat.GGUF,
            ParameterCount: 3_000L,
            EstimatedWeightSize: 1_500_000_000L,
            ContextWindowSize: 4_096,
            SupportedBackends: SharedBackends,
            TaskFit: SharedTaskFit,
            LanguageSupport: SharedLanguages,
            DistributionProfile: SharedDistProfile);

    private static readonly ModelNormalizer Normalizer = new();

    [Fact]
    public void Normalize_Llama3B_Q4_ReturnsExpectedMemory()
    {
        // Arrange
        var candidate = CreateCandidate(); // 3000M, Q4_K_M(0.5), ctx=4096

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        const long ExpectedWeightSize = 1_500_000_000L;         // 3000 * 1M * 0.5
        long expectedRamOnLoad = (long)(ExpectedWeightSize * ModelNormalizer.LargeModelOverhead); // 1.15 overhead
        const long ExpectedKvCache    = 436_207_616L;           // 2*26*4096*128*8*2.0
        Assert.Equal(expectedRamOnLoad, profile.EstimatedRamOnLoad);
        Assert.Equal(expectedRamOnLoad + ExpectedKvCache + ModelNormalizer.ScratchBufferBytes,
            profile.EstimatedRamOnInference);
        Assert.Equal(expectedRamOnLoad + ExpectedKvCache, profile.EstimatedVramOnFullOffload);
        Assert.Equal((long)(ExpectedWeightSize * ModelNormalizer.PartialOffloadRatio),
            profile.EstimatedVramOnPartialOffload);
        Assert.Equal(CpuCostClass.Low, profile.CpuCostClass);
        Assert.Equal(InferenceSpeedClass.Fast, profile.InferenceSpeedClass);
        Assert.Equal(QualityTier.Basic, profile.QualityTier);
        Assert.Equal(BackendRuntime.LlamaCpp, profile.RequiredRuntime);
        Assert.Equal(2, profile.CompatibleArchitectures.Count);
        Assert.Contains(CompatibleArchitecture.x64, profile.CompatibleArchitectures);
        Assert.Contains(CompatibleArchitecture.ARM64, profile.CompatibleArchitectures);
    }

    [Fact]
    public void Normalize_Mistral7B_Q5_ReturnsExpectedMemory()
    {
        // Arrange
        var candidate = CreateCandidate() with { ParameterCount = 7_000L, Quantization = "Q5_K_M", ContextWindowSize = 8_192 };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        const long ExpectedWeightSize = 4_375_000_000L;  // 7000 * 1M * 0.625
        long expectedRamOnLoad = (long)(ExpectedWeightSize * ModelNormalizer.LargeModelOverhead);
        Assert.Equal(ExpectedWeightSize, (long)(7_000L * 1_000_000L * 0.625));  // sanity
        Assert.Equal(expectedRamOnLoad, profile.EstimatedRamOnLoad);
        Assert.Equal(CpuCostClass.Medium, profile.CpuCostClass);
        Assert.Equal(InferenceSpeedClass.Moderate, profile.InferenceSpeedClass);
        Assert.Equal(QualityTier.Strong, profile.QualityTier);
    }

    [Fact]
    public void Normalize_Llama8B_FP16_ReturnsExpectedMemory()
    {
        // Arrange
        var candidate = CreateCandidate() with { ParameterCount = 8_000L, Quantization = "FP16" };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        const long ExpectedWeightSize = 16_000_000_000L; // 8000 * 1M * 2.0
        Assert.Equal(ExpectedWeightSize, (long)(8_000L * 1_000_000L * 2.0)); // sanity
        Assert.Equal((long)(ExpectedWeightSize * ModelNormalizer.LargeModelOverhead), profile.EstimatedRamOnLoad);
        Assert.Equal(CpuCostClass.Medium, profile.CpuCostClass);
        Assert.Equal(InferenceSpeedClass.Slow, profile.InferenceSpeedClass);
        Assert.Equal(QualityTier.Strong, profile.QualityTier);
    }

    [Fact]
    public void Normalize_DeepSeek14B_Q4_ReturnsExpectedMemory()
    {
        // Arrange
        var candidate = CreateCandidate() with { ParameterCount = 14_000L, Quantization = "Q4_K_M" };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert — 40-layer tier, CpuCost=High
        const long ExpectedWeightSize = 7_000_000_000L; // 14000 * 1M * 0.5
        Assert.Equal((long)(ExpectedWeightSize * ModelNormalizer.LargeModelOverhead), profile.EstimatedRamOnLoad);
        Assert.Equal(CpuCostClass.High, profile.CpuCostClass);
        Assert.Equal(InferenceSpeedClass.Slow, profile.InferenceSpeedClass);
        Assert.Equal(QualityTier.Strong, profile.QualityTier);
    }

    [Fact]
    public void Normalize_SmallModel_Under1B_UsesHigherOverhead()
    {
        // Arrange — 500M params is below OverheadBoundary (1000), should use 1.20 overhead
        var candidate = CreateCandidate() with { ParameterCount = 500L };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        var weightSize = ModelNormalizer.ComputeWeightSize(500L, 0.5);
        var expectedRam = ModelNormalizer.ComputeRamOnLoad(weightSize, 500L);
        Assert.Equal((long)(weightSize * ModelNormalizer.SmallModelOverhead), expectedRam);
        Assert.Equal(expectedRam, profile.EstimatedRamOnLoad);
    }

    [Fact]
    public void ClassifyCpuCost_ByParamCount_ReturnsCorrectTier()
    {
        // Arrange & Act & Assert
        Assert.Equal(CpuCostClass.Low,     ModelNormalizer.ClassifyCpuCost(3_000L));
        Assert.Equal(CpuCostClass.Medium,  ModelNormalizer.ClassifyCpuCost(7_000L));
        Assert.Equal(CpuCostClass.High,    ModelNormalizer.ClassifyCpuCost(14_000L));
        Assert.Equal(CpuCostClass.VeryHigh, ModelNormalizer.ClassifyCpuCost(70_000L));
    }

    [Fact]
    public void ClassifyGpuCost_OnnxFormat_ReturnsNone()
    {
        // Arrange
        var candidate = CreateCandidate() with { Format = ModelFormat.ONNX };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        Assert.Equal(GpuCostClass.None, profile.GpuCostClass);
    }

    [Fact]
    public void ClassifyGpuCost_ByParamCount_ReturnsCorrectTier()
    {
        // Arrange & Act & Assert (non-ONNX format)
        Assert.Equal(GpuCostClass.Low,    ModelNormalizer.ClassifyGpuCost(3_000L, ModelFormat.GGUF));
        Assert.Equal(GpuCostClass.Medium, ModelNormalizer.ClassifyGpuCost(8_000L, ModelFormat.GGUF));
        Assert.Equal(GpuCostClass.High,   ModelNormalizer.ClassifyGpuCost(14_000L, ModelFormat.GGUF));
    }

    [Fact]
    public void ClassifyInferenceSpeed_SmallQ4_ReturnsFast()
    {
        // Arrange & Act
        var speed = ModelNormalizer.ClassifyInferenceSpeed(3_000L, 0.5);

        // Assert
        Assert.Equal(InferenceSpeedClass.Fast, speed);
    }

    [Fact]
    public void ClassifyInferenceSpeed_MediumFP16_ReturnsSlow()
    {
        // Arrange & Act
        var speed = ModelNormalizer.ClassifyInferenceSpeed(8_000L, 2.0);

        // Assert
        Assert.Equal(InferenceSpeedClass.Slow, speed);
    }

    [Fact]
    public void ClassifyQuality_8B_Q4_ReturnsGood()
    {
        // Arrange & Act — 8000 ≤ MediumModelThreshold, bpp=0.5 < BppBoundary
        var quality = ModelNormalizer.ClassifyQuality(8_000L, 0.5);

        // Assert
        Assert.Equal(QualityTier.Good, quality);
    }

    [Fact]
    public void ClassifyQuality_8B_Q5_ReturnsStrong()
    {
        // Arrange & Act — 8000 ≤ MediumModelThreshold, bpp=0.625 is NOT < BppBoundary
        var quality = ModelNormalizer.ClassifyQuality(8_000L, 0.625);

        // Assert
        Assert.Equal(QualityTier.Strong, quality);
    }

    [Fact]
    public void Normalize_OnnxModel_ReturnsX64OnlyAndOnnxRuntime()
    {
        // Arrange
        var candidate = CreateCandidate() with { Format = ModelFormat.ONNX };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert
        Assert.Single(profile.CompatibleArchitectures);
        Assert.Equal(CompatibleArchitecture.x64, profile.CompatibleArchitectures[0]);
        Assert.Equal(BackendRuntime.OnnxRuntime, profile.RequiredRuntime);
        Assert.Equal(GpuCostClass.None, profile.GpuCostClass);
    }

    [Fact]
    public void GetBytesPerParam_UnknownQuantization_ReturnsDefault()
    {
        // Arrange & Act
        var bpp = ModelNormalizer.GetBytesPerParam("UNKNOWN");

        // Assert
        Assert.Equal(0.5, bpp);
    }

    [Fact]
    public void Normalize_ZeroParameterCount_ReturnsValidProfile()
    {
        // Arrange — 0M params, Q4_K_M (bpp=0.5), ctx=4096
        var candidate = CreateCandidate() with { ParameterCount = 0 };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert — weight=0, ramOnLoad=0, KV cache from tier (26L), no crash
        Assert.Equal(0L, profile.EstimatedRamOnLoad);
        Assert.Equal(0L, profile.EstimatedVramOnPartialOffload);

        // KV cache is non-zero because architecture tier still applies (26 layers, ctx=4096)
        long expectedKvCache = (long)(2L * 26 * 4_096 * 128 * 8 * ModelNormalizer.KvBytesPerValue);
        Assert.Equal(expectedKvCache, profile.EstimatedVramOnFullOffload);
        Assert.Equal(expectedKvCache + ModelNormalizer.ScratchBufferBytes, profile.EstimatedRamOnInference);
        Assert.Equal(CpuCostClass.Low, profile.CpuCostClass);
        Assert.Equal(QualityTier.Basic, profile.QualityTier);
    }

    [Fact]
    public void Normalize_VeryLargeModel_NoOverflow()
    {
        // Arrange — 100B params (100_000M), FP16 (bpp=2.0)
        var candidate = CreateCandidate() with { ParameterCount = 100_000L, Quantization = "FP16" };

        // Act
        ModelExecutionProfile profile = Normalizer.Normalize(candidate);

        // Assert — long arithmetic does not overflow to negative
        long expectedWeight = (long)(100_000L * 1_000_000L * 2.0); // 200,000,000,000,000
        Assert.True(expectedWeight > 0, "Weight size must not overflow");
        Assert.True(profile.EstimatedRamOnLoad > 0, "RAM on load must not overflow");
        Assert.Equal((long)(expectedWeight * ModelNormalizer.LargeModelOverhead), profile.EstimatedRamOnLoad);
        Assert.Equal(CpuCostClass.VeryHigh, profile.CpuCostClass);
        Assert.Equal(QualityTier.Premium, profile.QualityTier);
    }

    [Fact]
    public void GetBytesPerParam_EmptyString_ReturnsDefault()
    {
        // Arrange & Act
        var bpp = ModelNormalizer.GetBytesPerParam("");

        // Assert — empty string falls to wildcard default
        Assert.Equal(0.5, bpp);
    }

    [Fact]
    public void Normalize_NullCandidate_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => Normalizer.Normalize(null!));
    }
}
