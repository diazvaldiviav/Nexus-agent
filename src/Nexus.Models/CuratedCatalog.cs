namespace Nexus.Models;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Models.Enums;

/// <summary>
/// Loads the embedded <c>curated-catalog.json</c> at construction and exposes it through
/// the <see cref="ICuratedCatalog"/> contract. All data is immutable after construction;
/// all query methods are thread-safe.
/// </summary>
public sealed class CuratedCatalog : ICuratedCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyList<ModelCandidate> _all;
    private readonly Dictionary<string, ModelCandidate> _byId;
    private readonly Dictionary<string, IReadOnlyList<ModelCandidate>> _byFamily;
    private readonly Dictionary<ModelTaskFit, IReadOnlyList<ModelCandidate>> _byTaskFit;

    /// <summary>Initializes the catalog by loading and indexing all entries from the embedded JSON resource.</summary>
    public CuratedCatalog()
    {
        _all = LoadFromEmbeddedResource();

        _byId = new Dictionary<string, ModelCandidate>(_all.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in _all)
            _byId[candidate.Id] = candidate;

        _byFamily = _all
            .GroupBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ModelCandidate>)g.ToList().AsReadOnly(),
                StringComparer.OrdinalIgnoreCase);

        _byTaskFit = _all
            .SelectMany(c => c.TaskFit.Select(t => (TaskFit: t, Candidate: c)))
            .GroupBy(x => x.TaskFit)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ModelCandidate>)g.Select(x => x.Candidate).ToList().AsReadOnly());
    }

    /// <inheritdoc/>
    public int Count => _all.Count;

    /// <inheritdoc/>
    public IReadOnlyList<ModelCandidate> GetAllCandidates() => _all;

    /// <inheritdoc/>
    public ModelCandidate? GetById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _byId.TryGetValue(id, out var candidate);
        return candidate;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModelCandidate> GetByFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return _byFamily.TryGetValue(family, out var list) ? list : Array.Empty<ModelCandidate>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModelCandidate> GetByTaskFit(ModelTaskFit taskFit)
    {
        return _byTaskFit.TryGetValue(taskFit, out var list) ? list : Array.Empty<ModelCandidate>();
    }

    private static IReadOnlyList<ModelCandidate> LoadFromEmbeddedResource()
    {
        var assembly = typeof(CuratedCatalog).Assembly;
        const string resourceName = "Nexus.Models.Data.curated-catalog.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.GetName().Name}'. " +
                "Ensure 'curated-catalog.json' has Build Action set to EmbeddedResource.");

        var candidates = JsonSerializer.Deserialize<List<ModelCandidate>>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Deserialization of '{resourceName}' returned null. The file may be empty or malformed.");

        return candidates.AsReadOnly();
    }
}
