using Nexus.Hardware.Envelopes;
using Nexus.Hardware.States;

namespace Nexus.Hardware;

/// <summary>
/// Classifies hardware envelopes into discrete capability states using fixed thresholds.
/// Pure static logic with no dependencies — deterministic classification for model recommendation.
/// </summary>
public static class HostStateClassifier
{
    internal const double CpuWeakThreshold = 0.25;
    internal const double CpuModerateThreshold = 0.50;
    internal const double CpuStrongThreshold = 0.75;

    internal const long RamTightThreshold = 4_000_000_000L;
    internal const long RamAdequateThreshold = 8_000_000_000L;
    internal const long RamComfortableThreshold = 16_000_000_000L;

    internal const long GpuLimitedThreshold = 4_000_000_000L;
    internal const long GpuCapableThreshold = 8_000_000_000L;

    /// <summary>
    /// Classifies CPU capability based on the inference score from the envelope.
    /// </summary>
    /// <param name="envelope">The CPU envelope containing inference benchmarks.</param>
    /// <returns>A <see cref="CpuState"/> representing the CPU's classification tier.</returns>
    public static CpuState ClassifyCpu(CpuEnvelope envelope) => envelope.CpuInferenceScore switch
    {
        < CpuWeakThreshold => CpuState.Weak,
        < CpuModerateThreshold => CpuState.Moderate,
        < CpuStrongThreshold => CpuState.Strong,
        _ => CpuState.HighEnd
    };

    /// <summary>
    /// Classifies RAM capability based on the safe model RAM budget from the envelope.
    /// </summary>
    /// <param name="envelope">The RAM envelope containing memory budget calculations.</param>
    /// <returns>A <see cref="RamState"/> representing the RAM's classification tier.</returns>
    public static RamState ClassifyRam(RamEnvelope envelope) => envelope.SafeModelRamBudget switch
    {
        < RamTightThreshold => RamState.Tight,
        < RamAdequateThreshold => RamState.Adequate,
        < RamComfortableThreshold => RamState.Comfortable,
        _ => RamState.Abundant
    };

    /// <summary>
    /// Classifies GPU capability based on the safe GPU budget from the envelope.
    /// </summary>
    /// <param name="envelope">The GPU envelope containing VRAM budget calculations.</param>
    /// <returns>A <see cref="GpuState"/> representing the GPU's classification tier.</returns>
    public static GpuState ClassifyGpu(GpuEnvelope envelope) => envelope.SafeGpuBudget switch
    {
        <= 0 => GpuState.None,
        < GpuLimitedThreshold => GpuState.Limited,
        < GpuCapableThreshold => GpuState.Capable,
        _ => GpuState.Strong
    };
}
