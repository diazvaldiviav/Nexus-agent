namespace Nexus.Models.Enums;

/// <summary>
/// User interaction style preference influencing model selection and configuration.
/// </summary>
public enum InteractionPreference
{
    /// <summary>Prioritizes fast time-to-first-token.</summary>
    LowLatency,
    /// <summary>Balances speed and response quality.</summary>
    Balanced,
    /// <summary>Prioritizes thorough, chain-of-thought responses.</summary>
    DeepReasoning,
    /// <summary>Optimized for high-throughput batch operations.</summary>
    BatchProcessing
}
