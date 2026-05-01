using System.IO.Enumeration;
using System.Text;
using Microsoft.Extensions.Logging;
using Nexus.Connectors.ToolFiltering;
using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Core.Services;
using Spectre.Console;

namespace Nexus.CLI;

// Future improvements (deferred from Sprint 10):
// - i18n: extract user-facing strings to PermissionGateStrings static class
// - SelectionPrompt.AddChoiceGroup for allow/deny visual separation
// - Truncate long path arguments at 60 chars in display
// - Print [grey]Auto-allowed (session): {tool}[/] when session cache hits

/// <summary>
/// Interactive <see cref="IPermissionGate"/> for the CLI host.
/// Uses Spectre.Console to prompt the user before destructive or sensitive tool calls.
/// Enforces the Hard Safety Invariant: small models (&lt;8B) cannot grant persistent/session allowances.
/// </summary>
public sealed class CliPermissionGate : IPermissionGate
{
    private readonly NexusConfig _config;
    private readonly PersistentPermissionStore _store;
    private readonly IAnsiConsole _console;
    private readonly ILogger<CliPermissionGate>? _logger;

    /// <summary>Tracks in-session (tool, pattern) pairs allowed by the user for the duration of this process.</summary>
    private readonly HashSet<(string Tool, string Pattern)> _sessionAllowed = new();

    public CliPermissionGate(
        NexusConfig config,
        PersistentPermissionStore store,
        IAnsiConsole? console = null,
        ILogger<CliPermissionGate>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _console = console ?? AnsiConsole.Console;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PermissionGateResponse> RequestAsync(
        PermissionRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tier = ToolCapabilityResolver.Resolve(_config.Models.Local.Model);
        var isFullTier = tier == ToolCallingTier.Full;

        var cwd = Directory.GetCurrentDirectory();

        // Step 1 — persistent store check (deny entries honored for all tiers; allow only for Full)
        foreach (var pattern in request.Patterns)
        {
            var entry = await _store.LookupAsync(cwd, request.ToolName, pattern, ct)
                .ConfigureAwait(false);

            if (entry is null)
                continue;

            if (string.Equals(entry.Action, "deny", StringComparison.OrdinalIgnoreCase))
                return new PermissionGateResponse(PermissionDecision.Deny, "previously denied");

            if (string.Equals(entry.Action, "allow", StringComparison.OrdinalIgnoreCase))
            {
                if (isFullTier)
                    return new PermissionGateResponse(PermissionDecision.AllowPersisted);

                // Small model: ignore stored allow (Hard Safety Invariant)
                _logger?.LogWarning(
                    "[PermissionGate] Small model — persisted allowance for {Tool} ignored",
                    request.ToolName);
            }
        }

        // Step 2 — session cache (Full tier only)
        if (isFullTier)
        {
            foreach (var pattern in request.Patterns)
            {
                if (_sessionAllowed.Contains((request.ToolName, pattern)))
                    return new PermissionGateResponse(PermissionDecision.AllowForSession);
            }
        }

        // Step 3 — config rule
        var configAction = ResolveConfigAction(request.ToolName, request.Patterns);
        if (configAction is not null)
        {
            if (string.Equals(configAction, "allow", StringComparison.OrdinalIgnoreCase))
                return new PermissionGateResponse(PermissionDecision.Allow);

            if (string.Equals(configAction, "deny", StringComparison.OrdinalIgnoreCase))
                return new PermissionGateResponse(PermissionDecision.Deny);

            // "ask" falls through to interactive prompt
        }

        // Step 4 — interactive prompt
        ct.ThrowIfCancellationRequested();

        var (decision, feedback) = PromptUser(request, tier);

        // Write state side-effects
        if (decision == PermissionDecision.AllowForSession)
        {
            foreach (var pattern in request.Patterns)
                _sessionAllowed.Add((request.ToolName, pattern));
        }
        else if (decision == PermissionDecision.AllowPersisted)
        {
            try
            {
                foreach (var pattern in request.Patterns)
                    await _store.AllowAsync(cwd, request.ToolName, pattern, ct)
                        .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "[PermissionGate] Failed to persist allowance for {Tool} — falling back to session-only",
                    request.ToolName);
                _console.MarkupLine(
                    $"[red]Warning: Could not save persistent allowance ({Markup.Escape(ex.Message)}) — falling back to session-only.[/]");
                foreach (var pattern in request.Patterns)
                    _sessionAllowed.Add((request.ToolName, pattern));
                return new PermissionGateResponse(PermissionDecision.AllowForSession);
            }
        }

        return new PermissionGateResponse(decision, feedback);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private (PermissionDecision Decision, string? Feedback) PromptUser(
        PermissionRequest request,
        ToolCallingTier tier)
    {
        try
        {
            RenderRequestPanel(request, tier);

            var prompt = BuildSelectionPrompt(request, tier);
            var choice = _console.Prompt(prompt);

            // [[s]] and [[p]] are only shown to Full-tier models; small-model prompts omit them entirely.
            return choice switch
            {
                var c when c.StartsWith("[[a]]") => (PermissionDecision.Allow, null),
                var c when c.StartsWith("[[s]]") => (PermissionDecision.AllowForSession, (string?)null),
                var c when c.StartsWith("[[p]]") => (PermissionDecision.AllowPersisted, (string?)null),
                var c when c.StartsWith("[[d]]") => (PermissionDecision.Deny, (string?)null),
                var c when c.StartsWith("[[r]]") => PromptFeedback(),
                _ => (PermissionDecision.Deny, (string?)null)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex,
                "[PermissionGate] Interactive prompt unavailable (non-interactive stdin) — denying tool call");
            return (PermissionDecision.Deny, "non-interactive prompt unavailable");
        }
    }

    private static SelectionPrompt<string> BuildSelectionPrompt(
        PermissionRequest request,
        ToolCallingTier tier)
    {
        var prompt = new SelectionPrompt<string>()
            .Title($"[bold yellow]Allow tool [cyan]{Markup.Escape(request.ToolName)}[/] on server [cyan]{Markup.Escape(request.ServerName)}[/]?[/]");

        if (tier == ToolCallingTier.Full)
        {
            prompt.AddChoices(
                "[[a]] Allow once",
                "[[s]] Allow for session",
                "[[p]] Persist for project",
                "[[d]] Deny",
                "[[r]] Reject with feedback");
        }
        else
        {
            prompt.AddChoices(
                "[[a]] Allow once",
                "[[d]] Deny",
                "[[r]] Reject with feedback");
        }

        return prompt;
    }

    private void RenderRequestPanel(PermissionRequest request, ToolCallingTier tier)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[bold]Tool:[/] {Markup.Escape(request.ToolName)}");
        sb.AppendLine($"[bold]Server:[/] {Markup.Escape(request.ServerName)}");

        if (request.Arguments is not null && request.Arguments.Count > 0)
        {
            sb.AppendLine("[bold]Arguments:[/]");
            foreach (var (key, value) in request.Arguments)
                sb.AppendLine($"  {Markup.Escape(key)} = {Markup.Escape(value?.ToString() ?? "(null)")}");
        }

        var isDestructive = !string.IsNullOrWhiteSpace(request.Rationale)
            && (request.Rationale.Contains("destructive", StringComparison.OrdinalIgnoreCase)
                || request.Rationale.Contains("delete", StringComparison.OrdinalIgnoreCase)
                || request.Rationale.Contains("overwrite", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.Rationale))
        {
            var rationaleColor = isDestructive ? "red" : "white";
            sb.AppendLine($"[bold]Rationale:[/] [{rationaleColor}]{Markup.Escape(request.Rationale)}[/]");
        }

        var headerMarkup = isDestructive
            ? "[red bold]Permission Required — DESTRUCTIVE[/]"
            : "[yellow]Permission Required[/]";

        _console.Write(new Panel(sb.ToString().TrimEnd())
            .Header(headerMarkup)
            .Border(BoxBorder.Rounded));

        // Small-model warning rendered separately so it is not buried under arguments.
        // Panel.Subtitle is not available in Spectre.Console 0.49.x (deferred to upgrade).
        if (tier != ToolCallingTier.Full)
            _console.MarkupLine("[red bold]Small model detected — session/persist allowances disabled for safety.[/]");
    }

    private (PermissionDecision Decision, string? Feedback) PromptFeedback()
    {
        // NOTE: blocking call — timeout deferred to future sprint. Non-interactive stdin already handled via try/catch on InvalidOperationException.
        var feedbackPrompt = new TextPrompt<string>(
            "[bold]> Why are you rejecting? (sent to model as denial reason; empty = 'user denied'):[/]")
            .AllowEmpty();
        var feedback = _console.Prompt(feedbackPrompt);

        if (string.IsNullOrWhiteSpace(feedback))
            feedback = "user denied";

        return (PermissionDecision.DenyWithFeedback, feedback);
    }

    private string? ResolveConfigAction(string toolName, IReadOnlyList<string> patterns)
    {
        if (!_config.Permission.Tools.TryGetValue(toolName, out var rule))
            return null;

        // Per-pattern map: first match wins
        if (rule.Patterns is not null && rule.Patterns.Count > 0)
        {
            foreach (var (globPattern, action) in rule.Patterns)
            {
                foreach (var value in patterns)
                {
                    if (MatchesGlob(globPattern, value))
                        return action;
                }
            }
        }

        // Fallback to simple action
        return rule.Action;
    }

    private static bool MatchesGlob(string pattern, string value)
        => FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: true);
}
