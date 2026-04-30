using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;

namespace Nexus.Memory.Graph;

public class EntityExtractor
{
    private readonly IKnowledgeGraph _graph;
    private readonly ILlmClient? _llmClient;
    private readonly IEmbeddingService? _embeddingService;
    private readonly HttpClient? _geminiHttp;
    private readonly string? _geminiApiKey;
    private readonly ILogger<EntityExtractor>? _logger;

    public EntityExtractor(
        IKnowledgeGraph graph,
        ILlmClient? llmClient = null,
        IEmbeddingService? embeddingService = null,
        HttpClient? geminiHttpClient = null,
        string? geminiApiKey = null,
        ILogger<EntityExtractor>? logger = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _llmClient = llmClient;
        _embeddingService = embeddingService;
        _geminiHttp = geminiHttpClient;
        _geminiApiKey = geminiApiKey;
        _logger = logger;
    }

    private async Task<byte[]?> GenerateEmbeddingForEntityAsync(Entity entity, CancellationToken cancellationToken)
    {
        if (_embeddingService is null)
            return null;

        try
        {
            var text = entity.Name + " " + (entity.TextSummary ?? "");
            var embedding = await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
            return SemanticSearch.ToByteArray(embedding);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to generate embedding for entity '{Name}'. Entity will be saved without embedding.", entity.Name);
            return null;
        }
    }

    public virtual async Task<List<Entity>> ExtractAndPersistAsync(
        string text,
        string? extractionPrompt = null,
        CancellationToken cancellationToken = default)
    {
        ExtractionResult? result = null;

        // Level 1: Local LLM extraction
        if (extractionPrompt is not null && _llmClient is not null)
        {
            try
            {
                var rawResponse = await _llmClient.GenerateAsync(extractionPrompt, cancellationToken);
                result = TryParseExtractionJson(rawResponse);

                if (result is null)
                {
                    _logger?.LogWarning("Local LLM returned unparseable JSON. Trying cloud fallback.");

                    // Level 2: Cloud fallback (Gemini)
                    result = await TryCloudFallbackAsync(extractionPrompt, cancellationToken);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogWarning(ex, "Local LLM call failed with HTTP error. Trying cloud fallback.");
                result = await TryCloudFallbackAsync(extractionPrompt, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Local LLM call timed out. Trying cloud fallback.");
                result = await TryCloudFallbackAsync(extractionPrompt, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Local LLM returned malformed response. Trying cloud fallback.");
                result = await TryCloudFallbackAsync(extractionPrompt, cancellationToken);
            }
        }

        // If LLM extraction succeeded, persist entities and relations
        if (result is not null && result.Entities.Count > 0)
        {
            return await PersistExtractionResultAsync(result, cancellationToken);
        }

        // Level 3: Heuristic fallback
        _logger?.LogWarning("All LLM extraction paths failed or unavailable. Using heuristic fallback.");
        return await PersistHeuristicEntitiesAsync(text, cancellationToken);
    }

    internal static ExtractionResult? TryParseExtractionJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        try
        {
            var cleaned = rawResponse.Trim();

            // Strip markdown code fences
            cleaned = Regex.Replace(cleaned, @"^```(?:json)?\s*", "", RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"```\s*$", "", RegexOptions.Multiline);
            cleaned = cleaned.Trim();

            // Isolate JSON object
            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < 0 || lastBrace <= firstBrace)
                return null;

            cleaned = cleaned[firstBrace..(lastBrace + 1)];

            // Remove trailing commas before } or ]
            cleaned = Regex.Replace(cleaned, @",\s*([}\]])", "$1");

            var doc = JsonDocument.Parse(cleaned);
            var result = new ExtractionResult();

            if (doc.RootElement.TryGetProperty("entities", out var entitiesEl))
            {
                foreach (var item in entitiesEl.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var typeStr = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "other";
                    var summary = item.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() : null;

                    result.Entities.Add(new ExtractedEntity
                    {
                        Name = name,
                        Type = typeStr ?? "other",
                        Summary = summary
                    });
                }
            }

            if (doc.RootElement.TryGetProperty("relations", out var relationsEl))
            {
                foreach (var item in relationsEl.EnumerateArray())
                {
                    var entity1 = item.TryGetProperty("entity1", out var e1El) ? e1El.GetString() : null;
                    var entity2 = item.TryGetProperty("entity2", out var e2El) ? e2El.GetString() : null;
                    var relType = item.TryGetProperty("type", out var rtEl) ? rtEl.GetString() : null;

                    if (string.IsNullOrWhiteSpace(entity1) || string.IsNullOrWhiteSpace(entity2)
                        || string.IsNullOrWhiteSpace(relType))
                        continue;

                    result.Relations.Add(new ExtractedRelation
                    {
                        Entity1 = entity1,
                        Entity2 = entity2,
                        Type = relType
                    });
                }
            }

            return result.Entities.Count > 0 ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ExtractionResult?> TryCloudFallbackAsync(
        string extractionPrompt,
        CancellationToken cancellationToken)
    {
        if (_geminiHttp is null || string.IsNullOrEmpty(_geminiApiKey))
            return null;

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key={_geminiApiKey}";

            var request = new
            {
                contents = new[] { new { parts = new[] { new { text = extractionPrompt } } } },
                generationConfig = new { responseMimeType = "application/json" }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _geminiHttp.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text is not null ? TryParseExtractionJson(text) : null;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Gemini cloud fallback HTTP error. Falling back to heuristic.");
            return null;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Gemini cloud fallback timed out. Falling back to heuristic.");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Gemini cloud fallback failed unexpectedly. Falling back to heuristic.");
            return null;
        }
    }

    private async Task<List<Entity>> PersistExtractionResultAsync(
        ExtractionResult result,
        CancellationToken cancellationToken)
    {
        var persisted = new List<Entity>();
        var entityMap = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var extracted in result.Entities)
        {
            try
            {
                var entityType = Enum.TryParse<EntityType>(extracted.Type, true, out var t) ? t : EntityType.Other;
                var existing = await _graph.GetEntityByNameAsync(extracted.Name, cancellationToken);

                if (existing is not null)
                {
                    existing.MentionCount++;
                    existing.LastMentioned = DateTime.UtcNow;

                    var summaryChanged = false;
                    if (extracted.Summary is not null
                        && extracted.Summary.Length > (existing.TextSummary?.Length ?? 0))
                    {
                        existing.TextSummary = extracted.Summary;
                        summaryChanged = true;
                    }

                    if (existing.Type == EntityType.Other && entityType != EntityType.Other)
                    {
                        existing.Type = entityType;
                    }

                    if (summaryChanged)
                    {
                        existing.Embedding = await GenerateEmbeddingForEntityAsync(existing, cancellationToken).ConfigureAwait(false);
                    }

                    await _graph.UpdateEntityAsync(existing);
                    persisted.Add(existing);
                    entityMap[extracted.Name] = existing;
                }
                else
                {
                    var newEntity = new Entity
                    {
                        Name = extracted.Name,
                        Type = entityType,
                        TextSummary = extracted.Summary
                    };

                    newEntity.Embedding = await GenerateEmbeddingForEntityAsync(newEntity, cancellationToken).ConfigureAwait(false);
                    await _graph.AddEntityAsync(newEntity);
                    persisted.Add(newEntity);
                    entityMap[extracted.Name] = newEntity;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist entity '{Name}'. Continuing with next.", extracted.Name);
            }
        }

        await CreateRelationsAsync(result.Relations, entityMap, cancellationToken);

        return persisted;
    }

    private async Task CreateRelationsAsync(
        List<ExtractedRelation> relations,
        Dictionary<string, Entity> entityMap,
        CancellationToken cancellationToken)
    {
        foreach (var relation in relations)
        {
            try
            {
                if (!entityMap.TryGetValue(relation.Entity1, out var entity1)
                    || !entityMap.TryGetValue(relation.Entity2, out var entity2))
                    continue;

                if (entity1.Id == entity2.Id)
                    continue;

                // Check for duplicate relations
                var existingRelations = await _graph.GetRelationsForEntityAsync(entity1.Id);
                var isDuplicate = existingRelations.Any(r =>
                    r.EntityId2 == entity2.Id
                    && string.Equals(r.RelationType, relation.Type, StringComparison.OrdinalIgnoreCase));

                if (isDuplicate)
                    continue;

                await _graph.AddRelationAsync(new Relation
                {
                    EntityId1 = entity1.Id,
                    EntityId2 = entity2.Id,
                    RelationType = relation.Type,
                    Confidence = 1.0
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create relation '{Type}' between '{E1}' and '{E2}'. Continuing.",
                    relation.Type, relation.Entity1, relation.Entity2);
            }
        }
    }

    private async Task<List<Entity>> PersistHeuristicEntitiesAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var entities = ExtractEntitiesHeuristic(text);
        var persisted = new List<Entity>();

        foreach (var entity in entities)
        {
            try
            {
                var existing = await _graph.GetEntityByNameAsync(entity.Name, cancellationToken);
                if (existing is not null)
                {
                    existing.MentionCount++;
                    existing.LastMentioned = DateTime.UtcNow;
                    await _graph.UpdateEntityAsync(existing);
                    persisted.Add(existing);
                }
                else
                {
                    entity.Embedding = await GenerateEmbeddingForEntityAsync(entity, cancellationToken).ConfigureAwait(false);
                    await _graph.AddEntityAsync(entity);
                    persisted.Add(entity);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist heuristic entity '{Name}'. Continuing.", entity.Name);
            }
        }

        return persisted;
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

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "A", "An", "I", "We", "You", "He", "She", "It", "They",
        "This", "That", "In", "On", "At", "For", "To", "Of", "And", "Or", "But"
    };

    private static bool IsStopWord(string word) => StopWords.Contains(word);

    private static readonly HashSet<string> TechKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "API", "SDK", "REST", "JSON", "SQL", "HTTP", "JWT", "OAuth",
        "Docker", "Git", "C#", ".NET", "Python", "JavaScript"
    };

    private static EntityType InferEntityType(string name)
    {
        if (TechKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return EntityType.Technology;

        return EntityType.Other;
    }
}

internal sealed class ExtractionResult
{
    public List<ExtractedEntity> Entities { get; set; } = new();
    public List<ExtractedRelation> Relations { get; set; } = new();
}

internal sealed class ExtractedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "other";
    public string? Summary { get; set; }
}

internal sealed class ExtractedRelation
{
    public string Entity1 { get; set; } = string.Empty;
    public string Entity2 { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
