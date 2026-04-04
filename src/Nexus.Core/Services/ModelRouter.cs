using Nexus.Core.Config;
using Nexus.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Nexus.Core.Services;

public enum TaskType
{
    EntityExtraction,
    InteractionSummary,
    EntityResolution,
    MemoryQueryResponse,
    ComplexReasoning,
    CodeGeneration,
    Default
}

public class ModelRouter
{
    private readonly RoutingConfig _routing;
    private readonly ILogger<ModelRouter>? _logger;

    public ModelRouter(RoutingConfig routing, ILogger<ModelRouter>? logger = null)
    {
        _routing = routing;
        _logger = logger;
    }

    public string Route(TaskType task)
    {
        var decision = task switch
        {
            TaskType.EntityExtraction => _routing.EntityExtraction,
            TaskType.InteractionSummary => _routing.InteractionSummary,
            TaskType.EntityResolution => _routing.EntityResolution,
            TaskType.MemoryQueryResponse => _routing.MemoryQueryResponse,
            TaskType.ComplexReasoning => _routing.ComplexReasoning,
            TaskType.CodeGeneration => _routing.CodeGeneration,
            _ => _routing.Default
        };

        _logger?.LogDebug("Routing task {Task} to {Provider}", task, decision);
        return decision.ToLower();
    }

    public bool IsLocal(TaskType task) => Route(task) == "local";
    public bool IsCloud(TaskType task) => Route(task) == "cloud";
}
