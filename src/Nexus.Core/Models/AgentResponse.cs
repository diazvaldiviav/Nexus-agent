using Nexus.Memory.Models;

namespace Nexus.Core.Models;

public class AgentResponse
{
    public string Content { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public int DurationMs { get; set; }
    public List<Entity> ExtractedEntities { get; set; } = new();
}
