using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Connectors.Catalog;

/// <summary>
/// Loads and serves verification rules from bundled YAML resources and optional
/// user overrides from ~/.nexus/catalog/*.yaml.
/// </summary>
public sealed class VerificationCatalog : IVerificationCatalog
{
    private readonly Dictionary<(string Server, string Tool), VerificationRule> _rules;
    private readonly ILogger<VerificationCatalog>? _logger;

    public VerificationCatalog(NexusConfig config, ILogger<VerificationCatalog>? logger = null)
        : this(config, logger, overrideDir: null)
    {
    }

    /// <summary>
    /// Internal overload allowing tests to inject a custom catalog directory
    /// instead of the default ~/.nexus/catalog path.
    /// </summary>
    internal VerificationCatalog(
        NexusConfig config,
        ILogger<VerificationCatalog>? logger,
        string? overrideDir)
    {
        _ = config; // Reserved for future per-config filtering
        _logger = logger;

        var assembly = typeof(VerificationCatalog).Assembly;
        var bundled = VerificationCatalogLoader.LoadFromEmbeddedResources(assembly, _logger);

        var userDir = overrideDir ?? ExpandUserCatalogDir();
        var userOverrides = VerificationCatalogLoader.LoadUserOverrides(userDir, _logger);

        var bundledCount = bundled.Count;
        var overrideCount = userOverrides.Count;

        _rules = VerificationCatalogLoader.Merge(bundled, userOverrides)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        _logger?.LogInformation(
            "[Catalog] loaded {BundledCount}+{OverrideCount} rules from bundled+override sources ({TotalRules} effective)",
            bundledCount, overrideCount, _rules.Count);
    }

    /// <inheritdoc />
    public int Count => _rules.Count;

    /// <inheritdoc />
    public VerificationRule? GetRule(string server, string tool) =>
        _rules.TryGetValue((server.ToLowerInvariant(), tool.ToLowerInvariant()), out var rule)
            ? rule
            : null;

    /// <summary>Relative path (from user home) to the default user catalog directory.</summary>
    private const string DefaultUserCatalogDir = ".nexus/catalog";

    /// <summary>
    /// Expands the default user catalog path to an absolute directory path using
    /// <see cref="Environment.SpecialFolder.UserProfile"/> (~/.nexus/catalog on Unix,
    /// %USERPROFILE%\.nexus\catalog on Windows). When the home directory is unavailable
    /// (returns an empty string), <see cref="Path.Combine"/> produces a relative path
    /// that will silently yield no rules because <see cref="Directory.Exists"/> returns false.
    /// </summary>
    private static string ExpandUserCatalogDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Path segments mirror DefaultUserCatalogDir; kept as Path.Combine for cross-platform correctness.
        return Path.Combine(home, ".nexus", "catalog");
    }
}
