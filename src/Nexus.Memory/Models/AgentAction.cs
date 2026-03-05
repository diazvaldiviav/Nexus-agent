namespace Nexus.Memory.Models;

public class AgentAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ActionType { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? ModelUsed { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public int DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
