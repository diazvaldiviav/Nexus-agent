using Nexus.Memory.Abstractions;
using Nexus.Memory.Models;

namespace Nexus.Desktop.Tests.Fakes;

internal sealed class FakeKnowledgeGraph : IKnowledgeGraph, IActionLogNotifier
{
    public List<Entity> Entities { get; set; } = new();
    public List<Relation> Relations { get; set; } = new();
    public List<Interaction> Interactions { get; set; } = new();
    public List<AgentAction> AgentActions { get; set; } = new();

    public event Action<AgentAction>? ActionLogged;

    public int GetAllEntitiesCallCount { get; private set; }
    public int GetAllRelationsCallCount { get; private set; }
    public int GetRecentActionsCallCount { get; private set; }

    public Task<Entity> AddEntityAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        Entities.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<Entity?> GetEntityAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Entities.FirstOrDefault(e => e.Id == id));

    public Task<Entity?> GetEntityByNameAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Entities.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default)
        => Task.FromResult(Entities.Where(e => e.Type == type).ToList());

    public Task<List<Entity>> GetAllEntitiesAsync(CancellationToken cancellationToken = default)
    {
        GetAllEntitiesCallCount++;
        return Task.FromResult(Entities.ToList());
    }

    public Task UpdateEntityAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        var idx = Entities.FindIndex(e => e.Id == entity.Id);
        if (idx >= 0) Entities[idx] = entity;
        return Task.CompletedTask;
    }

    public Task<Relation> AddRelationAsync(Relation relation, CancellationToken cancellationToken = default)
    {
        Relations.Add(relation);
        return Task.FromResult(relation);
    }

    public Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
        => Task.FromResult(Relations.Where(r =>
            r.EntityId1 == entityId || r.EntityId2 == entityId).ToList());

    public Task<List<Relation>> GetAllRelationsAsync(CancellationToken cancellationToken = default)
    {
        GetAllRelationsCallCount++;
        return Task.FromResult(Relations.ToList());
    }

    public Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken cancellationToken = default)
    {
        Interactions.Add(interaction);
        return Task.FromResult(interaction);
    }

    public Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        AgentActions.Add(action);
        ActionLogged?.Invoke(action);
        return Task.FromResult(action);
    }

    public Task<List<Interaction>> GetRecentInteractionsAsync(int limit = 10, CancellationToken cancellationToken = default)
        => Task.FromResult(Interactions.OrderByDescending(i => i.Timestamp).Take(limit).ToList());

    public Task<int> GetInteractionCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Interactions.Count);

    public Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        GetRecentActionsCallCount++;
        return Task.FromResult(AgentActions.OrderByDescending(a => a.Timestamp).Take(limit).ToList());
    }

    public Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        Entities.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task UpdateRelationEntityIdAsync(string oldEntityId, string newEntityId, CancellationToken cancellationToken = default)
    {
        foreach (var r in Relations)
        {
            if (r.EntityId1 == oldEntityId) r.EntityId1 = newEntityId;
            if (r.EntityId2 == oldEntityId) r.EntityId2 = newEntityId;
        }
        return Task.CompletedTask;
    }

    public Task<List<Entity>> GetEntitiesByLevelAsync(MemoryLevel level, CancellationToken cancellationToken = default)
        => Task.FromResult(Entities.Where(e => e.MemoryLevel == level).ToList());

    public Task<List<Interaction>> GetInteractionsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        => Task.FromResult(Interactions.Where(i => i.Timestamp < cutoff).ToList());

    public Task DeleteInteractionAsync(string id, CancellationToken cancellationToken = default)
    {
        Interactions.RemoveAll(i => i.Id == id);
        return Task.CompletedTask;
    }
}
