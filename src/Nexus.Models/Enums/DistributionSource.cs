namespace Nexus.Models.Enums;

/// <summary>
/// Flags enum representing one or more distribution sources from which a model can be obtained.
/// </summary>
[Flags]
public enum DistributionSource
{
    /// <summary>Ollama model registry.</summary>
    Ollama = 1,
    /// <summary>Hugging Face model hub.</summary>
    HuggingFace = 2
}
