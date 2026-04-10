namespace Nexus.Models.Profiles;

using Nexus.Models.Enums;

/// <summary>
/// Describes how a model is obtained: download sources, identifiers, size, and install complexity.
/// </summary>
public record DistributionProfile(
    DistributionSource AvailableSources,
    DistributionSource PreferredSource,
    string? OllamaModelTag,
    string? HuggingFaceRepoId,
    string? HuggingFaceFilename,
    long EstimatedDownloadSize,
    bool CanBeManagedByRuntime,
    InstallComplexity InstallComplexity);
