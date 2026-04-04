using Microsoft.Data.Sqlite;
using Nexus.Memory.Models;
using System.Text.Json;

using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;

namespace Nexus.Memory.Graph;

public class KnowledgeGraph : IKnowledgeGraph, IActionLogNotifier
{
    private readonly string _connectionString;

    public event Action<AgentAction>? ActionLogged;

    public KnowledgeGraph(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Entity> AddEntityAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO entities (id, name, type, text_summary, embedding, first_mentioned, last_mentioned, mention_count, relevance_score, memory_level)
            VALUES ($id, $name, $type, $summary, $embedding, $first, $last, $count, $score, $level)";
        cmd.Parameters.AddWithValue("$id", entity.Id);
        cmd.Parameters.AddWithValue("$name", entity.Name);
        cmd.Parameters.AddWithValue("$type", entity.Type.ToString().ToLower());
        cmd.Parameters.AddWithValue("$summary", entity.TextSummary as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding", entity.Embedding as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$first", entity.FirstMentioned.ToString("O"));
        cmd.Parameters.AddWithValue("$last", entity.LastMentioned.ToString("O"));
        cmd.Parameters.AddWithValue("$count", entity.MentionCount);
        cmd.Parameters.AddWithValue("$score", entity.RelevanceScore);
        cmd.Parameters.AddWithValue("$level", entity.MemoryLevel.ToString().ToLower());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return entity;
    }

    public async Task<Entity?> GetEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return MapEntity(reader);
        return null;
    }

    public async Task<Entity?> GetEntityByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities WHERE LOWER(name) = LOWER($name) LIMIT 1";
        cmd.Parameters.AddWithValue("$name", name);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return MapEntity(reader);
        return null;
    }

    public async Task<List<Entity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities WHERE type = $type ORDER BY relevance_score DESC";
        cmd.Parameters.AddWithValue("$type", type.ToString().ToLower());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entities = new List<Entity>();
        while (await reader.ReadAsync(cancellationToken))
            entities.Add(MapEntity(reader));
        return entities;
    }

    public async Task<List<Entity>> GetAllEntitiesAsync(CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities ORDER BY relevance_score DESC";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entities = new List<Entity>();
        while (await reader.ReadAsync(cancellationToken))
            entities.Add(MapEntity(reader));
        return entities;
    }

    public async Task UpdateEntityAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE entities SET
                name = $name, type = $type, text_summary = $summary, embedding = $embedding,
                last_mentioned = $last, mention_count = $count, relevance_score = $score, memory_level = $level
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", entity.Id);
        cmd.Parameters.AddWithValue("$name", entity.Name);
        cmd.Parameters.AddWithValue("$type", entity.Type.ToString().ToLower());
        cmd.Parameters.AddWithValue("$summary", entity.TextSummary as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding", entity.Embedding as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last", entity.LastMentioned.ToString("O"));
        cmd.Parameters.AddWithValue("$count", entity.MentionCount);
        cmd.Parameters.AddWithValue("$score", entity.RelevanceScore);
        cmd.Parameters.AddWithValue("$level", entity.MemoryLevel.ToString().ToLower());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Relation> AddRelationAsync(Relation relation, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO relations (id, entity_id_1, entity_id_2, relation_type, context, timestamp, confidence)
            VALUES ($id, $e1, $e2, $type, $context, $ts, $conf)";
        cmd.Parameters.AddWithValue("$id", relation.Id);
        cmd.Parameters.AddWithValue("$e1", relation.EntityId1);
        cmd.Parameters.AddWithValue("$e2", relation.EntityId2);
        cmd.Parameters.AddWithValue("$type", relation.RelationType);
        cmd.Parameters.AddWithValue("$context", relation.Context as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", relation.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$conf", relation.Confidence);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return relation;
    }

    public async Task<List<Relation>> GetRelationsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM relations WHERE entity_id_1 = $id OR entity_id_2 = $id ORDER BY timestamp DESC";
        cmd.Parameters.AddWithValue("$id", entityId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var relations = new List<Relation>();
        while (await reader.ReadAsync(cancellationToken))
            relations.Add(MapRelation(reader));
        return relations;
    }

    public async Task<List<Relation>> GetAllRelationsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM relations ORDER BY timestamp DESC";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var relations = new List<Relation>();
        while (await reader.ReadAsync(cancellationToken))
            relations.Add(MapRelation(reader));
        return relations;
    }

    public async Task<Interaction> AddInteractionAsync(Interaction interaction, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO interactions (id, summary, embedding, referenced_entities, timestamp, token_count)
            VALUES ($id, $summary, $embedding, $refs, $ts, $tokens)";
        cmd.Parameters.AddWithValue("$id", interaction.Id);
        cmd.Parameters.AddWithValue("$summary", interaction.Summary);
        cmd.Parameters.AddWithValue("$embedding", interaction.Embedding as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$refs", JsonSerializer.Serialize(interaction.ReferencedEntityIds));
        cmd.Parameters.AddWithValue("$ts", interaction.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$tokens", interaction.TokenCount);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return interaction;
    }

    public async Task<AgentAction> LogActionAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO agent_actions (id, action_type, detail, model_used, tokens_in, tokens_out, duration_ms, timestamp)
            VALUES ($id, $type, $detail, $model, $in, $out, $ms, $ts)";
        cmd.Parameters.AddWithValue("$id", action.Id);
        cmd.Parameters.AddWithValue("$type", action.ActionType);
        cmd.Parameters.AddWithValue("$detail", action.Detail as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", action.ModelUsed as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", action.TokensIn);
        cmd.Parameters.AddWithValue("$out", action.TokensOut);
        cmd.Parameters.AddWithValue("$ms", action.DurationMs);
        cmd.Parameters.AddWithValue("$ts", action.Timestamp.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        ActionLogged?.Invoke(action);
        return action;
    }

    public async Task<List<Interaction>> GetRecentInteractionsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, summary, embedding, referenced_entities, timestamp, token_count FROM interactions ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var interactions = new List<Interaction>();
        while (await reader.ReadAsync(cancellationToken))
            interactions.Add(MapInteraction(reader));
        return interactions;
    }

    public async Task<int> GetInteractionCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM interactions";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<AgentAction>> GetRecentActionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM agent_actions ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var actions = new List<AgentAction>();
        while (await reader.ReadAsync(cancellationToken))
            actions.Add(MapAgentAction(reader));
        return actions;
    }

    public async Task DeleteEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        using var transaction = conn.BeginTransaction();

        var deleteRelations = conn.CreateCommand();
        deleteRelations.Transaction = transaction;
        deleteRelations.CommandText = "DELETE FROM relations WHERE entity_id_1 = $id OR entity_id_2 = $id";
        deleteRelations.Parameters.AddWithValue("$id", id);
        await deleteRelations.ExecuteNonQueryAsync(cancellationToken);

        var deleteEntity = conn.CreateCommand();
        deleteEntity.Transaction = transaction;
        deleteEntity.CommandText = "DELETE FROM entities WHERE id = $id";
        deleteEntity.Parameters.AddWithValue("$id", id);
        await deleteEntity.ExecuteNonQueryAsync(cancellationToken);

        transaction.Commit();
    }

    public async Task UpdateRelationEntityIdAsync(
        string oldEntityId,
        string newEntityId,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        using var transaction = conn.BeginTransaction();

        var updateE1 = conn.CreateCommand();
        updateE1.Transaction = transaction;
        updateE1.CommandText = "UPDATE relations SET entity_id_1 = $new WHERE entity_id_1 = $old";
        updateE1.Parameters.AddWithValue("$new", newEntityId);
        updateE1.Parameters.AddWithValue("$old", oldEntityId);
        await updateE1.ExecuteNonQueryAsync(cancellationToken);

        var updateE2 = conn.CreateCommand();
        updateE2.Transaction = transaction;
        updateE2.CommandText = "UPDATE relations SET entity_id_2 = $new WHERE entity_id_2 = $old";
        updateE2.Parameters.AddWithValue("$new", newEntityId);
        updateE2.Parameters.AddWithValue("$old", oldEntityId);
        await updateE2.ExecuteNonQueryAsync(cancellationToken);

        transaction.Commit();
    }

    public async Task<List<Entity>> GetEntitiesByLevelAsync(
        MemoryLevel level,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities WHERE memory_level = $level ORDER BY relevance_score DESC";
        cmd.Parameters.AddWithValue("$level", level.ToString().ToLower());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entities = new List<Entity>();
        while (await reader.ReadAsync(cancellationToken))
            entities.Add(MapEntity(reader));
        return entities;
    }

    public async Task<List<Interaction>> GetInteractionsOlderThanAsync(
        DateTime cutoff,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, summary, embedding, referenced_entities, timestamp, token_count FROM interactions WHERE timestamp < $cutoff ORDER BY timestamp ASC";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var interactions = new List<Interaction>();
        while (await reader.ReadAsync(cancellationToken))
            interactions.Add(MapInteraction(reader));
        return interactions;
    }

    public async Task DeleteInteractionAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM interactions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Entity MapEntity(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Type = Enum.TryParse<EntityType>(r.GetString(r.GetOrdinal("type")), true, out var t) ? t : EntityType.Other,
        TextSummary = r.IsDBNull(r.GetOrdinal("text_summary")) ? null : r.GetString(r.GetOrdinal("text_summary")),
        Embedding = r.IsDBNull(r.GetOrdinal("embedding")) ? null : (byte[])r.GetValue(r.GetOrdinal("embedding")),
        FirstMentioned = DateTime.Parse(r.GetString(r.GetOrdinal("first_mentioned"))),
        LastMentioned = DateTime.Parse(r.GetString(r.GetOrdinal("last_mentioned"))),
        MentionCount = r.GetInt32(r.GetOrdinal("mention_count")),
        RelevanceScore = r.GetDouble(r.GetOrdinal("relevance_score")),
        MemoryLevel = Enum.TryParse<MemoryLevel>(r.GetString(r.GetOrdinal("memory_level")), true, out var ml) ? ml : MemoryLevel.Relevant
    };

    private static Relation MapRelation(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        EntityId1 = r.GetString(r.GetOrdinal("entity_id_1")),
        EntityId2 = r.GetString(r.GetOrdinal("entity_id_2")),
        RelationType = r.GetString(r.GetOrdinal("relation_type")),
        Context = r.IsDBNull(r.GetOrdinal("context")) ? null : r.GetString(r.GetOrdinal("context")),
        Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("timestamp"))),
        Confidence = r.GetDouble(r.GetOrdinal("confidence"))
    };

    private static AgentAction MapAgentAction(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        ActionType = r.GetString(r.GetOrdinal("action_type")),
        Detail = r.IsDBNull(r.GetOrdinal("detail")) ? null : r.GetString(r.GetOrdinal("detail")),
        ModelUsed = r.IsDBNull(r.GetOrdinal("model_used")) ? null : r.GetString(r.GetOrdinal("model_used")),
        TokensIn = r.GetInt32(r.GetOrdinal("tokens_in")),
        TokensOut = r.GetInt32(r.GetOrdinal("tokens_out")),
        DurationMs = r.GetInt32(r.GetOrdinal("duration_ms")),
        Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("timestamp")))
    };

    private static Interaction MapInteraction(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Summary = r.GetString(r.GetOrdinal("summary")),
        Embedding = r.IsDBNull(r.GetOrdinal("embedding")) ? null : (byte[])r.GetValue(r.GetOrdinal("embedding")),
        ReferencedEntityIds = r.IsDBNull(r.GetOrdinal("referenced_entities"))
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("referenced_entities"))) ?? new List<string>(),
        Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("timestamp"))),
        TokenCount = r.GetInt32(r.GetOrdinal("token_count"))
    };
}
