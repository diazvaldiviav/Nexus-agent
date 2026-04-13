namespace Nexus.Models.Enums;

/// <summary>
/// User output optimization preference, trading off speed, quality, and stability.
/// </summary>
public enum OutputPreference
{
    /// <summary>Optimize for fastest possible output generation.</summary>
    MaxSpeed,
    /// <summary>Balance speed and output quality.</summary>
    Balanced,
    /// <summary>Optimize for highest quality output regardless of speed.</summary>
    MaxQuality,
    /// <summary>Optimize for consistent, deterministic output.</summary>
    MaxStability
}
