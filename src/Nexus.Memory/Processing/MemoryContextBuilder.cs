using Microsoft.Extensions.Logging;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;

namespace Nexus.Memory.Processing;

public class MemoryContextBuilder
{
    private readonly IKnowledgeGraph _graph;
    private readonly SemanticSearch _search;
    private readonly IEmbeddingService? _embeddingService;
    private readonly int _workingMemoryMaxTokens;
    private readonly int _relevantMemoryMaxTokens;
    private readonly int _maxRetrievalNodes;
    private readonly int _recentInteractionsFetchLimit;
    private readonly ILogger<MemoryContextBuilder>? _logger;

    public MemoryContextBuilder(
        IKnowledgeGraph graph,
        SemanticSearch search,
        IEmbeddingService? embeddingService = null,
        int workingMemoryMaxTokens = 1000,
        int relevantMemoryMaxTokens = 3000,
        int maxRetrievalNodes = 20,
        ILogger<MemoryContextBuilder>? logger = null,
        int recentInteractionsFetchLimit = 5)
    {
        _graph = graph;
        _search = search;
        _embeddingService = embeddingService;
        _workingMemoryMaxTokens = workingMemoryMaxTokens;
        _relevantMemoryMaxTokens = relevantMemoryMaxTokens;
        _maxRetrievalNodes = maxRetrievalNodes;
        _recentInteractionsFetchLimit = recentInteractionsFetchLimit;
        _logger = logger;
    }

    public async Task<Models.MemoryContext> BuildContextAsync(string query, CancellationToken cancellationToken = default)
    {
        var context = new Models.MemoryContext();

        // Parallelize: DB query and embedding generation are independent
        var entitiesTask = _graph.GetAllEntitiesAsync();
        Task<float[]?>? embeddingTask = null;
        if (_embeddingService is not null)
        {
            try
            {
                embeddingTask = _embeddingService.GenerateEmbeddingAsync(query, cancellationToken)!;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Embedding generation failed for query (sync). Will fall back to text search.");
            }
        }

        var allEntities = await entitiesTask.ConfigureAwait(false);

        // Level 1: Working memory - always included
        context.WorkingMemory = allEntities
            .Where(e => e.MemoryLevel == MemoryLevel.Working)
            .OrderByDescending(e => e.RelevanceScore)
            .ToList();

        // Level 2: Relevant memory - semantic search with embedding fallback
        List<Entity> relevantEntities;
        try
        {
            float[]? queryEmbedding = embeddingTask is not null
                ? await embeddingTask.ConfigureAwait(false)
                : null;

            if (queryEmbedding is not null)
            {
                relevantEntities = await _search.SearchByEmbeddingAsync(queryEmbedding, _maxRetrievalNodes).ConfigureAwait(false);
            }
            else
            {
                relevantEntities = await _search.SearchByTextAsync(query, _maxRetrievalNodes).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Embedding generation failed for query. Falling back to text search.");
            relevantEntities = await _search.SearchByTextAsync(query, _maxRetrievalNodes).ConfigureAwait(false);
        }

        context.RelevantMemory = relevantEntities
            .Where(e => e.MemoryLevel == MemoryLevel.Relevant)
            .Take(_maxRetrievalNodes)
            .ToList();

        // Get relations for working + relevant entities
        var entityIds = context.WorkingMemory.Concat(context.RelevantMemory).Select(e => e.Id).ToHashSet();
        var allRelations = await _graph.GetAllRelationsAsync().ConfigureAwait(false);
        context.Relations = allRelations
            .Where(r => entityIds.Contains(r.EntityId1) || entityIds.Contains(r.EntityId2))
            .ToList();

        // Recent interaction summaries
        context.RecentInteractions = await _graph.GetRecentInteractionsAsync(
            _recentInteractionsFetchLimit, cancellationToken).ConfigureAwait(false);

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
            var nameLookup = context.WorkingMemory
                .Concat(context.RelevantMemory)
                .ToDictionary(e => e.Id, e => e.Name);

            sb.AppendLine("\n## Known Relationships");
            foreach (var r in context.Relations.Take(20))
            {
                var name1 = nameLookup.GetValueOrDefault(r.EntityId1, r.EntityId1);
                var name2 = nameLookup.GetValueOrDefault(r.EntityId2, r.EntityId2);
                sb.AppendLine($"- {name1} --[{r.RelationType}]--> {name2}");
            }
        }

        if (context.RecentInteractions.Count > 0)
        {
            sb.AppendLine("\n## Recent Conversation Summaries");
            foreach (var interaction in context.RecentInteractions)
            {
                sb.AppendLine($"- [{interaction.Timestamp:yyyy-MM-dd HH:mm}] {interaction.Summary}");
            }
        }

        return sb.ToString();
    }
}
