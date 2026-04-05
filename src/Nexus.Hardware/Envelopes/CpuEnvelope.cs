namespace Nexus.Hardware.Envelopes;

/// <summary>
/// Snapshot of CPU capabilities relevant to local LLM inference, including architecture,
/// parallelism benchmarks, and the maximum safe thread count for model execution.
/// </summary>
public record CpuEnvelope(
    string CpuArchitectureClass,
    double CpuParallelismScore,
    double CpuSimdScore,
    double CpuInferenceScore,
    int MaxSafeCpuThreads)
{
    /// <summary>
    /// Returns <c>true</c> when the CPU has a positive inference score, indicating it can
    /// perform meaningful LLM computation.
    /// </summary>
    public bool IsViable() => CpuInferenceScore > 0;
}
