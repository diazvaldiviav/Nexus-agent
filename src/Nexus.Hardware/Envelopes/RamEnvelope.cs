using Nexus.Hardware.States;

namespace Nexus.Hardware.Envelopes;

/// <summary>
/// Snapshot of system RAM availability and budgets for local LLM model loading and inference,
/// including current usable memory and pressure classification.
/// </summary>
/// <param name="UsableRamNow">Currently available physical RAM in bytes.</param>
/// <param name="SafeModelRamBudget">Maximum bytes safely allocatable for model loading.</param>
/// <param name="SafeInferenceRamBudget">Maximum bytes safely allocatable during inference.</param>
/// <param name="RamPressureLevel">Classified memory pressure level.</param>
public record RamEnvelope(
    long UsableRamNow,
    long SafeModelRamBudget,
    long SafeInferenceRamBudget,
    PressureLevel RamPressureLevel)
{
    /// <summary>
    /// Returns <c>true</c> when there is a positive RAM budget available for model loading.
    /// </summary>
    public bool IsViable() => SafeModelRamBudget > 0;
}
