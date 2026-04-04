using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;

namespace Nexus.Memory.Processing;

public class MemoryCompressor
{
    private readonly IKnowledgeGraph _graph;
    private readonly string _archivePath;
    private readonly int _archiveThresholdDays;
    private readonly ILlmClient? _llmClient;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<MemoryCompressor>? _logger;

    private const int WeeklyCompressionAgeDays = 7;
    private const int MonthlyCompressionAgeDays = 30;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public MemoryCompressor(
        IKnowledgeGraph graph,
        string archivePath,
        int archiveThresholdDays = 90,
        ILlmClient? llmClient = null,
        IEmbeddingService? embeddingService = null,
        ILogger<MemoryCompressor>? logger = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _archiveThresholdDays = archiveThresholdDays;
        _llmClient = llmClient;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<int> ArchiveStaleEntitiesAsync(CancellationToken ct = default)
    {
        try
        {
            var archiveEntities = await _graph.GetEntitiesByLevelAsync(MemoryLevel.Archive, ct)
                .ConfigureAwait(false);

            var cutoff = DateTime.UtcNow.AddDays(-_archiveThresholdDays);
            var staleEntities = archiveEntities
                .Where(e => e.LastMentioned < cutoff)
                .ToList();

            if (staleEntities.Count == 0)
            {
                _logger?.LogDebug("No stale Archive-level entities found for archival");
                return 0;
            }

            // Map entities to DTOs with their relations
            var archivedEntities = new List<ArchivedEntity>();
            foreach (var entity in staleEntities)
            {
                ct.ThrowIfCancellationRequested();

                var relations = await _graph.GetRelationsForEntityAsync(entity.Id, ct)
                    .ConfigureAwait(false);

                var archivedRelations = relations.Select(r => new ArchivedRelation
                {
                    Id = r.Id,
                    EntityId1 = r.EntityId1,
                    EntityId2 = r.EntityId2,
                    RelationType = r.RelationType,
                    Context = r.Context,
                    Timestamp = r.Timestamp,
                    Confidence = r.Confidence
                }).ToList();

                archivedEntities.Add(new ArchivedEntity
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    Type = entity.Type.ToString(),
                    TextSummary = entity.TextSummary,
                    Embedding = entity.Embedding is not null
                        ? Convert.ToBase64String(entity.Embedding)
                        : null,
                    FirstMentioned = entity.FirstMentioned,
                    LastMentioned = entity.LastMentioned,
                    MentionCount = entity.MentionCount,
                    RelevanceScore = entity.RelevanceScore,
                    Relations = archivedRelations
                });
            }

            // Build archive file
            var archiveFile = new ArchiveFile
            {
                ArchivedAt = DateTime.UtcNow,
                Entities = archivedEntities
            };

            // Write to disk
            Directory.CreateDirectory(_archivePath);
            var fileName = $"archive-{DateTime.UtcNow:yyyy-MM-dd}.json";
            var filePath = Path.Combine(_archivePath, fileName);

            // If file exists for today, read and merge (deduplicate by Id)
            if (File.Exists(filePath))
            {
                var existingJson = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var existingFile = JsonSerializer.Deserialize<ArchiveFile>(existingJson, JsonOptions);
                if (existingFile?.Entities is not null)
                {
                    var existingIds = new HashSet<string>(
                        existingFile.Entities.Select(e => e.Id));
                    var newEntities = archiveFile.Entities
                        .Where(e => !existingIds.Contains(e.Id))
                        .ToList();
                    existingFile.Entities.AddRange(newEntities);
                    archiveFile = existingFile;
                }
            }

            // Atomic write: serialize to .tmp, then move
            var tmpPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(archiveFile, JsonOptions);
            await File.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);

            _logger?.LogInformation(
                "Archived {Count} stale entities to {FilePath}",
                staleEntities.Count, filePath);

            // Delete archived entities from the graph
            var deletedCount = 0;
            foreach (var entity in staleEntities)
            {
                try
                {
                    await _graph.DeleteEntityAsync(entity.Id, ct).ConfigureAwait(false);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "Failed to delete entity {EntityId} after archival",
                        entity.Id);
                }
            }

            _logger?.LogInformation(
                "Deleted {DeletedCount}/{TotalCount} archived entities from graph",
                deletedCount, staleEntities.Count);

            return staleEntities.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to archive stale entities");
            return 0;
        }
    }

    public async Task<int> CompressSummariesAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-WeeklyCompressionAgeDays);
            var interactions = await _graph.GetInteractionsOlderThanAsync(cutoff, ct)
                .ConfigureAwait(false);

            if (interactions.Count == 0)
            {
                _logger?.LogDebug("No interactions older than {Days} days to compress", WeeklyCompressionAgeDays);
                return 0;
            }

            var monthlyCutoff = DateTime.UtcNow.AddDays(-MonthlyCompressionAgeDays);

            // Partition into monthly (>30 days) and weekly (7-30 days) buckets
            var monthlyGroups = interactions
                .Where(i => i.Timestamp < monthlyCutoff)
                .GroupBy(i => (i.Timestamp.Year, i.Timestamp.Month))
                .Where(g => g.Count() > 1)
                .ToList();

            var weeklyGroups = interactions
                .Where(i => i.Timestamp >= monthlyCutoff)
                .GroupBy(i => GetIsoWeek(i.Timestamp))
                .Where(g => g.Count() > 1)
                .ToList();

            var totalReplaced = 0;

            foreach (var group in monthlyGroups)
            {
                var replaced = await CompressGroupAsync(
                    group.ToList(),
                    $"Monthly summary ({group.Key.Year}-{group.Key.Month:D2})",
                    ct).ConfigureAwait(false);
                totalReplaced += replaced;
            }

            foreach (var group in weeklyGroups)
            {
                var replaced = await CompressGroupAsync(
                    group.ToList(),
                    $"Weekly summary ({group.Key.Year}-W{group.Key.Week:D2})",
                    ct).ConfigureAwait(false);
                totalReplaced += replaced;
            }

            _logger?.LogInformation("Compressed {Count} interactions into summaries", totalReplaced);
            return totalReplaced;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to compress summaries");
            return 0;
        }
    }

    private async Task<int> CompressGroupAsync(
        List<Interaction> group,
        string label,
        CancellationToken ct)
    {
        // Concatenate summaries with time labels
        var concatenated = string.Join("\n", group.Select(i =>
            $"[{i.Timestamp:yyyy-MM-dd HH:mm}] {i.Summary}"));

        // Attempt LLM re-summarization
        string compressedSummary;
        if (_llmClient is not null)
        {
            try
            {
                var prompt = $"Summarize these interaction summaries into a single concise paragraph:\n\n{concatenated}";
                compressedSummary = await _llmClient.GenerateAsync(prompt, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LLM re-summarization failed, falling back to concatenation");
                compressedSummary = concatenated.Length > 500
                    ? concatenated[..500]
                    : concatenated;
            }
        }
        else
        {
            compressedSummary = concatenated.Length > 500
                ? concatenated[..500]
                : concatenated;
        }

        // Merge referenced entity IDs (union, distinct)
        var mergedEntityIds = group
            .SelectMany(i => i.ReferencedEntityIds)
            .Distinct()
            .ToList();

        // Use earliest timestamp from group
        var earliestTimestamp = group.Min(i => i.Timestamp);

        // Generate embedding if service available
        byte[]? embedding = null;
        if (_embeddingService is not null)
        {
            try
            {
                var floatEmbedding = await _embeddingService.GenerateEmbeddingAsync(compressedSummary, ct)
                    .ConfigureAwait(false);
                embedding = SemanticSearch.ToByteArray(floatEmbedding);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to generate embedding for compressed summary");
            }
        }

        // Add compressed interaction
        var compressed = new Interaction
        {
            Summary = $"{label}: {compressedSummary}",
            Embedding = embedding,
            ReferencedEntityIds = mergedEntityIds,
            Timestamp = earliestTimestamp,
            TokenCount = group.Sum(i => i.TokenCount)
        };
        await _graph.AddInteractionAsync(compressed).ConfigureAwait(false);

        // Delete originals
        foreach (var original in group)
        {
            try
            {
                await _graph.DeleteInteractionAsync(original.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete original interaction {Id}", original.Id);
            }
        }

        return group.Count;
    }

    private static (int Year, int Week) GetIsoWeek(DateTime date) =>
        (ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date));
}
