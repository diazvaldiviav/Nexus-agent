using System.Collections.Concurrent;
using System.Text.Json;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Connectors;

/// <summary>
/// Validates path arguments produced by LLMs against the filesystem.
/// Normalizes paths, checks existence, and fuzzy-matches against allowed
/// directories when a path does not exist. Thread-safe via ConcurrentDictionary cache.
/// </summary>
public sealed class PathValidator : IToolArgumentValidator
{
    private readonly IReadOnlyList<string> _allowedDirectories;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<PathValidator>? _logger;
    private readonly int _fuzzyThreshold;
    private readonly TimeSpan _cacheTtl;

    // Key: allowed root dir → (expiry UTC, catalog entries under that root)
    private readonly ConcurrentDictionary<string, (DateTime Expiry, List<CatalogEntry> Entries)> _catalogCache = new();

    private static readonly HashSet<string> PathKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "directory", "dir", "file", "source", "destination",
        "src", "dest", "target", "output", "input", "folder"
    };

    private static readonly HashSet<string> DestinationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "destination", "dest", "output", "target"
    };

    public PathValidator(
        NexusConfig config,
        ToolRegistry toolRegistry,
        ILogger<PathValidator>? logger = null,
        int fuzzyThreshold = 80,
        TimeSpan? cacheTtl = null)
    {
        _toolRegistry = toolRegistry;
        _logger = logger;
        _fuzzyThreshold = fuzzyThreshold;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(60);
        _allowedDirectories = ExtractAllowedDirectories(config.Mcp.Servers);
    }

    public async Task<ValidationOutcome> ValidateAsync(
        string toolName,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments is null || arguments.Count == 0)
            return ValidationOutcome.Ok(arguments);

        if (_allowedDirectories.Count == 0)
            return ValidationOutcome.Ok(arguments);

        var corrected = new Dictionary<string, object>(arguments, StringComparer.Ordinal);
        var corrections = new List<string>();

        var pathKeys = IdentifyPathParameters(toolName, arguments);

        foreach (var key in pathKeys)
        {
            if (!corrected.TryGetValue(key, out var rawValue))
                continue;

            // Handle array of paths (e.g. read_multiple_files)
            if (rawValue is JsonElement jsonEl && jsonEl.ValueKind == JsonValueKind.Array)
            {
                var correctedPaths = new List<string>();
                foreach (var item in jsonEl.EnumerateArray())
                {
                    var p = item.GetString();
                    if (p is null) continue;

                    var r = await ValidateSinglePathAsync(p, toolName, DestinationKeys.Contains(key), cancellationToken);
                    if (!r.IsValid)
                        return ValidationOutcome.Fail(r.ErrorMessage!);

                    correctedPaths.Add(r.CorrectedPath!);
                    if (r.WasCorrected)
                        corrections.Add($"{key}[]: '{p}' → '{r.CorrectedPath}'");
                }

                var json = JsonSerializer.Serialize(correctedPaths);
                corrected[key] = JsonDocument.Parse(json).RootElement.Clone();
                continue;
            }

            // Single string path
            if (rawValue is not string pathStr)
                continue;

            var result = await ValidateSinglePathAsync(pathStr, toolName, DestinationKeys.Contains(key), cancellationToken);
            if (!result.IsValid)
                return ValidationOutcome.Fail(result.ErrorMessage!);

            corrected[key] = result.CorrectedPath!;
            if (result.WasCorrected)
                corrections.Add($"{key}: '{pathStr}' → '{result.CorrectedPath}'");
        }

        // Cross-argument semantic resolution:
        // If destination is a directory and source has a filename → append filename
        ResolveDestinationDirectory(corrected, corrections);

        if (corrections.Count > 0)
        {
            var note = "Path(s) corrected:\n" + string.Join("\n", corrections);
            _logger?.LogInformation("[PathValidator] {Note}", note);
            return ValidationOutcome.Corrected(corrected, note);
        }

        return ValidationOutcome.Ok(corrected);
    }

    // ── Cross-argument semantic resolution ──────────────────────

    private static readonly (string Source, string Destination)[] SourceDestPairs =
    {
        ("source", "destination"),
        ("src", "dest"),
        ("source", "dest"),
        ("input", "output"),
    };

    private static void ResolveDestinationDirectory(
        Dictionary<string, object> args, List<string> corrections)
    {
        foreach (var (srcKey, dstKey) in SourceDestPairs)
        {
            if (args.TryGetValue(srcKey, out var srcVal) && srcVal is string srcPath &&
                args.TryGetValue(dstKey, out var dstVal) && dstVal is string dstPath)
            {
                if (Directory.Exists(dstPath) && !string.IsNullOrEmpty(Path.GetFileName(srcPath)))
                {
                    var fileName = Path.GetFileName(srcPath);
                    var resolved = Path.Combine(dstPath, fileName);
                    args[dstKey] = resolved;
                    corrections.Add($"{dstKey}: '{dstPath}' → '{resolved}' (appended filename from {srcKey})");
                }
            }
        }
    }

    // ── Single path validation pipeline ──────────────────────────

    private record PathCheckResult(
        bool IsValid,
        string? CorrectedPath,
        bool WasCorrected,
        string? ErrorMessage);

    internal record CatalogEntry(string FullPath, string Name, bool IsDirectory);

    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "create_directory", "move_file", "copy_file"
    };

    private async Task<PathCheckResult> ValidateSinglePathAsync(
        string rawPath, string toolName, bool isDestination, CancellationToken ct)
    {
        // 1. Normalize
        var normalized = NormalizePath(rawPath);

        // 2. Fast path: exact existing path inside allowed roots
        if (IsWithinAllowedDirectory(normalized) &&
            (File.Exists(normalized) || Directory.Exists(normalized)))
            return new PathCheckResult(true, normalized, normalized != rawPath, null);

        // 3. Build real filesystem catalog (dirs + files)
        var catalog = await GetCatalogAsync(ct);

        // 4. Find best match in catalog
        var match = FindBestMatch(normalized, catalog);
        if (match is not null)
            return new PathCheckResult(true, match, true, null);

        // 5. For destination/write tools allow non-existing leaf if parent can be matched
        if (isDestination || WriteTools.Contains(toolName))
        {
            var parentMatch = FindBestMatchForParent(normalized, catalog);
            if (parentMatch is not null)
            {
                var fileName = Path.GetFileName(normalized);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var newPath = Path.Combine(parentMatch, fileName);
                    return new PathCheckResult(true, newPath, true, null);
                }
            }
        }

        // 6. Reject with suggestions
        return new PathCheckResult(false, null, false,
            $"Path '{rawPath}' not found. Did you mean:\n{GetSuggestions(normalized, catalog)}");
    }

    // ── Path normalization ────────────────────────────────────────

    internal static string NormalizePath(string raw)
    {
        var p = raw.Trim();
        p = p.Replace('/', Path.DirectorySeparatorChar)
             .Replace('\\', Path.DirectorySeparatorChar);

        try { p = Path.GetFullPath(p); }
        catch { /* malformed — keep as-is */ }

        // Strip trailing separator (except root like "C:\")
        p = p.TrimEnd(Path.DirectorySeparatorChar);
        if (p.Length == 2 && p[1] == ':')
            p += Path.DirectorySeparatorChar;

        return p;
    }

    private bool IsWithinAllowedDirectory(string path)
    {
        foreach (var allowed in _allowedDirectories)
        {
            var normalizedAllowed = NormalizePath(allowed);
            if (path.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ── Catalog-based matching ────────────────────────────────────

    internal string? FindBestMatch(string proposedPath, List<CatalogEntry> catalog)
    {
        if (catalog.Count == 0)
            return null;

        var proposedName = Path.GetFileName(proposedPath);
        if (string.IsNullOrWhiteSpace(proposedName))
            return null;

        var exactMatches = catalog
            .Where(e => string.Equals(e.Name, proposedName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count > 0)
            return SelectBestByFullPath(proposedPath, exactMatches)?.FullPath;

        var fuzzyMatches = new List<CatalogEntry>();
        foreach (var entry in catalog)
        {
            var score = Fuzz.Ratio(
                proposedName.ToLowerInvariant(),
                entry.Name.ToLowerInvariant());
            if (score >= _fuzzyThreshold)
                fuzzyMatches.Add(entry);
        }

        if (fuzzyMatches.Count == 0)
            return null;

        return SelectBestByFullPath(proposedPath, fuzzyMatches)?.FullPath;
    }

    internal string? FindBestMatchForParent(string proposedPath, List<CatalogEntry> catalog)
    {
        var parent = Path.GetDirectoryName(proposedPath);
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        var dirCatalog = catalog.Where(c => c.IsDirectory).ToList();
        return FindBestMatch(parent, dirCatalog);
    }

    private CatalogEntry? SelectBestByFullPath(string proposedPath, List<CatalogEntry> matches)
    {
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        CatalogEntry? best = null;
        var bestScore = int.MinValue;
        foreach (var match in matches)
        {
            var score = Fuzz.Ratio(
                proposedPath.ToLowerInvariant(),
                match.FullPath.ToLowerInvariant());
            if (score > bestScore)
            {
                bestScore = score;
                best = match;
            }
        }

        return best;
    }

    internal string GetSuggestions(string proposedPath, List<CatalogEntry> catalog)
    {
        if (catalog.Count == 0)
            return "  - no indexed paths available";

        var suggestions = catalog
            .Select(e => new
            {
                Entry = e,
                Score = Fuzz.Ratio(
                    proposedPath.ToLowerInvariant(),
                    e.FullPath.ToLowerInvariant())
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (suggestions.Count == 0)
            return "  - no close paths found";

        return string.Join("\n", suggestions.Select(s =>
            $"  - {s.Entry.FullPath} (score: {s.Score})"));
    }

    // ── Catalog cache ─────────────────────────────────────────────

    private async Task<List<CatalogEntry>> GetCatalogAsync(CancellationToken ct)
    {
        var all = new List<CatalogEntry>();

        foreach (var root in _allowedDirectories)
        {
            var normalizedRoot = NormalizePath(root);
            var now = DateTime.UtcNow;

            if (_catalogCache.TryGetValue(normalizedRoot, out var cached) && cached.Expiry > now)
            {
                all.AddRange(cached.Entries);
                continue;
            }

            var entries = await Task.Run(() => ScanCatalogRecursive(normalizedRoot), ct);
            _catalogCache[normalizedRoot] = (now.Add(_cacheTtl), entries);
            all.AddRange(entries);
        }

        return all;
    }

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "node_modules", "bin", "obj"
    };

    private static List<CatalogEntry> ScanCatalogRecursive(string root)
    {
        var result = new List<CatalogEntry>();
        try
        {
            if (!Directory.Exists(root))
                return result;

            result.Add(new CatalogEntry(root, Path.GetFileName(root), true));
            ScanRecursive(root, result, 0, 5);
        }
        catch { /* permission errors — return what we have */ }
        return result;
    }

    private static void ScanRecursive(string dir, List<CatalogEntry> result, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var fileName = Path.GetFileName(file);
                result.Add(new CatalogEntry(file, fileName, false));
            }

            if (depth == maxDepth) return;

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkipDirectories.Contains(name)) continue;

                result.Add(new CatalogEntry(sub, name, true));
                ScanRecursive(sub, result, depth + 1, maxDepth);
            }
        }
        catch { /* skip inaccessible */ }
    }

    // ── Allowed directory extraction ──────────────────────────────

    internal static IReadOnlyList<string> ExtractAllowedDirectories(IEnumerable<McpServerEntry> servers)
    {
        var dirs = new List<string>();
        foreach (var server in servers)
        {
            var fsIndex = server.Args.FindIndex(
                a => a.Contains("server-filesystem", StringComparison.OrdinalIgnoreCase));

            if (fsIndex < 0) continue;

            for (int i = fsIndex + 1; i < server.Args.Count; i++)
            {
                var arg = server.Args[i].Trim();
                if (!string.IsNullOrEmpty(arg))
                    dirs.Add(arg);
            }
        }
        return dirs;
    }

    // ── Path parameter identification ─────────────────────────────

    internal HashSet<string> IdentifyPathParameters(
        string toolName, Dictionary<string, object> arguments)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From tool schema
        var tool = _toolRegistry.GetTool(toolName);
        if (tool?.InputSchema.HasValue == true)
        {
            var schema = tool.InputSchema.Value;
            if (schema.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateObject())
                {
                    var desc = prop.Value.TryGetProperty("description", out var d)
                        ? (d.GetString() ?? "") : "";

                    if (IsPathLikeName(prop.Name) || IsPathLikeDescription(desc))
                        result.Add(prop.Name);
                }
            }
        }

        // Fallback: name heuristic on argument keys
        foreach (var key in arguments.Keys)
        {
            if (IsPathLikeName(key))
                result.Add(key);
        }

        return result;
    }

    private static bool IsPathLikeName(string name) =>
        PathKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));

    private static bool IsPathLikeDescription(string desc) =>
        desc.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("file", StringComparison.OrdinalIgnoreCase) ||
        desc.Contains("directory", StringComparison.OrdinalIgnoreCase);
}
