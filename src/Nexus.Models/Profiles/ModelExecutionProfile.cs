namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Describes the resource requirements and quality characteristics for executing a model locally.
/// </summary>
/// <param name="CompatibleArchitectures">CPU architectures that can execute this model.</param>
/// <param name="EstimatedRamOnLoad">Estimated system RAM in bytes required to load the model.</param>
/// <param name="EstimatedRamOnInference">Estimated system RAM in bytes during active inference.</param>
/// <param name="EstimatedVramOnFullOffload">Estimated GPU VRAM in bytes for full GPU offload.</param>
/// <param name="EstimatedVramOnPartialOffload">Estimated GPU VRAM in bytes for partial GPU offload.</param>
/// <param name="CpuCostClass">Relative CPU cost during inference.</param>
/// <param name="GpuCostClass">Relative GPU cost during inference.</param>
/// <param name="InferenceSpeedClass">Expected token generation throughput classification.</param>
/// <param name="QualityTier">Expected output quality tier of the model.</param>
/// <param name="RequiredRuntime">Inference backend runtime required to execute the model.</param>
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
