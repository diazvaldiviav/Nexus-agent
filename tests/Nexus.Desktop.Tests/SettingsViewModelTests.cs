using Nexus.Core.Config;
using Nexus.Connectors;
using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Tests;

public class SettingsViewModelTests
{
    private static McpLifecycleService CreateMcpLifecycleService()
        => new(new FakeMcpClientManager(), new ToolRegistry());

    [Fact]
    public void Constructor_LoadsConfigValues()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Model = "test-model";
        config.Models.Cloud.Provider = "openai";
        config.Embeddings.Model = "test-embed";
        config.Memory.RelevanceDecayLambda = 0.1;
        config.Memory.SummarizationInterval = 5;
        config.Memory.RecentInteractionsFetchLimit = 3;

        // Act
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        // Assert
        Assert.Equal("test-model", vm.LocalModel);
        Assert.Equal("openai", vm.CloudProvider);
        Assert.Equal("test-embed", vm.EmbeddingsModel);
        Assert.Equal(0.1m, vm.DecayLambda);
        Assert.Equal(5, vm.SummarizationInterval);
        Assert.Equal(3, vm.RecentInteractionsFetchLimit);
    }

    [Fact]
    public void SaveSettings_UpdatesConfigObject()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        // Act — change fields to make VM dirty so SaveSettingsCommand can execute
        vm.LocalModel = "new-model";
        vm.DecayLambda = 0.2m;
        vm.SaveSettingsCommand.Execute(null);

        // Assert
        Assert.Equal("new-model", config.Models.Local.Model);
        Assert.Equal(0.2, config.Memory.RelevanceDecayLambda);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Constructor_HandlesNullApiKeys()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Gemini = null;
        config.Models.Anthropic = null;
        config.Models.OpenAi = null;

        // Act
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        // Assert
        Assert.Equal(string.Empty, vm.GeminiApiKey);
        Assert.Equal(string.Empty, vm.AnthropicApiKey);
        Assert.Equal(string.Empty, vm.OpenAiApiKey);
    }

    [Fact]
    public void SaveSettings_ClearsEmptyApiKeys()
    {
        // Arrange
        var config = new NexusConfig();
        config.Models.Local.Endpoint = "http://localhost:11434";
        config.Models.Gemini = new ProviderKeyConfig { ApiKey = "old-key" };
        var vm = new SettingsViewModel(config, CreateMcpLifecycleService());

        // Act — clearing the key makes VM dirty, enabling save
        vm.GeminiApiKey = "";
        vm.SaveSettingsCommand.Execute(null);

        // Assert
        Assert.Null(config.Models.Gemini.ApiKey);
    }
}
