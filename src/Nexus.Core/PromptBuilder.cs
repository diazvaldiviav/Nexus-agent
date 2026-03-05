using Nexus.Core.Config;
using Nexus.Memory;
using Nexus.Memory.Models;

namespace Nexus.Core;

public class PromptBuilder
{
    private readonly MemoryContextBuilder _memoryContextBuilder;
    private readonly AgentConfig _agentConfig;

    public PromptBuilder(MemoryContextBuilder memoryContextBuilder, AgentConfig agentConfig)
    {
        _memoryContextBuilder = memoryContextBuilder;
        _agentConfig = agentConfig;
    }

    public async Task<string> BuildSystemPromptAsync(string userQuery, float[]? queryEmbedding = null)
    {
        var context = await _memoryContextBuilder.BuildContextAsync(userQuery, queryEmbedding);
        var memorySection = _memoryContextBuilder.FormatContextAsPrompt(context);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are {_agentConfig.Name}, a personal AI agent with persistent memory.");
        sb.AppendLine($"You remember the user's projects, people, decisions and preferences over time.");
        sb.AppendLine($"Always respond in {(_agentConfig.Language == "es" ? "Spanish" : "English")}.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(memorySection))
        {
            sb.AppendLine("# Your Memory");
            sb.AppendLine(memorySection);
        }

        return sb.ToString();
    }

    public string BuildEntityExtractionPrompt(string conversationText)
    {
        return $"""
            Extract named entities from the following conversation text.
            Return a JSON object with an "entities" array. Each entity should have:
            - "name": the entity name
            - "type": one of [person, project, technology, decision, date, preference, other]
            - "summary": a brief description (1-2 sentences)

            Conversation:
            {conversationText}

            Return only valid JSON, no explanation.
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

            Return only the summary, no explanation.
            """;
    }
}
