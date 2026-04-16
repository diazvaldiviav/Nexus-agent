using System.Text;
using System.Text.Json;

namespace Nexus.Connectors.ToolFiltering;

public sealed class ToolPromptFormatter
{
    private readonly IToolComplexityClassifier _classifier;

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

    public ToolPromptFormatter(IToolComplexityClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public string Format(IEnumerable<ToolDefinition> tools, string? modelName)
    {
        var toolList = tools.ToList();
        if (toolList.Count == 0)
            return string.Empty;

        var modelTier = ToolCapabilityResolver.Resolve(modelName);

        var classified = toolList
            .Select(t => (tool: t, score: _classifier.Classify(t)))
            .ToList();

        var included = new List<(ToolDefinition tool, ToolComplexityScore score, string? hint)>();
        var excluded = new List<ToolDefinition>();

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
                    excluded.Add(tool);
                    break;
                case (ToolCallingTier.Limited, ToolComplexityTier.Moderate):
                    included.Add((tool, score, LimitedModerateHint));
                    break;
                case (ToolCallingTier.Limited, ToolComplexityTier.Simple):
                    included.Add((tool, score, null));
                    break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available tools:");

        foreach (var (tool, score, hint) in included)
        {
            RenderTool(sb, tool);
            if (hint is not null)
                sb.AppendLine($"    {hint}");
        }

        if (excluded.Count > 0)
        {
            sb.AppendLine();
            var includedPairs = included
                .Select(x => (x.tool, x.score))
                .ToList();
            foreach (var tool in excluded)
                sb.AppendLine(BuildExclusionHint(tool, includedPairs));
        }

        return sb.ToString();
    }

    private static void RenderTool(StringBuilder sb, ToolDefinition tool)
    {
        // Copied from ToolRegistry.cs lines 224-256. Keep in sync.
        sb.AppendLine($"- {tool.Name}: {tool.Description}");

        if (!tool.InputSchema.HasValue)
            return;

        var schema = tool.InputSchema.Value;
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var reqArray) &&
            reqArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArray.EnumerateArray())
            {
                var name = item.GetString();
                if (name is not null) required.Add(name);
            }
        }

        if (schema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var paramType = prop.Value.TryGetProperty("type", out var t)
                    ? t.GetString() ?? "any"
                    : "any";
                var desc = prop.Value.TryGetProperty("description", out var d)
                    ? d.GetString() ?? ""
                    : "";
                var reqTag = required.Contains(prop.Name) ? "REQUIRED" : "optional";

                sb.AppendLine($"    {prop.Name} ({paramType}, {reqTag}): {desc}");
            }
        }
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
