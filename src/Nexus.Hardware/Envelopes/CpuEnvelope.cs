namespace Nexus.Hardware.Envelopes;

/// <summary>
/// Snapshot of CPU capabilities relevant to local LLM inference, including architecture,
/// parallelism benchmarks, and the maximum safe thread count for model execution.
/// </summary>
/// <param name="CpuArchitectureClass">Processor architecture family (e.g. "x86_64", "Arm64").</param>
/// <param name="CpuParallelismScore">Normalized score (0-1) representing multi-threaded throughput.</param>
/// <param name="CpuSimdScore">Normalized score (0-1) representing SIMD/vector capability.</param>
/// <param name="CpuInferenceScore">Composite score (0-1) estimating LLM inference throughput.</param>
/// <param name="MaxSafeCpuThreads">Maximum thread count recommended for model execution.</param>
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
