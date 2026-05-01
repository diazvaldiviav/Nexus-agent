using System.Reflection;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nexus.Connectors.Catalog;

// ── Internal DTOs — only used by the loader ──────────────────────────────────

/// <summary>Internal DTO; one-to-one with the top-level schema in the bundled catalog YAML files.</summary>
internal sealed class CatalogYamlFile
{
    public string? Server { get; set; }
    public List<CatalogYamlTool>? Tools { get; set; }
}

/// <summary>Internal DTO; one-to-one with each tool entry in the bundled catalog YAML files.</summary>
internal sealed class CatalogYamlTool
{
    public string? Name { get; set; }
    public bool Mutates { get; set; }
    public bool Destructive { get; set; }
    public string? Method { get; set; }
    public CatalogYamlSnapshot? Snapshot { get; set; }
    public bool EmptyPostIsFailure { get; set; }
    public List<string>? SuccessKeywords { get; set; }
    public List<string>? FailureKeywords { get; set; }
    public List<string>? RequiredFields { get; set; }
}

/// <summary>Internal DTO; one-to-one with the <c>snapshot:</c> block in the bundled catalog YAML files.</summary>
internal sealed class CatalogYamlSnapshot
{
    public string? Tool { get; set; }
    public Dictionary<string, string>? ArgsFrom { get; set; }
    public string? Compare { get; set; }
}

// ── Loader ────────────────────────────────────────────────────────────────────

internal static class VerificationCatalogLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads verification rules from embedded YAML resources in the given assembly.
    /// Resources matching "Nexus.Connectors.Catalog.*.yaml" are parsed.
    /// Malformed files are skipped with a warning.
    /// </summary>
    internal static IReadOnlyList<VerificationRule> LoadFromEmbeddedResources(
        Assembly assembly,
        ILogger? logger = null)
    {
        var rules = new List<VerificationRule>();
        const string prefix = "Nexus.Connectors.Catalog.";
        const string suffix = ".yaml";

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var yaml = reader.ReadToEnd();
                var parsed = Deserializer.Deserialize<CatalogYamlFile>(yaml);
                if (parsed is null) continue;

                rules.AddRange(ToVerificationRules(parsed, logger));
                logger?.LogDebug("[VerificationCatalog] Loaded embedded catalog: {Resource}", name);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[VerificationCatalog] Malformed embedded YAML '{Resource}' — skipping", name);
            }
        }

        return rules;
    }

    /// <summary>
    /// Loads verification rules from *.yaml files in the given directory.
    /// Returns an empty list silently if the directory does not exist.
    /// </summary>
    internal static IReadOnlyList<VerificationRule> LoadUserOverrides(
        string catalogDirectory,
        ILogger? logger = null)
    {
        if (!Directory.Exists(catalogDirectory))
            return Array.Empty<VerificationRule>();

        var rules = new List<VerificationRule>();

        foreach (var filePath in Directory.EnumerateFiles(catalogDirectory, "*.yaml"))
        {
            try
            {
                var yaml = File.ReadAllText(filePath);
                var parsed = Deserializer.Deserialize<CatalogYamlFile>(yaml);
                if (parsed is null) continue;

                rules.AddRange(ToVerificationRules(parsed, logger));
                logger?.LogDebug("[VerificationCatalog] Loaded user override catalog: {File}", filePath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[VerificationCatalog] Malformed user YAML '{File}' — skipping", filePath);
            }
        }

        return rules;
    }

    /// <summary>
    /// Merges bundled rules and user overrides into a keyed dictionary.
    /// User overrides win on (server, tool) conflicts.
    /// Keys are (server, tool) both lowercased.
    /// </summary>
    internal static IReadOnlyDictionary<(string Server, string Tool), VerificationRule> Merge(
        IReadOnlyList<VerificationRule> bundled,
        IReadOnlyList<VerificationRule> userOverrides)
    {
        var result = new Dictionary<(string, string), VerificationRule>();

        foreach (var rule in bundled)
        {
            var key = (rule.Server.ToLowerInvariant(), rule.Tool.ToLowerInvariant());
            result[key] = rule;
        }

        foreach (var rule in userOverrides)
        {
            var key = (rule.Server.ToLowerInvariant(), rule.Tool.ToLowerInvariant());
            result[key] = rule;
        }

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IEnumerable<VerificationRule> ToVerificationRules(CatalogYamlFile file, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(file.Server) || file.Tools is null)
            yield break;

        foreach (var tool in file.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                continue;

            var method = ParseMethod(tool.Method, logger);

            if (method == VerificationMethod.SnapshotDiff && tool.Snapshot is null)
            {
                logger?.LogWarning(
                    "[Catalog] tool '{Server}/{Tool}' declares method=snapshot_diff but has no snapshot block — rule skipped",
                    file.Server,
                    tool.Name);
                continue;
            }

            yield return new VerificationRule
            {
                Server = file.Server,
                Tool = tool.Name,
                Mutates = tool.Mutates,
                Destructive = tool.Destructive,
                Method = method,
                Snapshot = tool.Snapshot is null ? null : new SnapshotSpec
                {
                    Tool = tool.Snapshot.Tool ?? "",
                    Args = tool.Snapshot.ArgsFrom ?? new Dictionary<string, string>(),
                    Compare = tool.Snapshot.Compare ?? "not_equal"
                },
                EmptyPostIsFailure = tool.EmptyPostIsFailure,
                SuccessKeywords = tool.SuccessKeywords ?? new List<string>(),
                FailureKeywords = tool.FailureKeywords ?? new List<string>(),
                RequiredFields = tool.RequiredFields ?? new List<string>()
            };
        }
    }

    private const string MethodSnapshotDiff = "snapshot_diff";
    private const string MethodResponseShape = "response_shape";
    private const string MethodResponseKeywords = "response_keywords";

    private static VerificationMethod ParseMethod(string? value, ILogger? logger = null)
    {
        var result = value?.ToLowerInvariant() switch
        {
            MethodSnapshotDiff => VerificationMethod.SnapshotDiff,
            MethodResponseShape => VerificationMethod.ResponseShape,
            MethodResponseKeywords => VerificationMethod.ResponseKeywords,
            _ => VerificationMethod.None
        };

        if (result == VerificationMethod.None && !string.IsNullOrWhiteSpace(value))
        {
            logger?.LogWarning(
                "[Catalog] unknown verification method '{Method}' — falling back to None (rule will be skipped at runtime)",
                value);
        }

        return result;
    }
}
