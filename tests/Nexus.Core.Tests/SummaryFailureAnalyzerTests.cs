using Nexus.Core.Models;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for SummaryFailureAnalyzer — AC-6 / AC-8.
/// </summary>
public class SummaryFailureAnalyzerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: NoFailures_ReturnsEmpty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoFailures_ReturnsEmpty()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user",      Content = "Read the file please." },
            new() { Role = "assistant", Content = "Sure, reading the file now." },
            new() { Role = "user",      Content = "[Tool result for step 1]\nFile content here." },
            new() { Role = "assistant", Content = "The file contains configuration data." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);
        var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);

        // Assert
        Assert.False(findings.HasFailures);
        Assert.Equal(0, findings.VerificationWarnings);
        Assert.Equal(0, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
        Assert.Equal("", grounding);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: OneVerificationWarning_Counted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneVerificationWarning_Counted()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user",      Content = "Write the config file." },
            new() { Role = "tool",      Content = "[VerificationWarning] File content mismatch\nExpected: foo\nGot: bar" }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert
        Assert.True(findings.HasFailures);
        Assert.Equal(1, findings.VerificationWarnings);
        Assert.Equal(0, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: OneRetryExhausted_Counted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneRetryExhausted_Counted()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Run the analysis tool." },
            new() { Role = "user", Content = "[PlanStep 4] Exceeded 5 attempts; moving on." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert
        Assert.True(findings.HasFailures);
        Assert.Equal(0, findings.VerificationWarnings);
        Assert.Equal(1, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: Mixed_AllCountsCorrect
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mixed_AllCountsCorrect()
    {
        // Arrange: one of each sentinel type + extra verification warning (total 6 failure messages → cap to 3 reasons)
        var history = new List<ConversationMessage>
        {
            new() { Role = "user",      Content = "Step 1: normal user message." },
            new() { Role = "tool",      Content = "[VerificationWarning] First warning: content mismatch" },
            new() { Role = "user",      Content = "[PlanStep 2] Exceeded 3 attempts; moving on." },
            new() { Role = "user",      Content = "[Tool write_file failed: IOException: disk full]" },
            new() { Role = "user",      Content = "[PermissionDenied] User denied write to /etc/passwd" },
            new() { Role = "user",      Content = "[DoomLoop] Detected cyclical tool call pattern" },
            new() { Role = "tool",      Content = "[VerificationWarning] Second warning: size mismatch" }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert — individual counts
        Assert.Equal(2, findings.VerificationWarnings);
        Assert.Equal(1, findings.RetriesExhausted);
        Assert.Equal(1, findings.ToolErrors);
        Assert.Equal(1, findings.PermissionDenials);
        Assert.Equal(1, findings.DoomLoops);
        Assert.True(findings.HasFailures);

        // ExcerptedReasons is capped at 3 most-recent (out of 6 total failure messages)
        Assert.Equal(3, findings.ExcerptedReasons.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: GroundingMessage_OmitsZeroCounts
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundingMessage_OmitsZeroCounts()
    {
        // Arrange: only verification warnings present
        var history = new List<ConversationMessage>
        {
            new() { Role = "tool", Content = "[VerificationWarning] Output did not contain expected text" }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);
        var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);

        // Assert: grounding message is present
        Assert.Contains("[PlanResult]", grounding);
        Assert.Contains("Verification failures:", grounding);

        // Zero-count lines must NOT appear
        Assert.DoesNotContain("Step retries exhausted: 0", grounding);
        Assert.DoesNotContain("Tool errors: 0", grounding);
        Assert.DoesNotContain("Permission denials: 0", grounding);
        Assert.DoesNotContain("Doom loops: 0", grounding);

        // Closing instruction always present
        Assert.Contains("Do NOT claim success", grounding);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: OneStepSkipped_Counted (Layer 3 — Sprint 10 follow-up)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneStepSkipped_Counted()
    {
        // Arrange — sentinel emitted by AgentService.ExecutePlanAsync when
        // a plan step has MatchedToolName == null and is skipped.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Modify the config file." },
            new() { Role = "user", Content = "[PlanStep 2] No tool matched; skipping." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert
        Assert.True(findings.HasFailures);
        Assert.Equal(0, findings.VerificationWarnings);
        Assert.Equal(0, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
        Assert.Equal(1, findings.StepsSkippedNoToolMatch);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: MultipleSkipsAndFailures_AllCountsCorrect (Layer 3 — Sprint 10)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleSkipsAndFailures_AllCountsCorrect()
    {
        // Arrange — mixed retry-exhausted + skipped sentinels.
        // Both share the "[PlanStep " prefix; the analyzer must distinguish them by
        // the secondary substrings (Exceeded/attempts vs No tool matched/skipping).
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "[PlanStep 1] Exceeded 5 attempts; moving on." },
            new() { Role = "user", Content = "[PlanStep 2] No tool matched; skipping." },
            new() { Role = "user", Content = "[PlanStep 3] No tool matched; skipping." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert — different sentinel kinds counted into different categories
        Assert.True(findings.HasFailures);
        Assert.Equal(1, findings.RetriesExhausted);
        Assert.Equal(2, findings.StepsSkippedNoToolMatch);
        Assert.Equal(0, findings.ToolErrors);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: GroundingMessage_IncludesSkippedSteps (Layer 3 — Sprint 10)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundingMessage_IncludesSkippedSteps()
    {
        // Arrange — only skipped steps; other categories must not appear with count 0.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "[PlanStep 2] No tool matched; skipping." },
            new() { Role = "user", Content = "[PlanStep 3] No tool matched; skipping." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);
        var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);

        // Assert
        Assert.Contains("[PlanResult]", grounding);
        Assert.Contains("Steps skipped (no matching tool): 2", grounding);

        // Zero-count lines must still be omitted
        Assert.DoesNotContain("Verification failures: 0", grounding);
        Assert.DoesNotContain("Tool errors: 0", grounding);
        Assert.DoesNotContain("Step retries exhausted: 0", grounding);

        Assert.Contains("Do NOT claim success", grounding);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: OneFidelityWarning_Counted (Layer 4 — AC-L4-4)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneFidelityWarning_Counted()
    {
        // Arrange — a [FidelityWarning] sentinel injected by AgentService when retries exhausted.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Read the sprint plan." },
            new() { Role = "user", Content = "[FidelityWarning] Final summary still diverges from tool results after 1 retries (score=0.24)." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert
        Assert.True(findings.HasFailures);
        Assert.Equal(1, findings.FidelityWarnings);
        Assert.Equal(0, findings.VerificationWarnings);
        Assert.Equal(0, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
        Assert.Equal(0, findings.StepsSkippedNoToolMatch);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: GroundingMessage_IncludesFidelityFailures (Layer 4 — AC-L4-4)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundingMessage_IncludesFidelityFailures()
    {
        // Arrange — fidelity warning only.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "[FidelityWarning] Final summary still diverges from tool results after 1 retries (score=0.24)." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);
        var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);

        // Assert
        Assert.Contains("[PlanResult]", grounding);
        Assert.Contains("- Fidelity failures: 1", grounding);
        Assert.Contains("Do NOT claim success", grounding);

        // Zero-count lines must be omitted
        Assert.DoesNotContain("Verification failures:", grounding);
        Assert.DoesNotContain("Steps skipped:", grounding);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: OneSchemaRejection_Counted (Fix H)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneSchemaRejection_Counted()
    {
        // Arrange — schema validator rejected a tool call (envelope returned by ExecuteToolWithTimeoutAsync).
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Search for the config file." },
            new() { Role = "user", Content = "[Tool result for step 1]\n[SchemaValidationError] Missing required argument 'pattern'. search_files requires: path (string, REQUIRED), pattern (string, REQUIRED), excludePatterns (array, optional)" }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);

        // Assert
        Assert.True(findings.HasFailures);
        Assert.Equal(1, findings.SchemaRejections);
        Assert.Equal(0, findings.VerificationWarnings);
        Assert.Equal(0, findings.RetriesExhausted);
        Assert.Equal(0, findings.ToolErrors);
        Assert.Equal(0, findings.PermissionDenials);
        Assert.Equal(0, findings.DoomLoops);
        Assert.Equal(0, findings.StepsSkippedNoToolMatch);
        Assert.Equal(0, findings.FidelityWarnings);
        Assert.Single(findings.ExcerptedReasons);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: GroundingMessage_IncludesSchemaRejections (Fix H)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundingMessage_IncludesSchemaRejections()
    {
        // Arrange — schema rejection only.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "[Tool result for step 2]\n[SchemaValidationError] Missing required argument 'content'." }
        };

        // Act
        var findings = SummaryFailureAnalyzer.Analyze(history);
        var grounding = SummaryFailureAnalyzer.BuildGroundingMessage(findings);

        // Assert
        Assert.Contains("[PlanResult]", grounding);
        Assert.Contains("- Schema validation rejections: 1", grounding);
        Assert.Contains("Do NOT claim success", grounding);

        // Zero-count lines must be omitted
        Assert.DoesNotContain("Verification failures:", grounding);
        Assert.DoesNotContain("Fidelity failures:", grounding);
    }
}
