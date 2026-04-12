namespace Nexus.Models.Enums;

/// <summary>
/// Classification of typical response length expected from the model.
/// </summary>
public enum ResponseLength
{
    /// <summary>Responses under ~128 tokens.</summary>
    Short,
    /// <summary>Responses of ~128-512 tokens.</summary>
    Medium,
    /// <summary>Responses of ~512-2048 tokens.</summary>
    Long,
    /// <summary>Responses exceeding ~2048 tokens.</summary>
    VeryLong
}
