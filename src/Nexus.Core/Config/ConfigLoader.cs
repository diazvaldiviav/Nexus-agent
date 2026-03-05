using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nexus.Core.Config;

public class ConfigLoader
{
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "nexus.yaml");

    public static NexusConfig Load(string? configPath = null)
    {
        var path = configPath ?? DefaultConfigPath;

        if (!File.Exists(path))
            return new NexusConfig();

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<NexusConfig>(yaml) ?? new NexusConfig();
    }

    public static void Save(NexusConfig config, string? configPath = null)
    {
        var path = configPath ?? DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        File.WriteAllText(path, serializer.Serialize(config));
    }

    public static string GetDatabasePath(NexusConfig config)
    {
        var dbPath = config.Memory.Database;
        if (dbPath.StartsWith("~/"))
            dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                dbPath[2..]);
        return dbPath;
    }
}
