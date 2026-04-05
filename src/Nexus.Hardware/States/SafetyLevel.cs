namespace Nexus.Hardware.States;

/// <summary>
/// Resource safety margin when running a model, indicating risk of OOM or thermal throttling.
/// </summary>
public enum SafetyLevel
{
    Unsafe,
    Caution,
    Safe,
    Comfortable
}
