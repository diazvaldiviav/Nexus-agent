namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Describes how a model is obtained: download sources, identifiers, size, and install complexity.
/// </summary>
/// <param name="AvailableSources">Flags indicating all sources this model can be downloaded from.</param>
/// <param name="PreferredSource">The recommended download source.</param>
/// <param name="OllamaModelTag">Ollama registry tag, or <see langword="null"/> if not available via Ollama.</param>
/// <param name="HuggingFaceRepoId">Hugging Face repository identifier, or <see langword="null"/> if not available.</param>
/// <param name="HuggingFaceFilename">Filename within the Hugging Face repository, or <see langword="null"/>.</param>
/// <param name="EstimatedDownloadSize">Estimated download size in bytes.</param>
/// <param name="CanBeManagedByRuntime">Whether the runtime can handle download and lifecycle automatically.</param>
/// <param name="InstallComplexity">Complexity level for installing and configuring the model.</param>
public record DistributionProfile(
    DistributionSource AvailableSources,
    DistributionSource PreferredSource,
    string? OllamaModelTag,
    string? HuggingFaceRepoId,
    string? HuggingFaceFilename,
    long EstimatedDownloadSize,
    bool CanBeManagedByRuntime,
    InstallComplexity InstallComplexity);
