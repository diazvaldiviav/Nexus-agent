namespace Nexus.Hardware.States;

/// <summary>
/// Overall feasibility assessment for running a specific model on the current hardware.
/// </summary>
public enum FeasibilityResult
{
    Rejected,
    FeasibleWithCaution,
    Feasible,
    Optimal
}
