namespace Nexus.Models;

using Nexus.Models.Enums;

/// <summary>
/// Read-only catalog of curated <see cref="ModelCandidate"/> entries known to the system.
/// </summary>
public interface ICuratedCatalog
{
    /// <summary>Gets the total number of candidates in the catalog.</summary>
    int Count { get; }

    /// <summary>Returns all candidates in the catalog.</summary>
    IReadOnlyList<ModelCandidate> GetAllCandidates();

    /// <summary>Returns the candidate with the given <paramref name="id"/>, or <see langword="null"/> if not found.</summary>
    ModelCandidate? GetById(string id);

    /// <summary>Returns all candidates belonging to the given model <paramref name="family"/>.</summary>
    IReadOnlyList<ModelCandidate> GetByFamily(string family);

    /// <summary>Returns all candidates whose <see cref="ModelCandidate.TaskFit"/> list contains <paramref name="taskFit"/>.</summary>
    IReadOnlyList<ModelCandidate> GetByTaskFit(ModelTaskFit taskFit);
}
