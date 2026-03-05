using Nexus.Memory.Models;

namespace Nexus.Memory;

public class MemoryContextBuilder
{
    private readonly KnowledgeGraph _graph;
    private readonly SemanticSearch _search;
    private readonly int _workingMemoryMaxTokens;
    private readonly int _relevantMemoryMaxTokens;
    private readonly int _maxRetrievalNodes;

    public MemoryContextBuilder(
        KnowledgeGraph graph,
        SemanticSearch search,
        int workingMemoryMaxTokens = 1000,
        int relevantMemoryMaxTokens = 3000,
        int maxRetrievalNodes = 20)
    {
        _graph = graph;
        _search = search;
        _workingMemoryMaxTokens = workingMemoryMaxTokens;
        _relevantMemoryMaxTokens = relevantMemoryMaxTokens;
        _maxRetrievalNodes = maxRetrievalNodes;
    }

    public async Task<Models.MemoryContext> BuildContextAsync(string query, float[]? queryEmbedding = null)
    {
        var context = new Models.MemoryContext();
        
        // Level 1: Working memory - always included
        var allEntities = await _graph.GetAllEntitiesAsync();
        context.WorkingMemory = allEntities
            .Where(e => e.MemoryLevel == MemoryLevel.Working)
            .OrderByDescending(e => e.RelevanceScore)
            .ToList();

        // Level 2: Relevant memory - semantic search
        List<Entity> relevantEntities;
        if (queryEmbedding != null)
            relevantEntities = await _search.SearchByEmbeddingAsync(queryEmbedding, _maxRetrievalNodes);
        else
            relevantEntities = await _search.SearchByTextAsync(query, _maxRetrievalNodes);

        context.RelevantMemory = relevantEntities
            .Where(e => e.MemoryLevel == MemoryLevel.Relevant)
            .Take(_maxRetrievalNodes)
            .ToList();

        // Get relations for working + relevant entities
        var entityIds = context.WorkingMemory.Concat(context.RelevantMemory).Select(e => e.Id).ToHashSet();
        var allRelations = await _graph.GetAllRelationsAsync();
        context.Relations = allRelations
            .Where(r => entityIds.Contains(r.EntityId1) || entityIds.Contains(r.EntityId2))
            .ToList();

        // Estimate token count (rough: 4 chars per token)
        var contextText = string.Join(" ", 
            context.WorkingMemory.Select(e => e.Name + " " + e.TextSummary).Concat(
            context.RelevantMemory.Select(e => e.Name + " " + e.TextSummary)));
        context.TotalTokenEstimate = contextText.Length / 4;

        return context;
    }

    public string FormatContextAsPrompt(Models.MemoryContext context)
    {
        var sb = new System.Text.StringBuilder();
        
        if (context.WorkingMemory.Count > 0)
        {
            sb.AppendLine("## Working Memory (Always Active)");
            foreach (var e in context.WorkingMemory)
            {
                sb.AppendLine($"- [{e.Type}] {e.Name}: {e.TextSummary ?? "No summary"}");
            }
        }

        if (context.RelevantMemory.Count > 0)
        {
            sb.AppendLine("\n## Relevant Context");
            foreach (var e in context.RelevantMemory)
            {
                sb.AppendLine($"- [{e.Type}] {e.Name} (score: {e.RelevanceScore:F2}): {e.TextSummary ?? "No summary"}");
            }
        }

        if (context.Relations.Count > 0)
        {
            sb.AppendLine("\n## Known Relationships");
            foreach (var r in context.Relations.Take(20))
            {
                sb.AppendLine($"- {r.EntityId1} --[{r.RelationType}]--> {r.EntityId2}");
            }
        }

        return sb.ToString();
    }
}
