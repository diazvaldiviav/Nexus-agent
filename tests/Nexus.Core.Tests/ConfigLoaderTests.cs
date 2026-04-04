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
                Local = new ModelProviderConfig { Model = "qwen3:8b" }
            }
        };

        ConfigLoader.Save(original, configPath);
        var loaded = ConfigLoader.Load(configPath);

        Assert.Equal("TestAgent", loaded.Agent.Name);
        Assert.Equal("es", loaded.Agent.Language);
        Assert.Equal("qwen3:8b", loaded.Models.Local.Model);
    }

    [Fact]
    public void GetDatabasePath_WithTilde_ShouldExpandHomeDirectory()
    {
        var config = new NexusConfig { Memory = new MemoryConfig { Database = "~/.nexus/memory.db" } };
        var path = ConfigLoader.GetDatabasePath(config);

        Assert.DoesNotContain("~", path);
        Assert.Contains(".nexus", path);
    }

    [Fact]
    public void Save_ThenLoad_EmbeddingsApiKey_RoundTripsCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nexus_apikey.yaml");
        var original = new NexusConfig
        {
            Embeddings = new EmbeddingsConfig
            {
                Provider = "openai",
                Model = "text-embedding-3-small",
                Endpoint = "https://api.openai.com",
                Dimensions = 1536,
                ApiKey = "sk-test-round-trip-key"
            }
        };

        // Act
        ConfigLoader.Save(original, configPath);
        var loaded = ConfigLoader.Load(configPath);

        // Assert
        Assert.Equal("openai", loaded.Embeddings.Provider);
        Assert.Equal("text-embedding-3-small", loaded.Embeddings.Model);
        Assert.Equal("https://api.openai.com", loaded.Embeddings.Endpoint);
        Assert.Equal(1536, loaded.Embeddings.Dimensions);
        Assert.Equal("sk-test-round-trip-key", loaded.Embeddings.ApiKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

[Collection("CWD")]
public class ConfigLoaderCwdTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigLoaderCwdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nexus_config_cwd_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Load_WhenLocalExists_PrefersLocalOverGlobal()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            var yaml = "agent:\n  name: LocalConfig\n";
            File.WriteAllText(Path.Combine(_tempDir, "nexus.yaml"), yaml);

            Directory.SetCurrentDirectory(_tempDir);

            var config = ConfigLoader.Load();

            Assert.Equal("LocalConfig", config.Agent.Name);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
