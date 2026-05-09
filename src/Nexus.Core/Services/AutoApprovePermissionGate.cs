using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;

namespace Nexus.Core.Services;

/// <summary>
/// Non-interactive <see cref="IPermissionGate"/> for Desktop/headless hosts.
/// Full-tier models (≥30B params) are auto-approved with a warning.
/// All other tiers (ChatOnly &lt;4B, Limited &lt;8B, Capable &lt;30B) are auto-denied to
/// enforce the Hard Safety Invariant in non-interactive mode.
/// <para>
/// NOTE: Tier detection is inlined here (duplicating <c>Nexus.Connectors.ToolFiltering.ToolCapabilityResolver</c>)
/// because <c>Nexus.Core</c> must not reference <c>Nexus.Connectors</c> (layer boundary rule).
/// Sprint 11 hardening: extract <c>IModelTierResolver</c> into <c>Nexus.Core</c>.
/// </para>
/// </summary>
public sealed class AutoApprovePermissionGate : IPermissionGate
{
    // Mirrors ToolCapabilityResolver constants — keep in sync.
    private static readonly Regex ParamRegex = new(
        @"(\d+(?:\.\d+)?)\s*b(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cross-layer constraint: must stay character-identical to
    // Nexus.Connectors.ToolFiltering.ToolCapabilityResolver thresholds (Sprint 11
    // cleanup candidate: extract IModelTierResolver to a shared abstraction layer).
    private const double ChatOnlyModelThreshold = 4.0;
    private const double LimitedModelThreshold = 8.0;
    private const double CapableModelThreshold = 30.0;

    private readonly NexusConfig _config;
    private readonly ILogger<AutoApprovePermissionGate>? _logger;

    public AutoApprovePermissionGate(
        NexusConfig config,
        ILogger<AutoApprovePermissionGate>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PermissionGateResponse> RequestAsync(
        PermissionRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var modelName = _config.Models.Local.Model;

        if (IsFullTier(modelName))
        {
            _logger?.LogWarning(
                "[PermissionGate] Auto-approving destructive tool '{Tool}' (no interactive prompt available; full-tier model)",
                request.ToolName);
            return Task.FromResult(new PermissionGateResponse(PermissionDecision.Allow));
        }

        _logger?.LogInformation(
            "[PermissionGate] Small model + non-interactive mode → denying destructive tool by default");
        return Task.FromResult(
            new PermissionGateResponse(PermissionDecision.Deny, "non-interactive prompt unavailable"));
    }

    // ── Tier detection (private) ──────────────────────────────────────────────

    private enum ToolTier { ChatOnly, Limited, Capable, Full }

    private static bool IsFullTier(string? modelName)
        => ResolveToolTier(modelName) == ToolTier.Full;

    private static ToolTier ResolveToolTier(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return ToolTier.Full;

        var match = ParamRegex.Match(modelName);
        if (!match.Success)
            return ToolTier.Full;

        if (!double.TryParse(
                match.Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var b))
            return ToolTier.Full;

        if (b < ChatOnlyModelThreshold)
            return ToolTier.ChatOnly;

        if (b < LimitedModelThreshold)
            return ToolTier.Limited;

        if (b < CapableModelThreshold)
            return ToolTier.Capable;

        return ToolTier.Full;
    }
}
