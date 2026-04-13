namespace Nexus.Models.Enums;

/// <summary>
/// Expected token generation throughput classification for the model on typical hardware.
/// </summary>
public enum InferenceSpeedClass
{
    /// <summary>High token throughput; near-instant responses.</summary>
    Fast,
    /// <summary>Acceptable token throughput for interactive use.</summary>
    Moderate,
    /// <summary>Reduced throughput; noticeable latency per response.</summary>
    Slow,
    /// <summary>Very low throughput; best for batch or offline use.</summary>
    VerySlow
}
