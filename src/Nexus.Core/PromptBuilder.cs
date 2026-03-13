using Nexus.Core.Config;
using Nexus.Memory;
using Nexus.Memory.Models;

namespace Nexus.Core;

public class PromptBuilder
{
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["es"] = "Spanish",
        ["fr"] = "French",
        ["de"] = "German",
        ["it"] = "Italian",
        ["pt"] = "Portuguese",
        ["zh"] = "Chinese",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
    };

    private readonly MemoryContextBuilder _memoryContextBuilder;
    private readonly AgentConfig _agentConfig;

    public PromptBuilder(MemoryContextBuilder memoryContextBuilder, AgentConfig agentConfig)
    {
        _memoryContextBuilder = memoryContextBuilder;
        _agentConfig = agentConfig;
    }

    public async Task<string> BuildSystemPromptAsync(string userQuery, CancellationToken cancellationToken = default)
    {
        var context = await _memoryContextBuilder.BuildContextAsync(userQuery, cancellationToken);
        var memorySection = _memoryContextBuilder.FormatContextAsPrompt(context);

        var languageName = LanguageNames.TryGetValue(_agentConfig.Language, out var name) ? name : "English";

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"You are {_agentConfig.Name}, a personal AI agent with persistent memory.");
        builder.AppendLine($"You remember the user's projects, people, decisions and preferences over time.");
        builder.AppendLine($"IMPORTANT: Always respond in the same language the user is writing in. Match their language exactly.");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(memorySection))
        {
            builder.AppendLine("# Your Memory");
            builder.AppendLine(memorySection);
        }

        return builder.ToString();
    }

    public string BuildEntityExtractionPrompt(string conversationText)
    {
        return $$"""
            Extract named entities and their relationships from the following conversation.
            Return ONLY a valid JSON object with this exact structure:

            {
              "entities": [
                {"name": "EntityName", "type": "person|project|technology|decision|date|preference|other", "summary": "Brief description"}
              ],
              "relations": [
                {"entity1": "EntityName1", "entity2": "EntityName2", "type": "descriptive_relation_type"}
              ]
            }

            Rules:
            - IMPORTANT: ALL output MUST be in English regardless of the conversation language. Entity names, types, summaries, and relation types must all be in English.
            - Return ONLY valid JSON. No markdown code blocks, no explanation text.
            - Relation types should be descriptive lowercase strings (e.g., "works_on", "uses", "decided_to").
            - Only include relations where both entities appear in the entities array.
            - Entity names must be proper nouns or specific terms, not generic words.

            Conversation:
            {{conversationText}}
            """;
    }

    public string BuildInteractionSummaryPrompt(string conversationText)
    {
        return $"""
            Summarize the following conversation in 2-3 sentences, focusing on:
            - Key decisions made
            - Important information shared
            - Action items or next steps

            Conversation:
            {conversationText}

            IMPORTANT: Write the summary in English regardless of the conversation language.
            Return only the summary, no explanation.
            """;
    }
}
