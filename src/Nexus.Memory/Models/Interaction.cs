namespace Nexus.Memory.Models;

public class Interaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Summary { get; set; } = string.Empty;
    public byte[]? Embedding { get; set; }
    public List<string> ReferencedEntityIds { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TokenCount { get; set; }
}
