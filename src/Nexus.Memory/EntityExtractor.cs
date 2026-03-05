using Nexus.Memory.Models;
using System.Text.Json;

namespace Nexus.Memory;

public class EntityExtractor
{
    private readonly KnowledgeGraph _graph;

    public EntityExtractor(KnowledgeGraph graph)
    {
        _graph = graph;
    }

    public async Task<List<Entity>> ExtractAndPersistAsync(string text, string? llmResponse = null)
    {
        // Simple heuristic extraction when no LLM is available
        var entities = ExtractEntitiesHeuristic(text);
        
        var persisted = new List<Entity>();
        foreach (var entity in entities)
        {
            var existing = await FindExistingEntityAsync(entity.Name, entity.Type);
            if (existing != null)
            {
                existing.MentionCount++;
                existing.LastMentioned = DateTime.UtcNow;
                await _graph.UpdateEntityAsync(existing);
                persisted.Add(existing);
            }
            else
            {
                await _graph.AddEntityAsync(entity);
                persisted.Add(entity);
            }
        }
        
        return persisted;
    }

    public async Task<List<Entity>> ExtractFromLlmResponseAsync(string jsonResponse)
    {
        try
        {
            var doc = JsonDocument.Parse(jsonResponse);
            var entities = new List<Entity>();
            
            if (doc.RootElement.TryGetProperty("entities", out var entitiesEl))
            {
                foreach (var item in entitiesEl.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    var typeStr = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "other";
                    var summary = item.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() : null;
                    
                    var type = Enum.TryParse<EntityType>(typeStr, true, out var t) ? t : EntityType.Other;
                    entities.Add(new Entity { Name = name, Type = type, TextSummary = summary });
                }
            }
            
            return entities;
        }
        catch
        {
            return new List<Entity>();
        }
    }

    private List<Entity> ExtractEntitiesHeuristic(string text)
    {
        var entities = new List<Entity>();
        
        // Extract capitalized proper nouns (simple heuristic)
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var capitalizedPhrases = new HashSet<string>();
        
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i].Trim('.', ',', '?', '!', ':');
            if (word.Length > 2 && char.IsUpper(word[0]) && !IsStopWord(word))
            {
                capitalizedPhrases.Add(word);
            }
        }
        
        foreach (var phrase in capitalizedPhrases)
        {
            entities.Add(new Entity
            {
                Name = phrase,
                Type = InferEntityType(phrase),
                TextSummary = $"Mentioned in conversation"
            });
        }
        
        return entities;
    }

    private bool IsStopWord(string word) =>
        new[] { "The", "A", "An", "I", "We", "You", "He", "She", "It", "They", "This", "That", "In", "On", "At", "For", "To", "Of", "And", "Or", "But" }
        .Contains(word, StringComparer.OrdinalIgnoreCase);

    private EntityType InferEntityType(string name)
    {
        var techKeywords = new[] { "API", "SDK", "REST", "JSON", "SQL", "HTTP", "JWT", "OAuth", "Docker", "Git", "C#", ".NET", "Python", "JavaScript" };
        if (techKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return EntityType.Technology;
        
        return EntityType.Other;
    }

    private async Task<Entity?> FindExistingEntityAsync(string name, EntityType type)
    {
        var entities = await _graph.GetEntitiesByTypeAsync(type);
        return entities.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
