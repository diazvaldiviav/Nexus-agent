using Nexus.Core.Config;
using Nexus.Core.Models;
using Nexus.Core.Services;
using Nexus.Memory.Graph;
using Nexus.Memory.Models;
using Nexus.Memory.Processing;

namespace Nexus.Core.Tests;

public class ContextWindowManagerTests
{
    private readonly StubInteractionSummarizer _stubSummarizer;
    private readonly ContextWindowManager _manager;
    private readonly ModelProviderConfig _modelConfig;

    public ContextWindowManagerTests()
    {
        _stubSummarizer = new StubInteractionSummarizer();

        var memoryConfig = new MemoryConfig
        {
            ContextCompactionThreshold = 0.80,
            CompactionKeepRecentMessages = 4
        };

        _modelConfig = new ModelProviderConfig
        {
            ContextWindow = 200,
            MaxOutputTokens = 50
        };

        // PromptBuilder needs MemoryContextBuilder + AgentConfig.
        // MemoryContextBuilder needs IKnowledgeGraph + SemanticSearch.
        // These are never called during compaction — only BuildInteractionSummaryPrompt is used,
        // which is a pure string method that does not touch any dependencies.
        var search = new SemanticSearch("DataSource=:memory:");
        var memoryContextBuilder = new MemoryContextBuilder(
            new StubKnowledgeGraph(), search);
        var promptBuilder = new PromptBuilder(memoryContextBuilder, new AgentConfig());

        _manager = new ContextWindowManager(_stubSummarizer, promptBuilder, memoryConfig);
    }

    [Fact]
    public void EstimateTokens_ReturnsCharsDividedByFour()
    {
        // Arrange
        var prompt = new string('p', 400);
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = new string('a', 200) },
            new() { Role = "assistant", Content = new string('b', 300) }
        };
        // Total chars: 400 (prompt) + 4 ("user") + 200 + 9 ("assistant") + 300 = 913
        var expectedTokens = 913 / 4; // 228

        // Act
        var result = _manager.EstimateTokens(prompt, history);

        // Assert
        Assert.Equal(expectedTokens, result);
    }

    [Fact]
    public async Task CompactIfNeeded_BelowThreshold_ReturnsFalse()
    {
        // Arrange — effective budget = 200 - 50 = 150, threshold = 150 * 0.80 = 120 tokens
        // Each message: role + content chars. Keep total well below 120*4 = 480 chars.
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "hi" },
            new() { Role = "assistant", Content = "hello" }
        };
        var originalCount = history.Count;

        // Act
        var result = await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert
        Assert.False(result);
        Assert.Equal(originalCount, history.Count);
    }

    [Fact]
    public async Task CompactIfNeeded_AboveThreshold_CompactsHistory()
    {
        // Arrange — need > 120 tokens = > 480 chars total (with empty system prompt)
        var history = CreateHistory(10, charsPerMessage: 100);
        // 10 messages * ~(4-9 role + 100 content) ~ 1040-1090 chars => ~260-272 tokens > 120

        // Act
        var result = await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert
        Assert.True(result);
        // keepRecent=4 + 1 summary message = 5
        Assert.Equal(5, history.Count);
    }

    [Fact]
    public async Task CompactIfNeeded_KeepsRecentMessages()
    {
        // Arrange
        var history = CreateHistory(10, charsPerMessage: 100);
        // Save the last 4 messages content for comparison
        var expectedRecent = history.Skip(6).Select(m => m.Content).ToList();

        // Act
        await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert — last 4 messages in compacted history should match originals
        var actualRecent = history.Skip(1).Select(m => m.Content).ToList(); // skip summary
        Assert.Equal(expectedRecent, actualRecent);
    }

    [Fact]
    public async Task CompactIfNeeded_SummaryMessageFormat()
    {
        // Arrange
        _stubSummarizer.SummaryToReturn = new Interaction { Summary = "Test summary" };
        var history = CreateHistory(10, charsPerMessage: 100);

        // Act
        await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert
        var summaryMsg = history[0];
        Assert.Equal(ContextWindowManager.SummaryRole, summaryMsg.Role);
        Assert.StartsWith(ContextWindowManager.SummaryPrefix, summaryMsg.Content);
        Assert.Contains("Test summary", summaryMsg.Content);
    }

    [Fact]
    public async Task CompactIfNeeded_SummarizerFails_FallsBackToTruncation()
    {
        // Arrange
        _stubSummarizer.ThrowOnSummarize = true;
        var history = CreateHistory(10, charsPerMessage: 100);

        // Act
        var result = await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert — no summary message, only keepRecent=4 messages
        Assert.True(result);
        Assert.Equal(4, history.Count);
        Assert.DoesNotContain(history, m => m.Role == "system");
    }

    [Fact]
    public async Task CompactIfNeeded_ReCompactsPreviousSummary()
    {
        // Arrange — start with a summary message followed by enough messages to trigger compaction
        var history = new List<ConversationMessage>
        {
            new() { Role = "system", Content = "[Conversation Summary]\nOld summary content" }
        };
        history.AddRange(CreateHistory(10, charsPerMessage: 100));

        // Act
        await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert — the summarizer should have received the old summary in its input
        Assert.NotNull(_stubSummarizer.LastConversationText);
        Assert.Contains("Old summary content", _stubSummarizer.LastConversationText);
    }

    [Fact]
    public async Task CompactIfNeeded_TooFewMessages_ReturnsFalse()
    {
        // Arrange — 3 messages, keepRecent=4, so count <= keepCount
        // Use large content to ensure tokens exceed threshold (to isolate the count check)
        var history = CreateHistory(3, charsPerMessage: 500);
        var originalCount = history.Count;

        // Act
        var result = await _manager.CompactIfNeededAsync("", history, _modelConfig);

        // Assert
        Assert.False(result);
        Assert.Equal(originalCount, history.Count);
    }

    private static List<ConversationMessage> CreateHistory(int count, int charsPerMessage = 100)
    {
        return Enumerable.Range(0, count)
            .Select(i => new ConversationMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('x', charsPerMessage)
            })
            .ToList();
    }

    private sealed class StubInteractionSummarizer : IInteractionSummarizer
    {
        public Interaction SummaryToReturn { get; set; } = new() { Summary = "Test summary" };
        public bool ThrowOnSummarize { get; set; }
        public string? LastConversationText { get; private set; }

        public Task<Interaction> SummarizeAsync(
            string conversationText,
            string summaryPrompt,
            List<string>? referencedEntityIds = null,
            CancellationToken cancellationToken = default)
        {
            LastConversationText = conversationText;

            if (ThrowOnSummarize)
                throw new InvalidOperationException("Stub: summarization failure");

            return Task.FromResult(SummaryToReturn);
        }
    }

    private sealed class StubKnowledgeGraph : Memory.Abstractions.IKnowledgeGraph
    {
        public Task<Entity> AddEntityAsync(Entity entity, CancellationToken ct = default) => Task.FromResult(entity);
        public Task<Entity?> GetEntityAsync(string id, CancellationToken ct = default) => Task.FromResult<Entity?>(null);
        public Task<Entity?> GetEntityByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Entity?>(null);
        public Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken ct = default) => Task.FromResult(new List<Entity>());
        public Task<List<Entity>> GetAllEntitiesAsync(CancellationToken ct = default) => Task.FromResult(new List<Entity>());
        public Task UpdateEntityAsync(Entity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Relation> AddRelationAsync(Relation relation, CancellationToken ct = default) => Task.FromResult(relation);
        public Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken ct = default) => Task.FromResult(new List<Relation>());
        public Task<List<Relation>> GetAllRelationsAsync(CancellationToken ct = default) => Task.FromResult(new List<Relation>());
        public Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken ct = default) => Task.FromResult(interaction);
        public Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken ct = default) => Task.FromResult(action);
        public Task<List<Interaction>> GetRecentInteractionsAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Interaction>());
        public Task<int> GetInteractionCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken ct = default) => Task.FromResult(new List<AgentAction>());
        public Task DeleteEntityAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateRelationEntityIdAsync(string oldEntityId, string newEntityId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Entity>> GetEntitiesByLevelAsync(MemoryLevel level, CancellationToken ct = default) => Task.FromResult(new List<Entity>());
        public Task<List<Interaction>> GetInteractionsOlderThanAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(new List<Interaction>());
        public Task DeleteInteractionAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
