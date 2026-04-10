namespace Nexus.Models;

using Nexus.Models.Enums;
using Nexus.Models.Profiles;

/// <summary>
/// Represents a candidate LLM model with its identity, capabilities, and distribution metadata.
/// </summary>
public record ModelCandidate(
    string Id,
    string Family,
    string Variant,
    string Quantization,
    ModelFormat Format,
    long ParameterCount,
    long EstimatedWeightSize,
    int ContextWindowSize,
    IReadOnlyList<BackendRuntime> SupportedBackends,
    IReadOnlyList<ModelTaskFit> TaskFit,
    IReadOnlyList<string> LanguageSupport,
    DistributionProfile DistributionProfile)
{
    public override string ToString() =>
        $"{Family} {Variant} [{Quantization}] ({ParameterCount}M params, {Format})";
}
