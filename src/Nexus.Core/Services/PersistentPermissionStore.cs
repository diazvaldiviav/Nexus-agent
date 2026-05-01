using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexus.Core.Config;

namespace Nexus.Core.Services;

/// <summary>
/// Persists tool permission decisions to <c>~/.nexus/permissions.json</c>.
/// Concurrency-safe within process via <see cref="SemaphoreSlim"/>;
/// cross-process safety is best-effort last-writer-wins (temp-file-then-rename).
/// </summary>
public sealed class PersistentPermissionStore : IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<PersistentPermissionStore>? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PersistentPermissionStore(
        string filePath,
        ILogger<PersistentPermissionStore>? logger = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = logger;
    }

    /// <summary>
    /// Tries to find a stored entry for (tool, value) in the current cwd.
    /// Returns the <see cref="PermissionEntry"/> on match, null on miss/malformed/missing file.
    /// </summary>
    public async Task<PermissionEntry?> LookupAsync(
        string cwd,
        string tool,
        string value,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var store = await LoadAsync(ct).ConfigureAwait(false);
        var cwdKey = ToCwdKey(cwd);

        if (!store.Directories.TryGetValue(cwdKey, out var dirEntry))
            return null;

        if (!dirEntry.Tools.TryGetValue(tool, out var toolEntry))
            return null;

        foreach (var (pattern, patternEntry) in toolEntry.Patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: true))
            {
                return new PermissionEntry(tool, pattern, patternEntry.Action, patternEntry.UpdatedAt);
            }
        }

        return null;
    }

    /// <summary>
    /// Persists an allow entry for the given cwd, tool, and pattern.
    /// Uses temp-file-then-rename for crash-safety.
    /// </summary>
    public Task AllowAsync(
        string cwd,
        string tool,
        string pattern,
        CancellationToken ct = default)
        => UpsertAsync(cwd, tool, pattern, "allow", ct);

    /// <summary>
    /// Upserts a tool/pattern → action entry for the given cwd.
    /// </summary>
    public async Task UpsertAsync(
        string cwd,
        string tool,
        string pattern,
        string action,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadAsync(ct).ConfigureAwait(false);
            var cwdKey = ToCwdKey(cwd);

            if (!store.Directories.TryGetValue(cwdKey, out var dirEntry))
            {
                dirEntry = new DirectoryEntry();
                store.Directories[cwdKey] = dirEntry;
            }

            if (!dirEntry.Tools.TryGetValue(tool, out var toolEntry))
            {
                toolEntry = new ToolEntry();
                dirEntry.Tools[tool] = toolEntry;
            }

            toolEntry.Patterns[pattern] = new PatternEntry
            {
                Action = action,
                UpdatedAt = DateTime.UtcNow
            };

            await WriteAtomicAsync(store, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Lists all permission entries for the given cwd and tool.
    /// </summary>
    public async Task<IReadOnlyList<PermissionEntry>> ListAsync(
        string cwd,
        string tool,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var store = await LoadAsync(ct).ConfigureAwait(false);
        var cwdKey = ToCwdKey(cwd);

        if (!store.Directories.TryGetValue(cwdKey, out var dirEntry))
            return Array.Empty<PermissionEntry>();

        if (!dirEntry.Tools.TryGetValue(tool, out var toolEntry))
            return Array.Empty<PermissionEntry>();

        return toolEntry.Patterns
            .Select(kvp => new PermissionEntry(tool, kvp.Key, kvp.Value.Action, kvp.Value.UpdatedAt))
            .ToList()
            .AsReadOnly();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<StoreFile> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new StoreFile();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            var store = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions);

            if (store is null || store.Version != 1)
            {
                _logger?.LogWarning(
                    "[PermissionStore] schema mismatch (version={Version}) — treating as empty",
                    store?.Version ?? 0);
                return new StoreFile();
            }

            return store;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "[PermissionStore] JSON parse failure at {Path} — treating as empty",
                _filePath);
            return new StoreFile();
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex,
                "[PermissionStore] I/O error reading {Path} — treating as empty",
                _filePath);
            return new StoreFile();
        }
    }

    private async Task WriteAtomicAsync(StoreFile store, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(store, JsonOptions);

        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static string ToCwdKey(string cwd)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(cwd));

    // ── Schema types (private) ────────────────────────────────────────────────

    private sealed class StoreFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("directories")]
        public Dictionary<string, DirectoryEntry> Directories { get; set; } = new();
    }

    private sealed class DirectoryEntry
    {
        [JsonPropertyName("tools")]
        public Dictionary<string, ToolEntry> Tools { get; set; } = new();
    }

    private sealed class ToolEntry
    {
        [JsonPropertyName("patterns")]
        public Dictionary<string, PatternEntry> Patterns { get; set; } = new();
    }

    private sealed class PatternEntry
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}

/// <summary>
/// A stored permission decision for a (tool, pattern) pair.
/// </summary>
public sealed record PermissionEntry(
    string Tool,
    string Pattern,
    string Action,
    DateTime UpdatedAt);
