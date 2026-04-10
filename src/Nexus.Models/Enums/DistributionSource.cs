namespace Nexus.Models.Enums;

/// <summary>
/// Flags enum representing one or more distribution sources from which a model can be obtained.
/// </summary>
[Flags]
public enum DistributionSource
{
    Ollama = 1,
    HuggingFace = 2
}
