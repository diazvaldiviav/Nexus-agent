using Microsoft.Extensions.Logging;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;
using Nexus.Memory.Graph;

namespace Nexus.Memory.Processing;

public interface IInteractionSummarizer
{
    Task<Interaction> SummarizeAsync(
        string conversationText,
        string summaryPrompt,
        List<string>? referencedEntityIds = null,
        CancellationToken cancellationToken = default);
}

public class InteractionSummarizer : IInteractionSummarizer
{
    private readonly IKnowledgeGraph _graph;
    private readonly ILlmClient? _llmClient;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<InteractionSummarizer>? _logger;

    public InteractionSummarizer(
        IKnowledgeGraph graph,
        ILlmClient? llmClient = null,
        IEmbeddingService? embeddingService = null,
        ILogger<InteractionSummarizer>? logger = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _llmClient = llmClient;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<Interaction> SummarizeAsync(
        string conversationText,
        string summaryPrompt,
        List<string>? referencedEntityIds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await GenerateSummaryAsync(conversationText, summaryPrompt, cancellationToken)
                .ConfigureAwait(false);

            byte[]? embedding = null;
            if (_embeddingService is not null)
            {
                try
                {
                    var floats = await _embeddingService.GenerateEmbeddingAsync(summary, cancellationToken)
                        .ConfigureAwait(false);
                    embedding = SemanticSearch.ToByteArray(floats);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to generate embedding for interaction summary. Persisting without embedding.");
                }
            }

            var interaction = new Interaction
            {
                Summary = summary,
                Embedding = embedding,
                ReferencedEntityIds = referencedEntityIds ?? [],
                TokenCount = summary.Length / 4
            };

            await _graph.AddInteractionAsync(interaction).ConfigureAwait(false);
            return interaction;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Interaction summarization failed entirely. Persisting fallback summary.");

            var fallback = new Interaction
            {
                Summary = "Summary unavailable",
                ReferencedEntityIds = referencedEntityIds ?? [],
                TokenCount = 0
            };

            try
            {
                await _graph.AddInteractionAsync(fallback).ConfigureAwait(false);
            }
            catch (Exception persistEx)
            {
                _logger?.LogError(persistEx, "Failed to persist fallback interaction summary.");
            }

            return fallback;
        }
    }

    private async Task<string> GenerateSummaryAsync(
        string conversationText,
        string summaryPrompt,
        CancellationToken cancellationToken)
    {
        if (_llmClient is not null)
        {
            try
            {
                var rawResponse = await _llmClient.GenerateAsync(summaryPrompt, cancellationToken)
                    .ConfigureAwait(false);
                return CleanSummary(rawResponse);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LLM summary generation failed. Falling back to heuristic.");
            }
        }

        return GenerateHeuristicSummary(conversationText);
    }

    internal static string CleanSummary(string rawResponse)
    {
        var trimmed = rawResponse.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "No summary content generated.";

        // Take first 3 sentences max
        var sentences = trimmed.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length <= 3)
            return trimmed;

        var result = string.Join(". ", sentences.Take(3));
        if (!result.EndsWith('.') && !result.EndsWith('!') && !result.EndsWith('?'))
            result += ".";
        return result;
    }

    internal static string GenerateHeuristicSummary(string conversationText)
    {
        // Find last assistant message
        var lines = conversationText.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("assistant:", StringComparison.OrdinalIgnoreCase))
            {
                var content = line["assistant:".Length..].Trim();
                if (content.Length > 200)
                    return content[..200] + "...";
                return content;
            }
        }

        // No assistant message found — take first 200 chars of conversation
        if (conversationText.Length > 200)
            return conversationText[..200] + "...";
        return conversationText;
    }
}
