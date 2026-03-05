using Microsoft.Data.Sqlite;
using Nexus.Memory.Models;

namespace Nexus.Memory;

public class SemanticSearch
{
    private readonly string _connectionString;

    public SemanticSearch(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Entity>> SearchByEmbeddingAsync(float[] queryEmbedding, int topK = 20)
    {
        var allEntities = await GetEntitiesWithEmbeddingsAsync();
        
        var scored = allEntities
            .Where(e => e.Embedding != null)
            .Select(e => (entity: e, score: CosineSimilarity(queryEmbedding, ToFloatArray(e.Embedding!))))
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => x.entity)
            .ToList();
        
        return scored;
    }

    public async Task<List<Entity>> SearchByTextAsync(string text, int topK = 20)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM entities 
            WHERE name LIKE $text OR text_summary LIKE $text
            ORDER BY relevance_score DESC
            LIMIT $topK";
        cmd.Parameters.AddWithValue("$text", $"%{text}%");
        cmd.Parameters.AddWithValue("$topK", topK);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var entities = new List<Entity>();
        while (await reader.ReadAsync())
        {
            entities.Add(new Entity
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Type = Enum.TryParse<EntityType>(reader.GetString(reader.GetOrdinal("type")), true, out var t) ? t : EntityType.Other,
                TextSummary = reader.IsDBNull(reader.GetOrdinal("text_summary")) ? null : reader.GetString(reader.GetOrdinal("text_summary")),
                MentionCount = reader.GetInt32(reader.GetOrdinal("mention_count")),
                RelevanceScore = reader.GetDouble(reader.GetOrdinal("relevance_score")),
                MemoryLevel = Enum.TryParse<MemoryLevel>(reader.GetString(reader.GetOrdinal("memory_level")), true, out var ml) ? ml : MemoryLevel.Relevant
            });
        }
        return entities;
    }

    private async Task<List<Entity>> GetEntitiesWithEmbeddingsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entities WHERE embedding IS NOT NULL ORDER BY relevance_score DESC";
        
        using var reader = await cmd.ExecuteReaderAsync();
        var entities = new List<Entity>();
        while (await reader.ReadAsync())
        {
            entities.Add(new Entity
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Type = Enum.TryParse<EntityType>(reader.GetString(reader.GetOrdinal("type")), true, out var t) ? t : EntityType.Other,
                TextSummary = reader.IsDBNull(reader.GetOrdinal("text_summary")) ? null : reader.GetString(reader.GetOrdinal("text_summary")),
                Embedding = (byte[])reader.GetValue(reader.GetOrdinal("embedding")),
                MentionCount = reader.GetInt32(reader.GetOrdinal("mention_count")),
                RelevanceScore = reader.GetDouble(reader.GetOrdinal("relevance_score")),
                MemoryLevel = Enum.TryParse<MemoryLevel>(reader.GetString(reader.GetOrdinal("memory_level")), true, out var ml) ? ml : MemoryLevel.Relevant
            });
        }
        return entities;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0f : dot / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static float[] ToFloatArray(byte[] bytes)
    {
        var result = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    public static byte[] ToByteArray(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
