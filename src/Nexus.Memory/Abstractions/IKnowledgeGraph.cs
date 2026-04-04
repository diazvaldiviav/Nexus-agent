using Nexus.Memory.Models;

namespace Nexus.Memory.Abstractions;

public interface IKnowledgeGraph
{
    Task<Entity> AddEntityAsync(Entity entity, CancellationToken cancellationToken = default);

    Task<Entity?> GetEntityAsync(string id, CancellationToken cancellationToken = default);

    Task<Entity?> GetEntityByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default);

    Task<List<Entity>> GetAllEntitiesAsync(CancellationToken cancellationToken = default);

    Task UpdateEntityAsync(Entity entity, CancellationToken cancellationToken = default);

    Task<Relation> AddRelationAsync(Relation relation, CancellationToken cancellationToken = default);

    Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken cancellationToken = default);

    Task<List<Relation>> GetAllRelationsAsync(CancellationToken cancellationToken = default);

    Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken cancellationToken = default);

    Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken cancellationToken = default);

    Task<List<Interaction>> GetRecentInteractionsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<int> GetInteractionCountAsync(CancellationToken cancellationToken = default);

    Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default);

    Task UpdateRelationEntityIdAsync(
        string oldEntityId,
        string newEntityId,
        CancellationToken cancellationToken = default);

    Task<List<Entity>> GetEntitiesByLevelAsync(
        MemoryLevel level,
        CancellationToken cancellationToken = default);

    Task<List<Interaction>> GetInteractionsOlderThanAsync(
        DateTime cutoff,
        CancellationToken cancellationToken = default);

    Task DeleteInteractionAsync(
        string id,
        CancellationToken cancellationToken = default);
}
