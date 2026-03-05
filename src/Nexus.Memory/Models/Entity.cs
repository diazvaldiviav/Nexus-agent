namespace Nexus.Memory.Models;

public class Entity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public EntityType Type { get; set; }
    public string? TextSummary { get; set; }
    public byte[]? Embedding { get; set; }
    public DateTime FirstMentioned { get; set; } = DateTime.UtcNow;
    public DateTime LastMentioned { get; set; } = DateTime.UtcNow;
    public int MentionCount { get; set; } = 1;
    public double RelevanceScore { get; set; } = 1.0;
    public MemoryLevel MemoryLevel { get; set; } = MemoryLevel.Relevant;
}
