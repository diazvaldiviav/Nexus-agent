using System.Net;
using Nexus.CLI;
using Nexus.Core.Config;
using Nexus.Integration.Tests.Fakes;

namespace Nexus.Integration.Tests;

public class OnboardingWizardTests : IDisposable
{
    private readonly string _tempDir;

    public OnboardingWizardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nexus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    // T-1: ConfigLoader.Exists returns false when no config file exists
    [Fact]
    public void Exists_WhenNoConfig_ReturnsFalse()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does-not-exist.yaml");
        Assert.False(ConfigLoader.Exists(nonExistentPath));
    }

    // T-2: ConfigLoader.Exists returns true when config file exists
    [Fact]
    public void Exists_WhenConfigExists_ReturnsTrue()
    {
        var configPath = Path.Combine(_tempDir, "nexus.yaml");
        File.WriteAllText(configPath, "agent:\n  name: Test\n");
        Assert.True(ConfigLoader.Exists(configPath));
    }

    // T-3: ConfigLoader.Exists with explicit path checks that specific path
    [Fact]
    public void Exists_WithExplicitPath_ChecksThatPath()
    {
        var specificPath = Path.Combine(_tempDir, "custom-config.yaml");
        Assert.False(ConfigLoader.Exists(specificPath));

        File.WriteAllText(specificPath, "agent:\n  name: Custom\n");
        Assert.True(ConfigLoader.Exists(specificPath));
    }

    // T-4: GenerateConfig produces correct default values
    [Fact]
    public void GeneratedConfig_HasCorrectDefaults()
    {
        var config = OnboardingWizard.GenerateConfig("qwen3:14b", "nomic-embed-text", null, null, null);

        Assert.Equal("ollama", config.Models.Local.Provider);
        Assert.Equal("qwen3:14b", config.Models.Local.Model);
        Assert.Equal("ollama", config.Embeddings.Provider);
        Assert.Equal("nomic-embed-text", config.Embeddings.Model);
        Assert.Null(config.Models.Gemini);
        Assert.Null(config.Models.Anthropic);
        Assert.Null(config.Models.OpenAi);
    }

    // T-5: GenerateConfig with API keys sets provider key configs
    [Fact]
    public void GeneratedConfig_WithApiKeys_SetsProviderKeys()
    {
        var config = OnboardingWizard.GenerateConfig(
            "qwen3:14b", "nomic-embed-text",
            "gemini-key-123", "anthropic-key-456", "openai-key-789");

        Assert.NotNull(config.Models.Gemini);
        Assert.Equal("gemini-key-123", config.Models.Gemini.ApiKey);

        Assert.NotNull(config.Models.Anthropic);
        Assert.Equal("anthropic-key-456", config.Models.Anthropic.ApiKey);

        Assert.NotNull(config.Models.OpenAi);
        Assert.Equal("openai-key-789", config.Models.OpenAi.ApiKey);
    }

    // T-6: DetectOllamaAsync returns model names when Ollama is running
    [Fact]
    public async Task DetectOllama_WhenRunning_ReturnsModelNames()
    {
        var json = """{"models":[{"name":"qwen3:14b"},{"name":"nomic-embed-text"}]}""";
        var handler = new TestHttpMessageHandler(json);
        var httpClient = new HttpClient(handler);

        var models = await OnboardingWizard.ParseOllamaTagsAsync(httpClient);

        Assert.Equal(2, models.Count);
        Assert.Contains("qwen3:14b", models);
        Assert.Contains("nomic-embed-text", models);
    }

    // T-7: ParseOllamaTagsAsync throws when Ollama returns error status
    [Fact]
    public async Task ParseOllamaTags_WhenNotRunning_Throws()
    {
        var handler = new TestHttpMessageHandler("", HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => OnboardingWizard.ParseOllamaTagsAsync(httpClient));
    }

    // T-8: GenerateConfig with MCP server adds server entry
    [Fact]
    public void GeneratedConfig_WithMcpServer_AddsServerEntry()
    {
        // Arrange
        var mcpServer = new McpServerEntry
        {
            Name = "filesystem",
            Transport = "stdio",
            Command = "npx",
            Args = new List<string> { "-y", "@modelcontextprotocol/server-filesystem", "/home/test" }
        };

        // Act
        var config = OnboardingWizard.GenerateConfig(
            "qwen3:14b", "nomic-embed-text", null, null, null, mcpServer);

        // Assert
        Assert.Single(config.Mcp.Servers);
        Assert.Equal("filesystem", config.Mcp.Servers[0].Name);
        Assert.Equal("stdio", config.Mcp.Servers[0].Transport);
        Assert.Equal("npx", config.Mcp.Servers[0].Command);
    }

    // T-9: GenerateConfig without MCP server has empty servers list
    [Fact]
    public void GeneratedConfig_WithoutMcpServer_HasEmptyServers()
    {
        // Act
        var config = OnboardingWizard.GenerateConfig(
            "qwen3:14b", "nomic-embed-text", null, null, null, null);

        // Assert
        Assert.Empty(config.Mcp.Servers);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
        GC.SuppressFinalize(this);
    }
}
