using Nexus.Hardware.States;

namespace Nexus.Hardware.Envelopes;

/// <summary>
/// Snapshot of system RAM availability and budgets for local LLM model loading and inference,
/// including current usable memory and pressure classification.
/// </summary>
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
