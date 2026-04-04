using Microsoft.Extensions.Logging;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;

namespace Nexus.Memory.Graph;

public class EntityResolver
{
    private readonly IKnowledgeGraph _graph;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILlmClient? _llmClient;
    private readonly double _threshold;
    private readonly ILogger<EntityResolver>? _logger;

    public EntityResolver(
        IKnowledgeGraph graph,
        IEmbeddingService? embeddingService = null,
        ILlmClient? llmClient = null,
        double threshold = 0.85,
        ILogger<EntityResolver>? logger = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _embeddingService = embeddingService;
        _llmClient = llmClient;
        _threshold = threshold;
        _logger = logger;
    }

    public async Task<List<DuplicatePair>> FindDuplicatesAsync(CancellationToken ct = default)
    {
        var allEntities = await _graph.GetAllEntitiesAsync().ConfigureAwait(false);
        var withEmbeddings = allEntities
            .Where(e => e.Embedding is not null)
            .ToList();

        var duplicates = new List<DuplicatePair>();

        for (int i = 0; i < withEmbeddings.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e1 = withEmbeddings[i];
            var emb1 = SemanticSearch.ToFloatArray(e1.Embedding!);

            for (int j = i + 1; j < withEmbeddings.Count; j++)
            {
                var e2 = withEmbeddings[j];
                var emb2 = SemanticSearch.ToFloatArray(e2.Embedding!);
                var similarity = SemanticSearch.CosineSimilarity(emb1, emb2);

                if (similarity >= _threshold)
                {
                    duplicates.Add(new DuplicatePair(e1, e2, similarity));
                }
            }
        }

        duplicates.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
        return duplicates;
    }

    public async Task<Entity> MergeEntitiesAsync(DuplicatePair pair, CancellationToken ct = default)
    {
        var (e1, e2, _) = pair;
        var (survivor, duplicate) = DetermineSurvivorAndDuplicate(e1, e2);

        // Consolidate fields
        survivor.MentionCount = e1.MentionCount + e2.MentionCount;
        survivor.TextSummary = PickLongerSummary(e1.TextSummary, e2.TextSummary);
        survivor.FirstMentioned = e1.FirstMentioned < e2.FirstMentioned ? e1.FirstMentioned : e2.FirstMentioned;
        survivor.LastMentioned = e1.LastMentioned > e2.LastMentioned ? e1.LastMentioned : e2.LastMentioned;
        survivor.RelevanceScore = Math.Max(e1.RelevanceScore, e2.RelevanceScore);

        // Regenerate embedding if service available
        if (_embeddingService is not null)
        {
            try
            {
                var text = survivor.Name + " " + (survivor.TextSummary ?? "");
                var embedding = await _embeddingService.GenerateEmbeddingAsync(text, ct).ConfigureAwait(false);
                survivor.Embedding = SemanticSearch.ToByteArray(embedding);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to regenerate embedding for entity {EntityName}; continuing merge without new embedding", survivor.Name);
            }
        }

        // Re-point relations from duplicate to survivor
        await _graph.UpdateRelationEntityIdAsync(duplicate.Id, survivor.Id, ct).ConfigureAwait(false);

        // Persist survivor and delete duplicate
        await _graph.UpdateEntityAsync(survivor).ConfigureAwait(false);
        await _graph.DeleteEntityAsync(duplicate.Id, ct).ConfigureAwait(false);

        return survivor;
    }

    public async Task<bool> ConfirmDuplicateAsync(DuplicatePair pair, CancellationToken ct = default)
    {
        if (_llmClient is null)
            return true;

        try
        {
            var prompt = $"""
                Are these two entities duplicates that refer to the same thing?
                Entity 1: "{pair.Entity1.Name}" — {pair.Entity1.TextSummary ?? "(no summary)"}
                Entity 2: "{pair.Entity2.Name}" — {pair.Entity2.TextSummary ?? "(no summary)"}
                Answer with "Yes" or "No" only.
                """;

            var response = await _llmClient.GenerateAsync(prompt, ct).ConfigureAwait(false);
            return response.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LLM confirmation failed for pair ({Entity1}, {Entity2}); auto-confirming", pair.Entity1.Name, pair.Entity2.Name);
            return true;
        }
    }

    public async Task<List<Entity>> FindAndMergeAsync(bool useLlmConfirmation = true, CancellationToken ct = default)
    {
        var duplicates = await FindDuplicatesAsync(ct).ConfigureAwait(false);
        var mergedIds = new HashSet<string>();
        var keptEntities = new List<Entity>();

        foreach (var pair in duplicates)
        {
            ct.ThrowIfCancellationRequested();

            if (mergedIds.Contains(pair.Entity1.Id) || mergedIds.Contains(pair.Entity2.Id))
                continue;

            if (useLlmConfirmation)
            {
                var confirmed = await ConfirmDuplicateAsync(pair, ct).ConfigureAwait(false);
                if (!confirmed)
                    continue;
            }

            // Determine which will be the duplicate (to track its ID)
            var (_, dup) = DetermineSurvivorAndDuplicate(pair.Entity1, pair.Entity2);
            var duplicateId = dup.Id;

            var survivor = await MergeEntitiesAsync(pair, ct).ConfigureAwait(false);
            mergedIds.Add(duplicateId);
            keptEntities.Add(survivor);
        }

        return keptEntities;
    }

    private static (Entity survivor, Entity duplicate) DetermineSurvivorAndDuplicate(Entity entity1, Entity entity2)
    {
        // Higher MentionCount wins; tiebreak: earlier FirstMentioned
        return entity1.MentionCount > entity2.MentionCount
            ? (entity1, entity2)
            : entity1.MentionCount < entity2.MentionCount
                ? (entity2, entity1)
                : entity1.FirstMentioned <= entity2.FirstMentioned
                    ? (entity1, entity2)
                    : (entity2, entity1);
    }

    private static string? PickLongerSummary(string? a, string? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Length >= b.Length ? a : b;
    }
}
