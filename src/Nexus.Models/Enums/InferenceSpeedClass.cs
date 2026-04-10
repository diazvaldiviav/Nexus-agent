namespace Nexus.Models.Enums;

/// <summary>
/// Expected token generation throughput classification for the model on typical hardware.
/// </summary>
public enum InferenceSpeedClass
{
    Fast,
    Moderate,
    Slow,
    VerySlow
}
