namespace Nexus.Models.Enums;

/// <summary>
/// CPU architecture families compatible with the model's runtime backend.
/// </summary>
public enum CompatibleArchitecture
{
    /// <summary>64-bit x86 architecture (Intel/AMD).</summary>
    x64,
    /// <summary>64-bit ARM architecture (Apple Silicon, Snapdragon).</summary>
    ARM64
}
