using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Config;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly NexusConfig _config;

    [ObservableProperty] private string _localProvider = "ollama";
    [ObservableProperty] private string _localModel = "qwen3:14b";
    [ObservableProperty] private string _localEndpoint = "http://localhost:11434";
    [ObservableProperty] private string _cloudProvider = "anthropic";
    [ObservableProperty] private string _cloudModel = "claude-sonnet-4-5-20250929";
    [ObservableProperty] private string _cloudApiKey = string.Empty;
    [ObservableProperty] private string _embeddingsModel = "nomic-embed-text";
    [ObservableProperty] private decimal _decayLambda = 0.05m;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<string> AvailableLocalModels { get; } = new(
        new[] { "qwen3:14b", "qwen3:8b", "llama3.2:3b", "mistral:7b", "phi3:mini" });

    public ObservableCollection<string> AvailableCloudProviders { get; } = new(
        new[] { "anthropic", "openai", "google" });

    public SettingsViewModel(NexusConfig config)
    {
        _config = config;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        LocalProvider = _config.Models.Local.Provider;
        LocalModel = _config.Models.Local.Model;
        LocalEndpoint = _config.Models.Local.Endpoint ?? "http://localhost:11434";
        CloudProvider = _config.Models.Cloud.Provider;
        CloudModel = _config.Models.Cloud.Model;
        CloudApiKey = _config.Models.Cloud.ApiKey ?? string.Empty;
        EmbeddingsModel = _config.Embeddings.Model;
        DecayLambda = (decimal)_config.Memory.RelevanceDecayLambda;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _config.Models.Local.Provider = LocalProvider;
        _config.Models.Local.Model = LocalModel;
        _config.Models.Local.Endpoint = LocalEndpoint;
        _config.Models.Cloud.Provider = CloudProvider;
        _config.Models.Cloud.Model = CloudModel;
        _config.Models.Cloud.ApiKey = string.IsNullOrWhiteSpace(CloudApiKey) ? null : CloudApiKey;
        _config.Embeddings.Model = EmbeddingsModel;
        _config.Memory.RelevanceDecayLambda = (double)DecayLambda;

        try
        {
            ConfigLoader.Save(_config);
            StatusMessage = "Settings saved successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
        }
    }
}
