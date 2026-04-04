namespace Nexus.Memory.Models;

public class MemoryContext
{
    public List<Entity> WorkingMemory { get; set; } = new();
    public List<Entity> RelevantMemory { get; set; } = new();
    public List<Relation> Relations { get; set; } = new();
    public List<Interaction> RecentInteractions { get; set; } = new();
    public int TotalTokenEstimate { get; set; }
}
