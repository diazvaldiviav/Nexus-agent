namespace Nexus.Models.Enums;

/// <summary>
/// Expected output quality tier of the model, from basic capability to premium performance.
/// </summary>
public enum QualityTier
{
    /// <summary>Functional but limited output quality.</summary>
    Basic,
    /// <summary>Solid output quality for most tasks.</summary>
    Good,
    /// <summary>High output quality with nuanced understanding.</summary>
    Strong,
    /// <summary>Best-in-class output quality.</summary>
    Premium
}
