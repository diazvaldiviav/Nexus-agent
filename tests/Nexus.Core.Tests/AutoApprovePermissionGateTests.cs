using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Services;

namespace Nexus.Core.Tests;

/// <summary>
/// Unit tests for <see cref="AutoApprovePermissionGate"/>.
/// Validates the Hard Safety Invariant: full-tier models are auto-approved,
/// small-tier models are auto-denied in non-interactive mode.
/// </summary>
public sealed class AutoApprovePermissionGateTests
{
    private static PermissionRequest MakeRequest(string toolName = "write_file")
        => new("filesystem", toolName,
            new Dictionary<string, object> { ["path"] = "/tmp/test.txt" }.AsReadOnly(),
            new[] { "/tmp/test.txt" },
            "destructive operation");

    /// <summary>
    /// Full-tier models (≥30B) are auto-approved with a warning.
    /// </summary>
    [Fact]
    public async Task RequestAsync_FullTierModel_ReturnsAllow()
    {
        // Arrange
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Model = "llama3:70b" }
            }
        };
        var gate = new AutoApprovePermissionGate(config);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Allow, response.Decision);
        Assert.Null(response.Feedback);
    }

    /// <summary>
    /// Small-tier models (&lt;8B) are auto-denied to enforce the Hard Safety Invariant.
    /// The feedback message must contain "non-interactive" to be surfaced to the agent.
    /// </summary>
    [Fact]
    public async Task RequestAsync_SmallModelTier_ReturnsDenyWithNonInteractiveReason()
    {
        // Arrange
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Model = "qwen3:1.7b" }
            }
        };
        var gate = new AutoApprovePermissionGate(config);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Deny, response.Decision);
        Assert.NotNull(response.Feedback);
        Assert.Contains("non-interactive", response.Feedback, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Capable-tier models (8B–30B) — like 14B — are also auto-denied in non-interactive mode.
    /// Only Full-tier (≥30B) triggers auto-approve. Validates the recalibrated thresholds
    /// (Limited &lt; 8B, Capable &lt; 30B, Full ≥ 30B).
    /// </summary>
    [Fact]
    public async Task RequestAsync_CapableTierModel_ReturnsDenyWithNonInteractiveReason()
    {
        // Arrange
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Model = "qwen3:14b" }
            }
        };
        var gate = new AutoApprovePermissionGate(config);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Deny, response.Decision);
        Assert.NotNull(response.Feedback);
        Assert.Contains("non-interactive", response.Feedback, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ChatOnly-tier models (&lt; 4B) — like 1.7B — are auto-denied in non-interactive mode.
    /// They share the same denial path as Limited/Capable tiers; only Full-tier (≥ 30B) auto-approves.
    /// </summary>
    [Fact]
    public async Task RequestAsync_ChatOnlyModel_ReturnsDenyWithNonInteractiveReason()
    {
        // Arrange
        var config = new NexusConfig
        {
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Model = "qwen3:1.7b" }
            }
        };
        var gate = new AutoApprovePermissionGate(config);

        // Act
        var response = await gate.RequestAsync(MakeRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(PermissionDecision.Deny, response.Decision);
        Assert.NotNull(response.Feedback);
        Assert.Contains("non-interactive", response.Feedback, StringComparison.OrdinalIgnoreCase);
    }
}
