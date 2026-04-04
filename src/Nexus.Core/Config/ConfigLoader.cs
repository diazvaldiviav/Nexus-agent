using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nexus.Core.Config;

public class ConfigLoader
{
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "nexus.yaml");

    // Returns true if ./nexus.yaml OR DefaultConfigPath exists (either satisfies). Explicit path checked first.
    public static bool Exists(string? configPath = null)
    {
        if (configPath is not null)
            return File.Exists(configPath);

        return File.Exists("nexus.yaml") || File.Exists(DefaultConfigPath);
    }

    public static NexusConfig Load(string? configPath = null)
    {
        var path = configPath
            ?? (File.Exists("nexus.yaml") ? Path.GetFullPath("nexus.yaml") : null)
            ?? (File.Exists(DefaultConfigPath) ? DefaultConfigPath : null)
            ?? DefaultConfigPath;

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

    public static string GetArchivePath(NexusConfig config)
    {
        var archivePath = config.Memory.ArchivePath;
        if (archivePath.StartsWith("~/"))
            archivePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                archivePath[2..]);
        return archivePath;
    }
}
