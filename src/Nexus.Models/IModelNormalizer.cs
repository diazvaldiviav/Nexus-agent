using Nexus.Models.Profiles;

namespace Nexus.Models;

/// <summary>
/// Computes a <see cref="ModelExecutionProfile"/> from a raw <see cref="ModelCandidate"/>
/// by deriving memory requirements, cost classes, and runtime requirements.
/// </summary>
public interface IModelNormalizer
{
    /// <summary>
    /// Derives a <see cref="ModelExecutionProfile"/> from the specified <paramref name="candidate"/>
    /// by computing memory requirements, cost classifications, and runtime selection.
    /// </summary>
    /// <param name="candidate">The model candidate to normalize.</param>
    /// <returns>A fully computed execution profile for the candidate.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate"/> is <see langword="null"/>.</exception>
    ModelExecutionProfile Normalize(ModelCandidate candidate);
}
