using Microsoft.Extensions.Logging;
using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Memory.Processing;

namespace Nexus.Core.Services;

public class ContextWindowManager
{
    public const string SummaryRole = "system";
    public const string SummaryPrefix = "[Conversation Summary]\n";

    private readonly IInteractionSummarizer _summarizer;
    private readonly PromptBuilder _promptBuilder;
    private readonly MemoryConfig _memoryConfig;
    private readonly ILogger<ContextWindowManager>? _logger;

    public ContextWindowManager(
        IInteractionSummarizer summarizer,
        PromptBuilder promptBuilder,
        MemoryConfig memoryConfig,
        ILogger<ContextWindowManager>? logger = null)
    {
        _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _memoryConfig = memoryConfig ?? throw new ArgumentNullException(nameof(memoryConfig));
        _logger = logger;
    }

    public int EstimateTokens(string systemPrompt, IReadOnlyList<ConversationMessage> history)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(history);
        var totalChars = systemPrompt.Length;
        for (int i = 0; i < history.Count; i++)
            totalChars += history[i].Role.Length + history[i].Content.Length;
        return totalChars / 4;
    }

    public async Task<bool> CompactIfNeededAsync(
        string systemPrompt,
        List<ConversationMessage> history,
        ModelProviderConfig modelConfig,
        CancellationToken cancellationToken = default)
    {
        var effectiveBudget = modelConfig.ContextWindow - modelConfig.MaxOutputTokens;
        var threshold = (int)(effectiveBudget * _memoryConfig.ContextCompactionThreshold);
        var estimatedTokens = EstimateTokens(systemPrompt, history);

        _logger?.LogDebug(
            "Context tokens: ~{Tokens} / {Threshold} ({Percent}%) | history: {Count} msgs | budget: {Budget}",
            estimatedTokens, threshold, estimatedTokens * 100 / Math.Max(threshold, 1), history.Count, effectiveBudget);

        if (estimatedTokens < threshold) return false;
        var keepCount = _memoryConfig.CompactionKeepRecentMessages;
        if (history.Count <= keepCount) return false;

        var splitIndex = history.Count - keepCount;
        var oldMessages = history.GetRange(0, splitIndex);
        var recentMessages = history.GetRange(splitIndex, keepCount);
        var concatenatedText = string.Join("\n", oldMessages.Select(m => $"{m.Role}: {m.Content}"));

        ConversationMessage? summaryMessage = null;
        try
        {
            var summaryPrompt = _promptBuilder.BuildInteractionSummaryPrompt(concatenatedText);
            var interaction = await _summarizer.SummarizeAsync(
                concatenatedText, summaryPrompt, referencedEntityIds: null, cancellationToken)
                .ConfigureAwait(false);
            summaryMessage = new ConversationMessage
            {
                Role = SummaryRole,
                Content = $"{SummaryPrefix}{interaction.Summary}"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Compaction summarization failed, falling back to truncation");
        }

        history.Clear();
        if (summaryMessage is not null) history.Add(summaryMessage);
        history.AddRange(recentMessages);

        var afterTokens = EstimateTokens(systemPrompt, history);

        _logger?.LogDebug(
            "Context compacted: {Before} → {After} tokens | summarized {Old} msgs, kept {Kept}",
            estimatedTokens, afterTokens, oldMessages.Count, recentMessages.Count);

        _logger?.LogInformation(
            "Compacted conversation: {Before} tokens -> {After} tokens, summarized {Count} messages",
            estimatedTokens, afterTokens, oldMessages.Count);
        return true;
    }
}
