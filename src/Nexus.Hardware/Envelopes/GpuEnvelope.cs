using Nexus.Hardware.States;

namespace Nexus.Hardware.Envelopes;

/// <summary>
/// Snapshot of GPU/VRAM capabilities for LLM inference offloading, including available VRAM,
/// safe budgets, pressure level, and offload feasibility flags.
/// </summary>
/// <param name="UsableLocalVramNow">Currently available VRAM in bytes.</param>
/// <param name="SafeGpuBudget">Maximum VRAM bytes safely allocatable for model layers.</param>
/// <param name="GpuPressureLevel">Classified VRAM pressure level.</param>
/// <param name="GpuOffloadCapacity">VRAM bytes available for GPU layer offloading.</param>
/// <param name="CanFullOffload">Whether the GPU can host all model layers.</param>
/// <param name="CanPartialOffload">Whether the GPU can host at least some model layers.</param>
public record GpuEnvelope(
    long UsableLocalVramNow,
    long SafeGpuBudget,
    PressureLevel GpuPressureLevel,
    long GpuOffloadCapacity,
    bool CanFullOffload,
    bool CanPartialOffload)
{
    /// <summary>
    /// Always returns <c>true</c> because GPU is optional for LLM inference — CPU-only execution is valid.
    /// Unlike <see cref="CpuEnvelope.IsViable"/> and <see cref="RamEnvelope.IsViable"/>, a zeroed GPU
    /// (e.g. from <see cref="NoGpu"/>) is still a viable hardware configuration.
    /// </summary>
    public bool IsViable() => true;

    /// <summary>
    /// Creates a zeroed-out envelope representing a system with no discrete GPU.
    /// </summary>
    public static GpuEnvelope NoGpu() => new(
        UsableLocalVramNow: 0,
        SafeGpuBudget: 0,
        GpuPressureLevel: PressureLevel.None,
        GpuOffloadCapacity: 0,
        CanFullOffload: false,
        CanPartialOffload: false);
}
