using Nexus.Core.Models;

namespace Nexus.Core.Abstractions;

/// <summary>
/// Generates a step-by-step <see cref="ToolPlan"/> for a given user message by
/// consulting the local LLM and matching each plan step to the available tool definitions.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must follow a strict graceful-degradation contract:
/// <list type="bullet">
///   <item><description>
///     Returns <see langword="null"/> immediately when
///     <c>McpConfig.ToolPlanningEnabled</c> is <see langword="false"/>.
///   </description></item>
///   <item><description>
///     Returns <see langword="null"/> when <paramref name="toolDefinitionsForPrompt"/>
///     is <see langword="null"/>, empty, or whitespace.
///   </description></item>
///   <item><description>
///     Returns <see langword="null"/> on any LLM failure (logs a warning).
///     The caller must fall through to the existing tool loop unchanged.
///   </description></item>
///   <item><description>
///     Returns <see langword="null"/> when the planner LLM call exceeds
///     <c>McpConfig.ToolPlanningTimeoutSeconds</c> (internal deadline, not caller cancellation).
///   </description></item>
///   <item><description>
///     Returns <see langword="null"/> when no valid steps can be parsed from the LLM output.
///   </description></item>
///   <item><description>
///     A non-null <see cref="ToolPlan"/> may still contain entries whose
///     <see cref="ToolPlanStep.MatchedToolName"/> is <see langword="null"/> (no tool passed the
///     similarity threshold). The executor skips such steps — it is not the planner's job to
///     suppress the plan when every step is unmatched.
///   </description></item>
///   <item><description>
///     <see cref="OperationCanceledException"/> is re-thrown — cancellation always propagates.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public interface IToolPlanner
{
    /// <summary>
    /// Generates a <see cref="ToolPlan"/> for <paramref name="userMessage"/> using the
    /// available tool definitions, or returns <see langword="null"/> on failure or when
    /// the feature gate is inactive.
    /// </summary>
    /// <param name="userMessage">The raw user message for which a plan should be produced.</param>
    /// <param name="toolDefinitionsForPrompt">
    /// Tool definitions formatted for inclusion in a prompt
    /// (e.g. from <c>IToolExecutor.GetToolDefinitionsForPrompt(modelName)</c>).
    /// </param>
    /// <param name="ct">Cancellation token honored on every async operation.</param>
    /// <returns>
    /// A <see cref="ToolPlan"/> containing the matched steps, or <see langword="null"/>
    /// if planning is disabled, no tools are available, or a non-cancellation failure occurs.
    /// </returns>
    Task<ToolPlan?> GeneratePlanAsync(
        string userMessage,
        string toolDefinitionsForPrompt,
        CancellationToken ct = default);
}
