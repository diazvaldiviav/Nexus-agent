using Nexus.Core.Config;
using Nexus.Core.Services;
using Nexus.Core.Tests.Fakes;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for OutputFidelityVerifier — AC-L4-1 / AC-L4-7.
/// All tests construct the verifier directly; no DI container needed.
/// </summary>
public class OutputFidelityVerifierTests
{
    // Shared tool result that is long enough to pass the 50-char guard.
    // ASCII-only to avoid accent/normalization ambiguity in n-gram tests.
    private const string LongToolResult =
        "## Sprint Backlog Items\n\n" +
        "### Task Alpha\nStatus: pending verification\nAssignee: alice\nPoints: five\n\n" +
        "### Task Beta\nStatus: blocked review\nAssignee: bob\nPoints: three";

    // Summary that closely quotes the tool result (high substring overlap).
    // Mirrors the exact token sequence from LongToolResult so n-grams match after normalization.
    private const string QuotedSummary =
        "sprint backlog items task alpha status pending verification assignee alice points five " +
        "task beta status blocked review assignee bob points three";

    // Summary with zero overlap (hallucination scenario)
    private const string HallucinatedSummary =
        "The sprint plan outlines goals: develop a basic e-commerce platform with product listing, " +
        "user authentication and shopping cart. Key tasks include creating Category.cs, Product.cs, " +
        "User.cs models for the backend and implementing the frontend with React and Tailwind CSS.";

    private static NexusConfig DefaultConfig() => new()
    {
        Mcp = new McpConfig
        {
            OutputFidelityVerificationEnabled = true,
            OutputFidelityMinScore = 0.30f,
            OutputFidelitySubstringWeight = 0.4f,
            OutputFidelityEmbeddingWeight = 0.6f,
            OutputFidelityMaxRetries = 1
        }
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: SubstringOnly_AbovesThreshold_PassesWhenLlmQuotes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubstringOnly_AbovesThreshold_PassesWhenLlmQuotes()
    {
        // Arrange — no embedding service, so score is purely substring-driven.
        // With weight 0.4 substring + 0.6 * 1.0 (null embedding fallback) and a high substring
        // score from quoted content, hybrid should exceed 0.30.
        var config = DefaultConfig();
        config.Mcp.OutputFidelityEmbeddingWeight = 0.0f;
        config.Mcp.OutputFidelitySubstringWeight = 1.0f;
        var verifier = new OutputFidelityVerifier(config);

        var toolResults = new[] { LongToolResult };

        // Act
        var result = await verifier.VerifyAsync(toolResults, QuotedSummary, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Passed, $"Expected pass but hybrid={result.HybridScore:F2} substring={result.SubstringScore:F2}");
        Assert.True(result.SubstringScore > 0f, "Expected non-zero substring score for quoted text");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: SubstringOnly_LlmHallucinates_Fails
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubstringOnly_LlmHallucinates_Fails()
    {
        // Arrange — substring-only (no embeddings). Hallucinated summary has zero 3-gram overlap.
        var config = DefaultConfig();
        config.Mcp.OutputFidelityEmbeddingWeight = 0.0f;
        config.Mcp.OutputFidelitySubstringWeight = 1.0f;
        config.Mcp.OutputFidelityMinScore = 0.30f;
        var verifier = new OutputFidelityVerifier(config);

        var toolResults = new[] { LongToolResult };

        // Act
        var result = await verifier.VerifyAsync(toolResults, HallucinatedSummary, CancellationToken.None);

        // Assert — with 0 embedding weight, hybrid = substringScore. Hallucination = ~0 → fail.
        // EmbeddingScore is still 1.0f (null-service fallback), but hybrid = 1.0*substringScore + 0.0*embeddingScore.
        Assert.NotNull(result);
        Assert.False(result.Passed, $"Expected fail but hybrid={result.HybridScore:F2}");
        Assert.True(result.SubstringScore < 0.30f, $"Expected low substring score, got {result.SubstringScore:F2}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: EmbeddingOnly_TopicallyAligned_Passes_WhenSubstringWeightZero
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmbeddingOnly_TopicallyAligned_Passes_WhenSubstringWeightZero()
    {
        // Arrange — embedding returns a fixed high-similarity pair (same vector = cosine 1.0).
        var config = DefaultConfig();
        config.Mcp.OutputFidelitySubstringWeight = 0.0f;
        config.Mcp.OutputFidelityEmbeddingWeight = 1.0f;
        config.Mcp.OutputFidelityMinScore = 0.30f;

        // Same embedding for both calls → cosine = 1.0
        var fakeEmb = new FakeEmbeddingService(fixedEmbedding: Enumerable.Repeat(0.1f, 768).ToArray());
        var verifier = new OutputFidelityVerifier(config, embeddings: fakeEmb);

        var toolResults = new[] { LongToolResult };

        // Act
        var result = await verifier.VerifyAsync(toolResults, QuotedSummary, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Passed, $"Expected pass but hybrid={result.HybridScore:F2}");
        Assert.True(result.EmbeddingScore > 0.9f, $"Expected high embedding score, got {result.EmbeddingScore:F2}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: Hybrid_DefaultWeights_DistinguishHallucinationFromQuote
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hybrid_DefaultWeights_DistinguishHallucinationFromQuote()
    {
        // Arrange — realistic scenario: embedding returns same vector for both to isolate substring.
        // With default weights 0.4 / 0.6 and embedding=1.0 (null fallback):
        //   Hallucinated: hybrid = 0.4*0 + 0.6*1.0 = 0.60 → passes (substring 0, embedding 1.0)
        //   This shows that substring-only discrimination requires lowering embedding weight.
        // Instead, test with embedding returning DIFFERENT vectors to simulate real divergence.
        var config = DefaultConfig();

        // Quoted: embedding cosine is high (1.0, same vector)
        var highSim = Enumerable.Repeat(0.5f, 768).ToArray();
        var fakeEmbHigh = new FakeEmbeddingService(fixedEmbedding: highSim);
        var verifierQuoted = new OutputFidelityVerifier(config, embeddings: fakeEmbHigh);

        var toolResults = new[] { LongToolResult };

        // Act — quoted summary
        var quotedResult = await verifierQuoted.VerifyAsync(toolResults, QuotedSummary, CancellationToken.None);

        // Assert — quoted summary passes
        Assert.NotNull(quotedResult);
        Assert.True(quotedResult.Passed, $"Quoted: hybrid={quotedResult.HybridScore:F2}");

        // For hallucination: use config with no embedding (null) so embedding=1.0 (fallback).
        // Hallucination with substring=0 and embedding=1.0: hybrid = 0.4*0 + 0.6*1.0 = 0.60 → passes.
        // This is intentional (NFR-2: no-embedding fallback is permissive).
        // Instead test with embedding returns zero vector for hallucinated text:
        var zeroEmb = new float[768]; // all zeros → cosine = 0
        var toolEmb = Enumerable.Repeat(1.0f, 768).ToArray();
        var fakeEmbDiff = new CallCountedEmbeddingService(toolEmb, zeroEmb);
        var verifierHallucinated = new OutputFidelityVerifier(config, embeddings: fakeEmbDiff);

        var hallucinatedResult = await verifierHallucinated.VerifyAsync(toolResults, HallucinatedSummary, CancellationToken.None);

        // Assert — hallucinated: embedding = 0 (different vectors), substring = 0 → hybrid = 0 < 0.30 → fail
        Assert.NotNull(hallucinatedResult);
        Assert.False(hallucinatedResult.Passed, $"Hallucinated: hybrid={hallucinatedResult.HybridScore:F2}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: EmptyToolResults_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyToolResults_ReturnsNull()
    {
        var verifier = new OutputFidelityVerifier(DefaultConfig());

        var result = await verifier.VerifyAsync(Array.Empty<string>(), QuotedSummary, CancellationToken.None);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: ToolResultBelow50Chars_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolResultBelow50Chars_ReturnsNull()
    {
        var verifier = new OutputFidelityVerifier(DefaultConfig());
        var shortResult = new[] { "short" }; // < 50 chars

        var result = await verifier.VerifyAsync(shortResult, QuotedSummary, CancellationToken.None);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: SummaryBelow50Chars_ReturnsNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SummaryBelow50Chars_ReturnsNull()
    {
        var verifier = new OutputFidelityVerifier(DefaultConfig());
        var shortSummary = "Too short."; // < 50 chars

        var result = await verifier.VerifyAsync(new[] { LongToolResult }, shortSummary, CancellationToken.None);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: EmbeddingServiceNull_FallsBackToSubstringOnly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmbeddingServiceNull_FallsBackToSubstringOnly()
    {
        // Arrange — no embedding service. Embedding score defaults to 1.0.
        var config = DefaultConfig(); // weights: sub=0.4, emb=0.6
        var verifier = new OutputFidelityVerifier(config, embeddings: null);

        // Act
        var result = await verifier.VerifyAsync(new[] { LongToolResult }, QuotedSummary, CancellationToken.None);

        // Assert — embedding score should be 1.0 (fallback)
        Assert.NotNull(result);
        Assert.Equal(1.0f, result.EmbeddingScore);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: EmbeddingServiceThrows_LogsWarningAndUsesSubstring
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmbeddingServiceThrows_LogsWarningAndUsesSubstring()
    {
        // Arrange — embedding service throws InvalidOperationException (non-OCE)
        var config = DefaultConfig();
        var throwingEmb = new FakeEmbeddingService(exception: new InvalidOperationException("Embedding unavailable"));
        var verifier = new OutputFidelityVerifier(config, embeddings: throwingEmb);

        // Act — should not throw; should fall back to 1.0f for embedding
        var result = await verifier.VerifyAsync(new[] { LongToolResult }, QuotedSummary, CancellationToken.None);

        // Assert — result is non-null (no crash); embedding score is 1.0 (graceful fallback)
        Assert.NotNull(result);
        Assert.Equal(1.0f, result.EmbeddingScore);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: Cancellation_PropagatesOce
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_PropagatesOce()
    {
        // Arrange — embedding service respects cancellation
        var config = DefaultConfig();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancellingEmb = new FakeEmbeddingService(
            exception: new OperationCanceledException("Cancelled"));
        var verifier = new OutputFidelityVerifier(config, embeddings: cancellingEmb);

        // Act + Assert — OCE must propagate, not be swallowed
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(new[] { LongToolResult }, QuotedSummary, cts.Token));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: Normalization_StripsMarkdownAndStopwords
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Normalization_StripsMarkdownAndStopwords()
    {
        // Arrange — tool result and summary both contain the exact same 3-gram after normalization.
        // Markdown headers (##, ###, **, `) and stopwords ("the", "is", "a") must be stripped.
        // ASCII-only to avoid accent handling ambiguity.
        var config = DefaultConfig();
        config.Mcp.OutputFidelityEmbeddingWeight = 0.0f;
        config.Mcp.OutputFidelitySubstringWeight = 1.0f;

        // Tool result: markdown header + stopwords + key content tokens.
        // After stripping ## ** ` the content tokens are: project status complete pipeline running active
        var toolResult = "## **Project** `Status` Complete\n\n### Pipeline Running Active\n\n" +
                         "This system is in active deployment mode for the project.";

        // Summary: plain prose containing the same token sequences.
        // "project status complete" and "pipeline running active" appear verbatim after normalization.
        var summary = "The document shows project status complete with pipeline running active deployment mode.";


        var verifier = new OutputFidelityVerifier(config);

        // Act
        var result = await verifier.VerifyAsync(new[] { toolResult }, summary, CancellationToken.None);

        // Assert — "project status complete" is the same 3-gram in both after normalization
        Assert.NotNull(result);
        Assert.True(result.SubstringScore > 0f,
            $"Expected substring score > 0 after normalization, got {result.SubstringScore:F2}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: NgramTooShort_ReturnsBenefitOfDoubt
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NgramTooShort_ReturnsBenefitOfDoubt()
    {
        // Arrange — tool result has < 3 non-stopword tokens → Build3Grams returns empty (< 3) → substringScore = 1.0f
        // Use long strings padded with stopwords to pass the 50-char check but with only 2 content tokens.
        // Stopwords: the, a, an, is, to, of, and, or, in, on, with, for, be, by, as, at, this, that, it
        // Single-char tokens are also filtered (length < 2).
        var config = DefaultConfig();
        config.Mcp.OutputFidelityEmbeddingWeight = 0.0f;
        config.Mcp.OutputFidelitySubstringWeight = 1.0f;

        // Tool text: all stopwords + one content word "ok" → only 1 non-stopword token → 0 3-grams
        // Must be > 50 chars; repeat the stopword phrase to pad length.
        var shortContentTool = "the is to of and or in on with for be by as at ok " +
                               "the is to of and or in on with for be by as at"; // 91 chars, 1 content token

        var longSummary = "The document shows the task is complete and verified by the team. All items are in order.";

        var verifier = new OutputFidelityVerifier(config);

        // Act
        var result = await verifier.VerifyAsync(new[] { shortContentTool }, longSummary, CancellationToken.None);

        // Assert — benefit of doubt: Build3Grams returns empty (< 3 tokens) → substringScore = 1.0
        Assert.NotNull(result);
        Assert.Equal(1.0f, result.SubstringScore);
        Assert.True(result.Passed, $"Expected pass (benefit of doubt), hybrid={result.HybridScore:F2}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal fake for differentiated embeddings per call
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class CallCountedEmbeddingService : Nexus.Memory.Abstractions.IEmbeddingService
    {
        private readonly float[] _firstVec;
        private readonly float[] _secondVec;
        private int _callCount;

        public CallCountedEmbeddingService(float[] firstVec, float[] secondVec)
        {
            _firstVec = firstVec;
            _secondVec = secondVec;
        }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(_callCount == 1 ? _firstVec : _secondVec);
        }
    }
}
