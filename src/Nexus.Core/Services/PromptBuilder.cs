using Nexus.Core.Abstractions;
using Nexus.Core.Config;
using Nexus.Memory.Models;
using Nexus.Memory.Processing;

namespace Nexus.Core.Services;

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
    private readonly IToolExecutor? _toolExecutor;

    public PromptBuilder(MemoryContextBuilder memoryContextBuilder, AgentConfig agentConfig, IToolExecutor? toolExecutor = null)
    {
        _memoryContextBuilder = memoryContextBuilder;
        _agentConfig = agentConfig;
        _toolExecutor = toolExecutor;
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

        if (_toolExecutor is not null && _toolExecutor.HasTools)
        {
            builder.AppendLine();
            builder.AppendLine("# Available Tools");
            builder.AppendLine(_toolExecutor.GetToolDefinitionsForPrompt());
            builder.AppendLine();
            builder.AppendLine("# CRITICAL: How to Use Tools");
            builder.AppendLine("When the user asks you to perform an action (create a file, read a file, list a directory, etc.), you MUST use the appropriate tool.");
            builder.AppendLine("You CANNOT perform actions like creating, reading, writing, or deleting files on your own. You MUST use tools.");
            builder.AppendLine("NEVER say \"I created the file\" or \"I saved it\" without actually calling a tool first.");
            builder.AppendLine();
            builder.AppendLine("To call a tool, respond with ONLY this line and nothing else:");
            builder.AppendLine("[TOOL_CALL: {\"name\": \"tool_name\", \"arguments\": {\"param1\": \"value1\"}}]");
            builder.AppendLine();
            builder.AppendLine("Rules:");
            builder.AppendLine("1. The [TOOL_CALL: ...] line must be the ONLY content in your response. No text before or after.");
            builder.AppendLine("2. After the tool executes, you will receive the result and can then respond to the user.");
            builder.AppendLine("3. If you need to perform an action but no suitable tool exists, tell the user you cannot do it and suggest alternatives.");
            builder.AppendLine("4. ALWAYS use absolute paths (e.g. D:\\Nexus\\file.txt), NEVER relative paths (e.g. file.txt or ./file.txt).");
            builder.AppendLine("5. Use ONLY the exact parameter names listed in the tool definitions above. Do NOT invent parameter names.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("# Important Limitations");
            builder.AppendLine("You do NOT have the ability to create, read, write, or modify files on the user's system.");
            builder.AppendLine("You do NOT have access to the internet, APIs, or external services.");
            builder.AppendLine("If the user asks you to perform an action (like creating a file), provide the content and instruct them to save it manually.");
            builder.AppendLine("NEVER claim you have created, saved, or modified a file — you cannot do that without tools.");
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
