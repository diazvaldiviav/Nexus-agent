using Microsoft.Data.Sqlite;
using Nexus.Memory.Models;

using Nexus.Memory.Abstractions;

namespace Nexus.Memory.Processing;

public class RelevanceDecay
{
    private readonly string _connectionString;
    private readonly double _lambda;
    private readonly double _workingThresholdScore;
    private readonly int _workingThresholdMentions;
    private readonly double _archiveThresholdScore;
    private readonly MemoryCompressor? _compressor;

    public RelevanceDecay(
        string connectionString,
        double lambda = 0.05,
        double workingThresholdScore = 0.7,
        int workingThresholdMentions = 3,
        double archiveThresholdScore = 0.05,
        MemoryCompressor? compressor = null)
    {
        _connectionString = connectionString;
        _lambda = lambda;
        _workingThresholdScore = workingThresholdScore;
        _workingThresholdMentions = workingThresholdMentions;
        _archiveThresholdScore = archiveThresholdScore;
        _compressor = compressor;
    }

    public double ComputeScore(double baseScore, int mentionCount, DateTime lastMentioned)
    {
        var daysSince = (DateTime.UtcNow - lastMentioned).TotalDays;
        var lambdaEff = _lambda / Math.Log2(mentionCount + 1);
        return baseScore * Math.Exp(-lambdaEff * daysSince);
    }

    public async Task ApplyDecayAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        // Read all entities
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, mention_count, last_mentioned, relevance_score FROM entities";
        
        var updates = new List<(string id, double score, string level)>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var mentionCount = reader.GetInt32(1);
                var lastMentioned = DateTime.Parse(reader.GetString(2));
                var baseScore = reader.GetDouble(3);

                var newScore = ComputeScore(baseScore, mentionCount, lastMentioned);
                var level = DetermineLevel(newScore, mentionCount);
                updates.Add((id, newScore, level));
            }
        }

        // Apply updates
        foreach (var (id, score, level) in updates)
        {
            var update = conn.CreateCommand();
            update.CommandText = "UPDATE entities SET relevance_score = $score, memory_level = $level WHERE id = $id";
            update.Parameters.AddWithValue("$score", score);
            update.Parameters.AddWithValue("$level", level);
            update.Parameters.AddWithValue("$id", id);
            await update.ExecuteNonQueryAsync();
        }

        if (_compressor is not null)
        {
            try { await _compressor.ArchiveStaleEntitiesAsync().ConfigureAwait(false); }
            catch (Exception) { /* archival is best-effort */ }
        }
    }

    private string DetermineLevel(double score, int mentionCount)
    {
        if (score > _workingThresholdScore && mentionCount >= _workingThresholdMentions)
            return "working";
        if (score > _archiveThresholdScore)
            return "relevant";
        return "archive";
    }
}
