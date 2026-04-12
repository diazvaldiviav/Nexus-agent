namespace Nexus.Models;

using Nexus.Models.Enums;
using Nexus.Models.Profiles;

/// <summary>
/// Represents a candidate LLM model with its identity, capabilities, and distribution metadata.
/// </summary>
/// <param name="Id">Unique identifier for this model variant (e.g. "llama3.2-3b-q4km").</param>
/// <param name="Family">Model family name (e.g. "Llama", "Mistral").</param>
/// <param name="Variant">Specific variant within the family (e.g. "3.2-3B").</param>
/// <param name="Quantization">Weight quantization scheme (e.g. "Q4_K_M", "FP16").</param>
/// <param name="Format">Serialization format of the model weights file.</param>
/// <param name="ParameterCount">Total parameter count in millions.</param>
/// <param name="EstimatedWeightSize">Estimated on-disk weight file size in bytes.</param>
/// <param name="ContextWindowSize">Maximum context window length in tokens.</param>
/// <param name="SupportedBackends">Inference runtime backends that can load this model.</param>
/// <param name="TaskFit">Task categories this model is suited for.</param>
/// <param name="LanguageSupport">ISO language codes the model supports.</param>
/// <param name="DistributionProfile">Download and installation metadata for this model.</param>
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
    /// <inheritdoc/>
    public override string ToString() =>
        $"{Family} {Variant} [{Quantization}] ({ParameterCount}M params, {Format})";
}
