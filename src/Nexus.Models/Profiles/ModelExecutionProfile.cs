namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Describes the resource requirements and quality characteristics for executing a model locally.
/// </summary>
public record ModelExecutionProfile(
    IReadOnlyList<CompatibleArchitecture> CompatibleArchitectures,
    long EstimatedRamOnLoad,
    long EstimatedRamOnInference,
    long EstimatedVramOnFullOffload,
    long EstimatedVramOnPartialOffload,
    CpuCostClass CpuCostClass,
    GpuCostClass GpuCostClass,
    InferenceSpeedClass InferenceSpeedClass,
    QualityTier QualityTier,
    BackendRuntime RequiredRuntime);
