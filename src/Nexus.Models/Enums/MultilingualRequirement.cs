namespace Nexus.Models.Enums;

/// <summary>
/// Degree of multilingual capability required from the model.
/// </summary>
public enum MultilingualRequirement
{
    /// <summary>Single-language (typically English) is sufficient.</summary>
    None,
    /// <summary>Limited multilingual support for common languages.</summary>
    Basic,
    /// <summary>Robust multilingual support across many languages.</summary>
    Strong
}
