using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexus.Connectors.ToolFiltering;

/// <summary>
/// Formats tool definitions for LLM prompts, filtering and annotating tools
/// based on the model's capability tier.
/// </summary>
public sealed class ToolPromptFormatter
{
    private readonly IToolComplexityClassifier _classifier;
    private readonly ILogger<ToolPromptFormatter>? _logger;

    private const string LimitedModerateHint =
        "(Prefer simpler alternatives when possible.)";
    private const string CapableComplexHint =
        "(This tool takes nested arguments — double-check your JSON.)";

    private static readonly Dictionary<string, string> WorkflowOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["edit_file"]  = "read_text_file → modify content → write_file",
        ["multi_edit"] = "read_text_file → modify content → write_file",
        ["patch_file"] = "read_text_file → modify content → write_file",
    };

    public ToolPromptFormatter(IToolComplexityClassifier classifier, ILogger<ToolPromptFormatter>? logger = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _logger = logger;
    }

    /// <summary>
    /// Formats the given tools into a prompt string filtered for the model's capability tier.
    /// </summary>
    public string Format(IEnumerable<ToolDefinition> tools, string? modelName)
    {
        var toolList = tools.ToList();
        if (toolList.Count == 0)
            return string.Empty;

        var modelTier = ToolCapabilityResolver.Resolve(modelName);
        _logger?.LogInformation("Tool filtering: model '{ModelName}' resolved to tier {Tier}", modelName, modelTier);

        // ChatOnly tier: model is too small to reliably emit tool-call JSON.
        // Return empty so the planner skip path in AgentService kicks in and the
        // legacy chat loop runs without any tools exposed to the prompt.
        if (modelTier == ToolCallingTier.ChatOnly)
        {
            _logger?.LogInformation(
                "Tool filtering: ChatOnly tier — suppressing all {Count} tool definitions",
                toolList.Count);
            return string.Empty;
        }

        var classified = toolList
            .Select(t => (tool: t, score: _classifier.Classify(t)))
            .ToList();

        var included = new List<(ToolDefinition tool, ToolComplexityScore score, string? hint)>();
        var excluded = new List<(ToolDefinition tool, ToolComplexityScore score)>();

        foreach (var (tool, score) in classified)
        {
            switch (modelTier, score.Tier)
            {
                case (ToolCallingTier.Full, _):
                    included.Add((tool, score, null));
                    break;
                case (ToolCallingTier.Capable, ToolComplexityTier.Complex):
                    included.Add((tool, score, CapableComplexHint));
                    break;
                case (ToolCallingTier.Capable, _):
                    included.Add((tool, score, null));
                    break;
                case (ToolCallingTier.Limited, ToolComplexityTier.Complex):
                    excluded.Add((tool, score));
                    break;
                case (ToolCallingTier.Limited, ToolComplexityTier.Moderate):
                    included.Add((tool, score, LimitedModerateHint));
                    break;
                case (ToolCallingTier.Limited, ToolComplexityTier.Simple):
                    included.Add((tool, score, null));
                    break;
            }
        }

        _logger?.LogInformation("Tool filtering: {Included} included, {Excluded} excluded",
            included.Count, excluded.Count);

        foreach (var (tool, score) in excluded)
        {
            _logger?.LogDebug("Tool '{Name}' excluded: tier={Tier}, score={Score:F2}",
                tool.Name, score.Tier, score.Score);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available tools:");

        foreach (var (tool, score, hint) in included)
        {
            ToolRegistry.RenderToolToStringBuilder(sb, tool);
            if (hint is not null)
                sb.AppendLine($"    {hint}");
        }

        if (excluded.Count > 0)
        {
            sb.AppendLine();
            var includedPairs = included
                .Select(x => (x.tool, x.score))
                .ToList();
            foreach (var (tool, _) in excluded)
                sb.AppendLine(BuildExclusionHint(tool, includedPairs));
        }

        return sb.ToString();
    }

    private static string BuildExclusionHint(
        ToolDefinition excluded,
        IReadOnlyList<(ToolDefinition tool, ToolComplexityScore score)> included)
    {
        // Priority 1: Hardcoded workflow override
        if (WorkflowOverrides.TryGetValue(excluded.Name, out var workflow))
            return $"Tool '{excluded.Name}' hidden. Recommended workflow: {workflow}";

        // Priority 2: Auto-discover Simple tools from same MCP server
        var sameServerSimple = included
            .Where(t => string.Equals(t.tool.ServerName, excluded.ServerName, StringComparison.Ordinal)
                      && t.score.Tier == ToolComplexityTier.Simple)
            .Select(t => t.tool.Name)
            .ToList();

        if (sameServerSimple.Count > 0)
            return $"Tool '{excluded.Name}' hidden. Simple tools from same server: "
                 + string.Join(", ", sameServerSimple);

        // Priority 3: No alternatives available
        return $"Tool '{excluded.Name}' is not available for this model due to complex arguments.";
    }
}
