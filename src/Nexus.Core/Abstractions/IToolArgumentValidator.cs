namespace Nexus.Core.Abstractions;

/// <summary>
/// Outcome from semantic argument validation. Either the arguments are
/// valid/corrected (IsValid = true) or an error message is available
/// to feed back to the LLM (IsValid = false).
/// </summary>
public sealed class ValidationOutcome
{
    public bool IsValid { get; private init; }
    public Dictionary<string, object>? CorrectedArguments { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool WasCorrected { get; private init; }

    public static ValidationOutcome Ok(Dictionary<string, object>? args) =>
        new() { IsValid = true, CorrectedArguments = args };

    public static ValidationOutcome Corrected(Dictionary<string, object> args, string note) =>
        new() { IsValid = true, CorrectedArguments = args, WasCorrected = true, ErrorMessage = note };

    public static ValidationOutcome Fail(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}

/// <summary>
/// Semantic validation layer between ToolCallParser and tool execution.
/// Validates and corrects path arguments before the MCP call.
/// </summary>
public interface IToolArgumentValidator
{
    Task<ValidationOutcome> ValidateAsync(
        string toolName,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken = default);
}
