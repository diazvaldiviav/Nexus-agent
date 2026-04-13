using Nexus.Models.Enums;
using Nexus.Models.Profiles;

namespace Nexus.Models;

/// <summary>
/// Derives a <see cref="ModelExecutionProfile"/> from a <see cref="ModelCandidate"/> using
/// fixed quantization tables, architectural tier heuristics, and overhead constants.
/// Stateless and deterministic — safe to use as a singleton.
/// </summary>
public class ModelNormalizer : IModelNormalizer
{
    // Parameter count thresholds (millions)
    internal const long SmallModelThreshold = 3_000;
    internal const long MediumModelThreshold = 8_000;
    internal const long LargeModelThreshold = 14_000;

    // RAM overhead multipliers
    internal const double SmallModelOverhead = 1.20;
    internal const double LargeModelOverhead = 1.15;
    internal const long OverheadBoundary = 1_000;

    // VRAM and KV cache sizing
    internal const long ScratchBufferBytes = 512L * 1024 * 1024;
    internal const double PartialOffloadRatio = 0.40;
    internal const double KvBytesPerValue = 2.0;
    internal const double BppBoundary = 0.625;

    private readonly record struct ArchitectureTier(int NumLayers, int NumHeads, int HeadDim, int NumKvHeads);

    /// <inheritdoc />
    public ModelExecutionProfile Normalize(ModelCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var bpp = GetBytesPerParam(candidate.Quantization);
        var weightSize = ComputeWeightSize(candidate.ParameterCount, bpp);
        var ramOnLoad = ComputeRamOnLoad(weightSize, candidate.ParameterCount);
        var kvCache = ComputeKvCacheSize(candidate.ParameterCount, candidate.ContextWindowSize);
        var ramOnInference = ramOnLoad + kvCache + ScratchBufferBytes;
        var vramFull = ramOnLoad + kvCache;
        var vramPartial = (long)(weightSize * PartialOffloadRatio);

        var cpuCost = ClassifyCpuCost(candidate.ParameterCount);
        var gpuCost = ClassifyGpuCost(candidate.ParameterCount, candidate.Format);
        var speed = ClassifyInferenceSpeed(candidate.ParameterCount, bpp);
        var quality = ClassifyQuality(candidate.ParameterCount, bpp);

        var architectures = DetermineArchitectures(candidate.Format);
        var runtime = DetermineRuntime(candidate.Format);

        return new ModelExecutionProfile(
            CompatibleArchitectures: architectures,
            EstimatedRamOnLoad: ramOnLoad,
            EstimatedRamOnInference: ramOnInference,
            EstimatedVramOnFullOffload: vramFull,
            EstimatedVramOnPartialOffload: vramPartial,
            CpuCostClass: cpuCost,
            GpuCostClass: gpuCost,
            InferenceSpeedClass: speed,
            QualityTier: quality,
            RequiredRuntime: runtime);
    }

    /// <summary>Returns the bytes-per-parameter multiplier for the given quantization scheme, defaulting to 0.5 for unknown schemes.</summary>
    internal static double GetBytesPerParam(string quantization) => quantization switch
    {
        "FP32"     => 4.0,
        "FP16"     => 2.0,
        "BF16"     => 2.0,
        "Q8_0"     => 1.0,
        "Q6_K"     => 0.83,
        "Q5_K_M"   => 0.625,
        "Q5_K_S"   => 0.625,
        "Q5_K_L"   => 0.625,
        "Q4_K_M"   => 0.5,
        "Q4_K_S"   => 0.5,
        "Q4_K_L"   => 0.5,
        "Q4_0"     => 0.5,
        "Q3_K_M"   => 0.4375,
        "Q3_K_S"   => 0.4375,
        "Q3_K_L"   => 0.4375,
        "Q2_K"     => 0.3125,
        "IQ4_XS"   => 0.5,
        "IQ4_NL"   => 0.5,
        "IQ3_XXS"  => 0.39,
        "IQ2_XXS"  => 0.28,
        _          => 0.5
    };

    /// <summary>Computes the estimated weight file size in bytes from parameter count and bytes-per-parameter.</summary>
    internal static long ComputeWeightSize(long paramCountMillions, double bytesPerParam) =>
        (long)(paramCountMillions * 1_000_000L * bytesPerParam);

    /// <summary>Computes estimated RAM required to load weights, applying a size-dependent overhead multiplier.</summary>
    internal static long ComputeRamOnLoad(long weightSize, long paramCountMillions) =>
        (long)(weightSize * (paramCountMillions < OverheadBoundary ? SmallModelOverhead : LargeModelOverhead));

    /// <summary>Computes the KV cache memory in bytes based on architecture tier and context length.</summary>
    internal static long ComputeKvCacheSize(long paramCountMillions, int contextLength)
    {
        var tier = GetArchitectureTier(paramCountMillions);
        return (long)(2L * tier.NumLayers * contextLength * tier.HeadDim * tier.NumKvHeads * KvBytesPerValue);
    }

    /// <summary>Classifies CPU cost tier based on parameter count thresholds.</summary>
    internal static CpuCostClass ClassifyCpuCost(long paramCountMillions) => paramCountMillions switch
    {
        <= SmallModelThreshold  => CpuCostClass.Low,
        <= MediumModelThreshold => CpuCostClass.Medium,
        <= LargeModelThreshold  => CpuCostClass.High,
        _                       => CpuCostClass.VeryHigh
    };

    /// <summary>Classifies GPU cost tier; ONNX format returns <see cref="GpuCostClass.None"/>.</summary>
    internal static GpuCostClass ClassifyGpuCost(long paramCountMillions, ModelFormat format)
    {
        if (format == ModelFormat.ONNX)
            return GpuCostClass.None;

        return paramCountMillions switch
        {
            <= SmallModelThreshold  => GpuCostClass.Low,
            <= MediumModelThreshold => GpuCostClass.Medium,
            _                       => GpuCostClass.High
        };
    }

    /// <summary>Classifies expected inference speed from parameter count and quantization precision.</summary>
    internal static InferenceSpeedClass ClassifyInferenceSpeed(long paramCountMillions, double bytesPerParam)
    {
        if (paramCountMillions <= SmallModelThreshold)
            return bytesPerParam <= BppBoundary ? InferenceSpeedClass.Fast : InferenceSpeedClass.Moderate;

        if (paramCountMillions <= MediumModelThreshold)
            return bytesPerParam <= BppBoundary ? InferenceSpeedClass.Moderate : InferenceSpeedClass.Slow;

        if (paramCountMillions <= LargeModelThreshold)
            return bytesPerParam <= BppBoundary ? InferenceSpeedClass.Slow : InferenceSpeedClass.VerySlow;

        return InferenceSpeedClass.VerySlow;
    }

    /// <summary>Classifies expected output quality tier from parameter count and quantization precision.</summary>
    internal static QualityTier ClassifyQuality(long paramCountMillions, double bytesPerParam)
    {
        if (paramCountMillions <= SmallModelThreshold)
            return QualityTier.Basic;

        if (paramCountMillions <= MediumModelThreshold)
            return bytesPerParam < BppBoundary ? QualityTier.Good : QualityTier.Strong;

        return bytesPerParam < BppBoundary ? QualityTier.Strong : QualityTier.Premium;
    }

    /// <summary>Returns compatible CPU architectures; ONNX is x64-only, others support x64 and ARM64.</summary>
    internal static IReadOnlyList<CompatibleArchitecture> DetermineArchitectures(ModelFormat format) =>
        format == ModelFormat.ONNX
            ? new[] { CompatibleArchitecture.x64 }
            : new[] { CompatibleArchitecture.x64, CompatibleArchitecture.ARM64 };

    /// <summary>Maps model format to the required inference backend runtime.</summary>
    internal static BackendRuntime DetermineRuntime(ModelFormat format) => format switch
    {
        ModelFormat.GGUF          => BackendRuntime.LlamaCpp,
        ModelFormat.OllamaManaged => BackendRuntime.OllamaRuntime,
        ModelFormat.ONNX          => BackendRuntime.OnnxRuntime,
        ModelFormat.SafeTensors   => BackendRuntime.LlamaCpp,
        _                         => BackendRuntime.LlamaCpp
    };

    private static ArchitectureTier GetArchitectureTier(long paramCountMillions) => paramCountMillions switch
    {
        <= SmallModelThreshold  => new ArchitectureTier(26, 32, 128, 8),
        <= MediumModelThreshold => new ArchitectureTier(32, 32, 128, 8),
        <= LargeModelThreshold  => new ArchitectureTier(40, 40, 128, 8),
        _                       => new ArchitectureTier(48, 48, 128, 8)
    };
}
