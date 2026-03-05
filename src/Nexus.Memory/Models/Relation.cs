namespace Nexus.Memory.Models;

public class Relation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityId1 { get; set; } = string.Empty;
    public string EntityId2 { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string? Context { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double Confidence { get; set; } = 1.0;
}
