namespace Nexus.Models.Enums;

/// <summary>
/// Classification of typical prompt length submitted to the model.
/// </summary>
public enum PromptLength
{
    /// <summary>Prompts under ~256 tokens.</summary>
    Short,
    /// <summary>Prompts of ~256-1024 tokens.</summary>
    Medium,
    /// <summary>Prompts of ~1024-4096 tokens.</summary>
    Long,
    /// <summary>Prompts exceeding ~4096 tokens.</summary>
    VeryLong
}
