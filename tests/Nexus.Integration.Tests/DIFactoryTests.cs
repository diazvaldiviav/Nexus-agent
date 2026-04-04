using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Core.Abstractions;
using Nexus.Core.Models;
using Nexus.Core.Providers;
using Nexus.Core.Services;
using Nexus.Core.Config;
using Nexus.Memory.Abstractions;
using Nexus.Memory.Embedding;
using Nexus.Memory.Graph;
using Nexus.Memory.Infrastructure;
using Nexus.Memory.Processing;
using Xunit;

namespace Nexus.Integration.Tests;

/// <summary>
/// DI factory tests that verify provider selection based on configuration.
/// AC-8: DI factory selects provider based on config.Embeddings.Provider
/// AC-9: Missing API key produces descriptive error
/// </summary>
public class DIFactoryTests : IDisposable
{
    private readonly List<string> _dbPaths = new();
    private readonly List<IServiceProvider> _providers = new();

    private (IServiceProvider provider, string dbPath) BuildServices(NexusConfig config)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"di_factory_test_{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);
        config.Memory = new MemoryConfig { Database = dbPath };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddNexusAgent(config);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return (provider, dbPath);
    }

    [Fact]
    public void DI_OllamaProvider_ResolvesOllamaEmbeddingService()
    {
        // Arrange
        var config = new NexusConfig
        {
            Embeddings = new EmbeddingsConfig { Provider = "ollama" }
        };

        var (provider, _) = BuildServices(config);

        // Act
        var embeddingService = provider.GetRequiredService<IEmbeddingService>();

        // Assert
        Assert.IsType<OllamaEmbeddingService>(embeddingService);
    }

    [Fact]
    public void DI_OpenAiProvider_ResolvesOpenAiEmbeddingService()
    {
        // Arrange
        var config = new NexusConfig
        {
            Embeddings = new EmbeddingsConfig
            {
                Provider = "openai",
                ApiKey = "sk-test-key-for-di-test",
                Model = "text-embedding-3-small",
                Dimensions = 1536
            }
        };

        var (provider, _) = BuildServices(config);

        // Act
        var embeddingService = provider.GetRequiredService<IEmbeddingService>();

        // Assert
        Assert.IsType<OpenAiEmbeddingService>(embeddingService);
    }

    [Fact]
    public void DI_OpenAiProvider_NoApiKey_ThrowsDescriptiveError()
    {
        // Arrange: Config with provider=openai but no API key.
        // MEDIUM-1: Isolate OPENAI_API_KEY env var to prevent CI environment leaking a key.
        var originalEnvVar = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

            var config = new NexusConfig
            {
                Embeddings = new EmbeddingsConfig
                {
                    Provider = "openai",
                    ApiKey = null
                }
            };

            var (provider, _) = BuildServices(config);

            // Act & Assert: resolving IEmbeddingService should throw
            // DI may wrap the original exception, so check both Message and InnerException
            var ex = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IEmbeddingService>());

            var message = ex.Message + (ex.InnerException?.Message ?? "");
            Assert.Contains("OpenAI API key is required", message);
            Assert.Contains("OPENAI_API_KEY", message);
        }
        finally
        {
            // Restore original env var value
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalEnvVar);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var provider in _providers)
        {
            (provider as IDisposable)?.Dispose();
        }
        SqliteConnection.ClearAllPools();
        foreach (var dbPath in _dbPaths)
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
