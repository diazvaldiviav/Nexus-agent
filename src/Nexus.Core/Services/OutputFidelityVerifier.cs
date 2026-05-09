using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Config;
using Nexus.Memory.Abstractions;

namespace Nexus.Core.Services;

/// <summary>
/// Fidelity result produced by <see cref="OutputFidelityVerifier.VerifyAsync"/>.
/// </summary>
/// <param name="HybridScore">Combined weighted score (0.0 – 1.0).</param>
/// <param name="SubstringScore">N-gram substring match score (0.0 – 1.0).</param>
/// <param name="EmbeddingScore">Embedding cosine similarity score (0.0 – 1.0).</param>
/// <param name="Passed"><see langword="true"/> when <paramref name="HybridScore"/> meets or exceeds the configured threshold.</param>
/// <param name="Reason">Human-readable score breakdown for logging.</param>
public sealed record FidelityResult(
    float HybridScore,
    float SubstringScore,
    float EmbeddingScore,
    bool Passed,
    string Reason);

/// <summary>
/// Layer 4 — Output Fidelity Verifier. Scores an LLM summary against accumulated
/// read-tool results using a hybrid substring n-gram + embedding cosine similarity model.
/// Stateless; safe to register as Singleton.
/// </summary>
public sealed class OutputFidelityVerifier
{
    private readonly NexusConfig _config;
    private readonly ILogger<OutputFidelityVerifier>? _logger;
    private readonly IEmbeddingService? _embeddings;

    /// <summary>
    /// ASCII stopwords filtered during n-gram tokenization.
    /// 19 entries with OrdinalIgnoreCase comparer.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "to", "of", "and", "or", "in",
        "on", "with", "for", "be", "by", "as", "at", "this", "that", "it"
    };

    /// <summary>
    /// Regex that matches runs of whitespace, used by <see cref="Normalize"/>.
    /// </summary>
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Regex that matches runs of punctuation/markdown control characters to strip.
    /// </summary>
    private static readonly Regex MarkdownStrip = new(@"[#*`~]+", RegexOptions.Compiled);

    public OutputFidelityVerifier(
        NexusConfig config,
        ILogger<OutputFidelityVerifier>? logger = null,
        IEmbeddingService? embeddings = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _embeddings = embeddings;
    }

    /// <summary>
    /// Scores the candidate <paramref name="llmSummary"/> against accumulated
    /// <paramref name="readToolResults"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="FidelityResult"/> on success, or <see langword="null"/> when verification
    /// was skipped (disabled, insufficient data) or threw an unrecoverable exception.
    /// </returns>
    public async Task<FidelityResult?> VerifyAsync(
        IReadOnlyList<string> readToolResults,
        string llmSummary,
        CancellationToken ct)
    {
        try
        {
            // 1. Early-skip guards
            if (!_config.Mcp.OutputFidelityVerificationEnabled)
                return null;

            if (readToolResults is null || readToolResults.Count == 0)
                return null;

            var combinedToolText = string.Join("\n", readToolResults);
            if (combinedToolText.Length < 50)
                return null;

            if (llmSummary is null || llmSummary.Length < 50)
                return null;

            // 2. Substring n-gram score
            var substringScore = SubstringScoreOf(combinedToolText, llmSummary);

            // 3. Embedding cosine similarity score
            var embeddingScore = await EmbeddingScoreOfAsync(combinedToolText, llmSummary, ct).ConfigureAwait(false);

            // 4. Hybrid combination
            var hybridScore = _config.Mcp.OutputFidelitySubstringWeight * substringScore
                            + _config.Mcp.OutputFidelityEmbeddingWeight * embeddingScore;

            var passed = hybridScore >= _config.Mcp.OutputFidelityMinScore;

            var reason = $"hybrid={hybridScore:F2} substring={substringScore:F2} embedding={embeddingScore:F2}";

            return new FidelityResult(hybridScore, substringScore, embeddingScore, passed, reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[FidelityVerifier] threw — returning null");
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static float SubstringScoreOf(string toolText, string summary)
    {
        var normalizedTool = Normalize(toolText);
        var normalizedSummary = Normalize(summary);

        var tokens = Tokenize(normalizedTool);
        var ngrams = Build3Grams(tokens);

        if (ngrams.Count < 3)
            return 1.0f; // benefit of doubt — too short to verify

        int matched = 0;
        foreach (var gram in ngrams)
        {
            if (normalizedSummary.Contains(gram, StringComparison.Ordinal))
                matched++;
        }

        return matched / (float)ngrams.Count;
    }

    private async Task<float> EmbeddingScoreOfAsync(string toolText, string summary, CancellationToken ct)
    {
        if (_embeddings is null)
            return 1.0f;

        try
        {
            var a = await _embeddings.GenerateEmbeddingAsync(toolText, ct).ConfigureAwait(false);
            var b = await _embeddings.GenerateEmbeddingAsync(summary, ct).ConfigureAwait(false);
            var raw = CosineSimilarity(a, b);
            return Math.Clamp(raw, 0f, 1f);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[FidelityVerifier] embedding scoring threw — falling back to 1.0f");
            return 1.0f;
        }
    }

    /// <summary>
    /// Normalizes text for n-gram comparison:
    /// lowercase, strip markdown control characters (#, *, `, ~), collapse whitespace.
    /// </summary>
    private static string Normalize(string s)
    {
        var lower = s.ToLowerInvariant();
        var stripped = MarkdownStrip.Replace(lower, " ");
        var collapsed = WhitespaceRun.Replace(stripped, " ").Trim();
        return collapsed;
    }

    /// <summary>
    /// Splits normalized text into tokens; filters stopwords and tokens shorter than 2 chars.
    /// </summary>
    private static IReadOnlyList<string> Tokenize(string normalizedText)
    {
        var result = new List<string>();
        var parts = normalizedText.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length < 2) continue;
            if (Stopwords.Contains(part)) continue;
            result.Add(part);
        }
        return result;
    }

    /// <summary>
    /// Builds a list of sliding 3-gram strings ("t0 t1 t2") from a token list.
    /// </summary>
    private static IReadOnlyList<string> Build3Grams(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
            return Array.Empty<string>();

        var ngrams = new List<string>(tokens.Count - 2);
        for (int i = 0; i <= tokens.Count - 3; i++)
            ngrams.Add($"{tokens[i]} {tokens[i + 1]} {tokens[i + 2]}");

        return ngrams;
    }

    /// <summary>
    /// Cosine similarity in [0, 1] between two vectors of equal length.
    /// Returns 0 when either vector is zero-norm or lengths differ.
    /// Local copy of the helper in <c>ToolPlanner.cs</c> (avoids cross-layer cycle).
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0f || normB == 0f)
            return 0f;
        return dot / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
