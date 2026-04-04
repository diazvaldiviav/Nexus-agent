using Microsoft.Data.Sqlite;

namespace Nexus.Memory.Infrastructure;

public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={databasePath}";
    }

    public string ConnectionString => _connectionString;

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS entities (
                id              TEXT PRIMARY KEY,
                name            TEXT NOT NULL,
                type            TEXT NOT NULL,
                text_summary    TEXT,
                embedding       BLOB,
                first_mentioned DATETIME NOT NULL,
                last_mentioned  DATETIME NOT NULL,
                mention_count   INTEGER DEFAULT 1,
                relevance_score REAL DEFAULT 1.0,
                memory_level    TEXT DEFAULT 'relevant'
            );

            CREATE TABLE IF NOT EXISTS relations (
                id              TEXT PRIMARY KEY,
                entity_id_1     TEXT NOT NULL REFERENCES entities(id),
                entity_id_2     TEXT NOT NULL REFERENCES entities(id),
                relation_type   TEXT NOT NULL,
                context         TEXT,
                timestamp       DATETIME NOT NULL,
                confidence      REAL DEFAULT 1.0
            );

            CREATE TABLE IF NOT EXISTS interactions (
                id                  TEXT PRIMARY KEY,
                summary             TEXT NOT NULL,
                embedding           BLOB,
                referenced_entities TEXT,
                timestamp           DATETIME NOT NULL,
                token_count         INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS agent_actions (
                id              TEXT PRIMARY KEY,
                action_type     TEXT NOT NULL,
                detail          TEXT,
                model_used      TEXT,
                tokens_in       INTEGER DEFAULT 0,
                tokens_out      INTEGER DEFAULT 0,
                duration_ms     INTEGER DEFAULT 0,
                timestamp       DATETIME NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_entities_type ON entities(type);
            CREATE INDEX IF NOT EXISTS idx_entities_memory_level ON entities(memory_level);
            CREATE INDEX IF NOT EXISTS idx_entities_last_mentioned ON entities(last_mentioned);
            CREATE INDEX IF NOT EXISTS idx_relations_entity1 ON relations(entity_id_1);
            CREATE INDEX IF NOT EXISTS idx_relations_entity2 ON relations(entity_id_2);
            CREATE INDEX IF NOT EXISTS idx_interactions_timestamp ON interactions(timestamp);
            CREATE INDEX IF NOT EXISTS idx_agent_actions_timestamp ON agent_actions(timestamp);
        ";
        cmd.ExecuteNonQuery();
    }
}
