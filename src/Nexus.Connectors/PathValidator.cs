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

    // Key: allowed root dir → (expiry UTC, flat list of known sub-dirs)
    private readonly ConcurrentDictionary<string, (DateTime Expiry, List<string> Dirs)> _dirCache = new();

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

    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "create_directory", "move_file", "copy_file"
    };

    private async Task<PathCheckResult> ValidateSinglePathAsync(
        string rawPath, string toolName, bool isDestination, CancellationToken ct)
    {
        // 1. Normalize
        var normalized = NormalizePath(rawPath);

        // 2. Allowed-directory guard
        if (!IsWithinAllowedDirectory(normalized))
        {
            var repaired = TryRepairRoot(normalized);
            if (repaired is null)
            {
                return new PathCheckResult(false, null, false,
                    $"Path '{rawPath}' is outside allowed directories. " +
                    $"Allowed: {string.Join(", ", _allowedDirectories)}. " +
                    $"Use paths within these directories.");
            }
            normalized = repaired;
        }

        // 3. Existence check — if it exists, we're done
        if (File.Exists(normalized) || Directory.Exists(normalized))
            return new PathCheckResult(true, normalized, normalized != rawPath, null);

        // ── Destination path: no fuzzy match, no recursive search ──
        // The user says WHERE to put something — just validate the parent exists
        if (isDestination)
        {
            var parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return new PathCheckResult(true, normalized, normalized != rawPath, null);

            return new PathCheckResult(false, null, false,
                $"Destination path '{rawPath}' — parent directory does not exist. " +
                $"Available directories: {string.Join(", ", _allowedDirectories)}");
        }

        // ── Source path: fuzzy match + recursive search ──

        // 4. Fuzzy match — only accept if the result actually exists
        var fuzzyResult = await TryFuzzyMatchAsync(normalized, ct);
        if (fuzzyResult is not null && (File.Exists(fuzzyResult) || Directory.Exists(fuzzyResult)))
            return new PathCheckResult(true, fuzzyResult, true, null);

        // 5. Recursive file search — find by filename anywhere in allowed dirs
        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrEmpty(fileName))
        {
            var found = TryFindFileRecursive(fileName);
            if (found is not null)
                return new PathCheckResult(true, found, true, null);
        }

        // 6. Write operations: allow new path if parent directory exists
        if (WriteTools.Contains(toolName))
        {
            var parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return new PathCheckResult(true, normalized, normalized != rawPath, null);
        }

        return new PathCheckResult(false, null, false,
            $"Path '{rawPath}' does not exist and no close match found. " +
            $"Available directories: {string.Join(", ", _allowedDirectories)}");
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

    // ── Root repair ───────────────────────────────────────────────
    // e.g. D:\Nova Tech\Nexus\scrum.md → D:\Nova Tech\Nexus\Nexus-agent\scrum.md

    internal string? TryRepairRoot(string path)
    {
        foreach (var allowed in _allowedDirectories)
        {
            var normalizedAllowed = NormalizePath(allowed);
            var relative = StripCommonRoot(path, normalizedAllowed);
            if (relative is null) continue;

            var candidate = Path.Combine(normalizedAllowed, relative);
            try { candidate = Path.GetFullPath(candidate); } catch { continue; }

            if (IsWithinAllowedDirectory(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Given "D:\Nova Tech\Nexus\file.md" and allowed "D:\Nova Tech\Nexus\Nexus-agent",
    /// finds the common prefix ("D:\Nova Tech\Nexus") and returns the remainder ("file.md").
    /// </summary>
    internal static string? StripCommonRoot(string path, string allowedRoot)
    {
        var pathParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var allowedParts = allowedRoot.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        int common = 0;
        for (int i = 0; i < Math.Min(pathParts.Length, allowedParts.Length); i++)
        {
            if (string.Equals(pathParts[i], allowedParts[i], StringComparison.OrdinalIgnoreCase))
                common++;
            else break;
        }

        if (common == 0) return null;

        var remaining = pathParts.Skip(common).ToArray();
        return remaining.Length > 0
            ? string.Join(Path.DirectorySeparatorChar.ToString(), remaining)
            : null;
    }

    // ── Fuzzy directory matching ──────────────────────────────────

    private async Task<string?> TryFuzzyMatchAsync(string path, CancellationToken ct)
    {
        var dirPart = Path.GetDirectoryName(path);
        var filePart = Path.GetFileName(path);

        if (string.IsNullOrEmpty(dirPart))
            return null;

        var knownDirs = await GetKnownDirectoriesAsync(ct);
        if (knownDirs.Count == 0)
            return null;

        string? bestDir = null;
        int bestScore = 0;

        foreach (var known in knownDirs)
        {
            var score = Fuzz.Ratio(
                dirPart.ToLowerInvariant(),
                known.ToLowerInvariant());

            if (score > bestScore && score >= _fuzzyThreshold)
            {
                bestScore = score;
                bestDir = known;
            }
        }

        if (bestDir is null)
            return null;

        var candidate = string.IsNullOrEmpty(filePart)
            ? bestDir
            : Path.Combine(bestDir, filePart);

        _logger?.LogDebug(
            "[PathValidator] Fuzzy: '{Original}' → '{Corrected}' (score {Score})",
            dirPart, bestDir, bestScore);

        return candidate;
    }

    // ── Directory cache ───────────────────────────────────────────

    private async Task<List<string>> GetKnownDirectoriesAsync(CancellationToken ct)
    {
        var all = new List<string>();

        foreach (var root in _allowedDirectories)
        {
            var normalizedRoot = NormalizePath(root);
            var now = DateTime.UtcNow;

            if (_dirCache.TryGetValue(normalizedRoot, out var cached) && cached.Expiry > now)
            {
                all.AddRange(cached.Dirs);
                continue;
            }

            var dirs = await Task.Run(() => ScanDirectoriesRecursive(normalizedRoot), ct);
            _dirCache[normalizedRoot] = (now.Add(_cacheTtl), dirs);
            all.AddRange(dirs);
        }

        return all;
    }

    private static List<string> ScanDirectoriesRecursive(string root)
    {
        var result = new List<string> { root };
        try { ScanRecursive(root, result, 0, 5); }
        catch { /* permission errors — return what we have */ }
        return result;
    }

    private static void ScanRecursive(string dir, List<string> result, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                // Skip hidden dirs (.git, .vs, etc.)
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.')) continue;

                result.Add(sub);
                ScanRecursive(sub, result, depth + 1, maxDepth);
            }
        }
        catch { /* skip inaccessible */ }
    }

    // ── Recursive file search ─────────────────────────────────────

    internal string? TryFindFileRecursive(string fileName)
    {
        foreach (var root in _allowedDirectories)
        {
            var normalizedRoot = NormalizePath(root);
            var found = SearchFileRecursive(normalizedRoot, fileName, 0, 5);
            if (found is not null)
            {
                _logger?.LogDebug(
                    "[PathValidator] File search: '{FileName}' found at '{Path}'",
                    fileName, found);
                return found;
            }
        }
        return null;
    }

    private static string? SearchFileRecursive(string dir, string fileName, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return null;
        try
        {
            var filePath = Path.Combine(dir, fileName);
            if (File.Exists(filePath))
                return filePath;

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.')) continue;

                var found = SearchFileRecursive(sub, fileName, depth + 1, maxDepth);
                if (found is not null)
                    return found;
            }
        }
        catch { /* skip inaccessible */ }
        return null;
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
