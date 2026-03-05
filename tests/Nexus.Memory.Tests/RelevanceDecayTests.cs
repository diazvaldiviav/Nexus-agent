using Nexus.Memory;
using Nexus.Memory.Models;
using Xunit;

namespace Nexus.Memory.Tests;

public class RelevanceDecayTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly RelevanceDecay _decay;

    public RelevanceDecayTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nexus_decay_test_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_dbPath);
        _dbInit.Initialize();
        _decay = new RelevanceDecay(_dbInit.ConnectionString, lambda: 0.05);
    }

    [Fact]
    public void ComputeScore_RecentMention_ShouldBeHighScore()
    {
        var score = _decay.ComputeScore(1.0, 1, DateTime.UtcNow);
        Assert.True(score > 0.99, $"Expected > 0.99 but got {score}");
    }

    [Fact]
    public void ComputeScore_OldMention_ShouldBeDecayed()
    {
        var oneYearAgo = DateTime.UtcNow.AddDays(-365);
        var score = _decay.ComputeScore(1.0, 1, oneYearAgo);
        Assert.True(score < 0.5, $"Expected < 0.5 but got {score}");
    }

    [Fact]
    public void ComputeScore_FrequentMentions_ShouldDecaySlower()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var rareScore = _decay.ComputeScore(1.0, 1, thirtyDaysAgo);
        var frequentScore = _decay.ComputeScore(1.0, 50, thirtyDaysAgo);
        Assert.True(frequentScore > rareScore, "Frequent mentions should decay slower");
    }

    [Fact]
    public async Task ApplyDecayAsync_ShouldUpdateEntityScores()
    {
        var graph = new KnowledgeGraph(_dbInit.ConnectionString);
        var entity = new Entity
        {
            Name = "OldEntity",
            Type = EntityType.Other,
            LastMentioned = DateTime.UtcNow.AddDays(-100),
            MentionCount = 1,
            RelevanceScore = 1.0
        };
        await graph.AddEntityAsync(entity);

        await _decay.ApplyDecayAsync();

        var updated = await graph.GetEntityAsync(entity.Id);
        Assert.NotNull(updated);
        Assert.True(updated.RelevanceScore < 1.0, "Score should have decayed");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
