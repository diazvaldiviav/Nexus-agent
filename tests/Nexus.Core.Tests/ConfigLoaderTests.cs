using Nexus.Core.Config;
using Xunit;

namespace Nexus.Core.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nexus_config_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Load_WhenFileNotExists_ShouldReturnDefaultConfig()
    {
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.yaml");
        var config = ConfigLoader.Load(nonExistentPath);

        Assert.NotNull(config);
        Assert.Equal("Nexus", config.Agent.Name);
        Assert.Equal("ollama", config.Models.Local.Provider);
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTripConfig()
    {
        var configPath = Path.Combine(_tempDir, "nexus.yaml");
        var original = new NexusConfig
        {
            Agent = new AgentConfig { Name = "TestAgent", Language = "es" },
            Models = new ModelsConfig
            {
                Local = new ModelProviderConfig { Model = "qwen2.5:7b" }
            }
        };

        ConfigLoader.Save(original, configPath);
        var loaded = ConfigLoader.Load(configPath);

        Assert.Equal("TestAgent", loaded.Agent.Name);
        Assert.Equal("es", loaded.Agent.Language);
        Assert.Equal("qwen2.5:7b", loaded.Models.Local.Model);
    }

    [Fact]
    public void GetDatabasePath_WithTilde_ShouldExpandHomeDirectory()
    {
        var config = new NexusConfig { Memory = new MemoryConfig { Database = "~/.nexus/memory.db" } };
        var path = ConfigLoader.GetDatabasePath(config);

        Assert.DoesNotContain("~", path);
        Assert.Contains(".nexus", path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
