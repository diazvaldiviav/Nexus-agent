namespace Nexus.Memory.Models;

public class ArchiveFile
{
    public DateTime ArchivedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<ArchivedEntity> Entities { get; set; } = [];
}

public class ArchivedEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TextSummary { get; set; }
    public string? Embedding { get; set; }
    public DateTime FirstMentioned { get; set; }
    public DateTime LastMentioned { get; set; }
    public int MentionCount { get; set; }
    public double RelevanceScore { get; set; }
    public List<ArchivedRelation> Relations { get; set; } = [];
}

public class ArchivedRelation
{
    public string Id { get; set; } = string.Empty;
    public string EntityId1 { get; set; } = string.Empty;
    public string EntityId2 { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string? Context { get; set; }
    public DateTime Timestamp { get; set; }
    public double Confidence { get; set; }
}
