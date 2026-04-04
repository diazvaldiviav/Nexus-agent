using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Config;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly NexusConfig _config;
    private SettingsSnapshot? _lastSnapshot;
    private bool _isLoading;

    [ObservableProperty] private string _localProvider = "ollama";
    [ObservableProperty] private string _localModel = "qwen3:14b";
    [ObservableProperty] private string _localEndpoint = "http://localhost:11434";
    [ObservableProperty] private string _cloudProvider = "anthropic";
    [ObservableProperty] private string _cloudModel = "claude-sonnet-4-5-20250929";
    [ObservableProperty] private string _geminiApiKey = string.Empty;
    [ObservableProperty] private string _anthropicApiKey = string.Empty;
    [ObservableProperty] private string _openAiApiKey = string.Empty;
    [ObservableProperty] private string _embeddingsModel = "nomic-embed-text";
    [ObservableProperty] private decimal _decayLambda = 0.05m;
    [ObservableProperty] private int _summarizationInterval = 10;
    [ObservableProperty] private int _recentInteractionsFetchLimit = 5;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasSuccess;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    private string? _decayLambdaError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    private string? _localEndpointError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    private string? _summarizationIntervalError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    private string? _recentInteractionsFetchLimitError;
    [ObservableProperty] private string? _apiKeyWarning;

    public bool HasValidationErrors =>
        DecayLambdaError is not null ||
        LocalEndpointError is not null ||
        SummarizationIntervalError is not null ||
        RecentInteractionsFetchLimitError is not null;

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
        _isLoading = true;
        try
        {
            LocalProvider = _config.Models.Local.Provider;
            LocalModel = _config.Models.Local.Model;
            LocalEndpoint = _config.Models.Local.Endpoint ?? "http://localhost:11434";
            CloudProvider = _config.Models.Cloud.Provider;
            CloudModel = _config.Models.Cloud.Model;
            GeminiApiKey = _config.Models.Gemini?.ApiKey ?? string.Empty;
            AnthropicApiKey = _config.Models.Anthropic?.ApiKey ?? string.Empty;
            OpenAiApiKey = _config.Models.OpenAi?.ApiKey ?? string.Empty;
            EmbeddingsModel = _config.Embeddings.Model;
            DecayLambda = (decimal)_config.Memory.RelevanceDecayLambda;
            SummarizationInterval = _config.Memory.SummarizationInterval;
            RecentInteractionsFetchLimit = _config.Memory.RecentInteractionsFetchLimit;

            _lastSnapshot = CaptureSnapshot();
            IsDirty = false;
            DecayLambdaError = null;
            LocalEndpointError = null;
            SummarizationIntervalError = null;
            RecentInteractionsFetchLimitError = null;
            ApiKeyWarning = ConfigValidator.CheckApiKeyWarning(CloudProvider, GeminiApiKey, AnthropicApiKey, OpenAiApiKey);
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveSettings()
    {
        _config.Models.Local.Provider = LocalProvider;
        _config.Models.Local.Model = LocalModel;
        _config.Models.Local.Endpoint = LocalEndpoint;
        _config.Models.Cloud.Provider = CloudProvider;
        _config.Models.Cloud.Model = CloudModel;
        (_config.Models.Gemini ??= new ProviderKeyConfig()).ApiKey =
            string.IsNullOrWhiteSpace(GeminiApiKey) ? null : GeminiApiKey;
        (_config.Models.Anthropic ??= new ProviderKeyConfig()).ApiKey =
            string.IsNullOrWhiteSpace(AnthropicApiKey) ? null : AnthropicApiKey;
        (_config.Models.OpenAi ??= new ProviderKeyConfig()).ApiKey =
            string.IsNullOrWhiteSpace(OpenAiApiKey) ? null : OpenAiApiKey;
        _config.Embeddings.Model = EmbeddingsModel;
        _config.Memory.RelevanceDecayLambda = (double)DecayLambda;
        _config.Memory.SummarizationInterval = SummarizationInterval;
        _config.Memory.RecentInteractionsFetchLimit = RecentInteractionsFetchLimit;

        try
        {
            ConfigLoader.Save(_config);
            _lastSnapshot = CaptureSnapshot();
            IsDirty = false;
            SaveSettingsCommand.NotifyCanExecuteChanged();
            HasSuccess = true;
            HasError = false;
            StatusMessage = "Settings saved successfully!";
        }
        catch (Exception ex)
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = $"Error saving: {ex.Message}";
        }
    }

    private bool CanSave() => IsDirty && !HasValidationErrors;

    private SettingsSnapshot CaptureSnapshot() => new(
        LocalProvider, LocalModel, LocalEndpoint,
        CloudProvider, CloudModel,
        GeminiApiKey, AnthropicApiKey, OpenAiApiKey,
        EmbeddingsModel, DecayLambda,
        SummarizationInterval, RecentInteractionsFetchLimit);

    private void CheckDirty()
    {
        if (_isLoading) return;
        var wasDirty = IsDirty;
        IsDirty = _lastSnapshot is not null && CaptureSnapshot() != _lastSnapshot;
        if (wasDirty != IsDirty)
        {
            HasError = false;
            HasSuccess = false;
        }
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnDecayLambdaChanged(decimal value)
    {
        if (_isLoading) return;
        DecayLambdaError = ConfigValidator.ValidateDecayLambda((double)value);
        CheckDirty();
    }

    partial void OnLocalEndpointChanged(string value)
    {
        if (_isLoading) return;
        LocalEndpointError = ConfigValidator.ValidateLocalEndpoint(value);
        CheckDirty();
    }

    partial void OnSummarizationIntervalChanged(int value)
    {
        if (_isLoading) return;
        SummarizationIntervalError = ConfigValidator.ValidateSummarizationInterval(value);
        CheckDirty();
    }

    partial void OnRecentInteractionsFetchLimitChanged(int value)
    {
        if (_isLoading) return;
        RecentInteractionsFetchLimitError = ConfigValidator.ValidateRecentInteractionsFetchLimit(value);
        CheckDirty();
    }

    partial void OnCloudProviderChanged(string value)
    {
        ApiKeyWarning = ConfigValidator.CheckApiKeyWarning(value, GeminiApiKey, AnthropicApiKey, OpenAiApiKey);
        CheckDirty();
    }

    partial void OnGeminiApiKeyChanged(string value)
    {
        ApiKeyWarning = ConfigValidator.CheckApiKeyWarning(CloudProvider, value, AnthropicApiKey, OpenAiApiKey);
        CheckDirty();
    }

    partial void OnAnthropicApiKeyChanged(string value)
    {
        ApiKeyWarning = ConfigValidator.CheckApiKeyWarning(CloudProvider, GeminiApiKey, value, OpenAiApiKey);
        CheckDirty();
    }

    partial void OnOpenAiApiKeyChanged(string value)
    {
        ApiKeyWarning = ConfigValidator.CheckApiKeyWarning(CloudProvider, GeminiApiKey, AnthropicApiKey, value);
        CheckDirty();
    }

    partial void OnLocalProviderChanged(string value) => CheckDirty();
    partial void OnLocalModelChanged(string value) => CheckDirty();
    partial void OnCloudModelChanged(string value) => CheckDirty();
    partial void OnEmbeddingsModelChanged(string value) => CheckDirty();

    private record SettingsSnapshot(
        string LocalProvider, string LocalModel, string LocalEndpoint,
        string CloudProvider, string CloudModel,
        string GeminiApiKey, string AnthropicApiKey, string OpenAiApiKey,
        string EmbeddingsModel, decimal DecayLambda,
        int SummarizationInterval, int RecentInteractionsFetchLimit);
}
