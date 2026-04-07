using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Connectors;
using Nexus.Core.Config;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly NexusConfig _config;
    private readonly McpLifecycleService _mcpLifecycle;
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
    [ObservableProperty] private string _newMcpServerName = string.Empty;
    [ObservableProperty] private string _newMcpServerTransport = "stdio";
    [ObservableProperty] private string _newMcpServerCommandOrUrl = string.Empty;
    [ObservableProperty] private string _newMcpServerArgs = string.Empty;
    [ObservableProperty] private McpServerRow? _selectedMcpServer;
    [ObservableProperty] private bool _isMcpBusy;

    public bool HasValidationErrors =>
        DecayLambdaError is not null ||
        LocalEndpointError is not null ||
        SummarizationIntervalError is not null ||
        RecentInteractionsFetchLimitError is not null;

    public ObservableCollection<string> AvailableLocalModels { get; } = new(
        new[] { "qwen3:14b", "qwen3:8b", "llama3.2:3b", "mistral:7b", "phi3:mini" });

    public ObservableCollection<string> AvailableCloudProviders { get; } = new(
        new[] { "anthropic", "openai", "google" });

    public ObservableCollection<string> AvailableMcpTransports { get; } = new(
        new[] { "stdio", "sse" });

    public ObservableCollection<McpServerRow> McpServers { get; } = new();
    public bool HasMcpServers => McpServers.Count > 0;
    public bool IsNotMcpBusy => !IsMcpBusy;

    public SettingsViewModel(NexusConfig config, McpLifecycleService mcpLifecycle)
    {
        _config = config;
        _mcpLifecycle = mcpLifecycle ?? throw new ArgumentNullException(nameof(mcpLifecycle));
        McpServers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMcpServers));
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
            RefreshMcpServers();
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
    partial void OnIsMcpBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotMcpBusy));

    [RelayCommand]
    private async Task ConnectMcpServerAsync(McpServerRow? server)
    {
        if (server is null || IsMcpBusy) return;

        var entry = _config.Mcp.Servers.FirstOrDefault(s =>
            string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = $"MCP server '{server.Name}' is not configured.";
            return;
        }

        IsMcpBusy = true;
        try
        {
            var result = await _mcpLifecycle.ConnectServerAsync(entry);
            if (result.Success)
            {
                HasSuccess = true;
                HasError = false;
                StatusMessage = $"Connected to MCP server '{result.ServerName}' ({result.ToolCount} tools).";
            }
            else
            {
                HasError = true;
                HasSuccess = false;
                StatusMessage = $"Failed to connect MCP server '{result.ServerName}': {result.ErrorMessage ?? "connection failed"}";
            }
        }
        finally
        {
            IsMcpBusy = false;
            RefreshMcpServers();
        }
    }

    [RelayCommand]
    private async Task DisconnectMcpServerAsync(McpServerRow? server)
    {
        if (server is null || IsMcpBusy) return;

        var index = _config.Mcp.Servers.FindIndex(s =>
            string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = $"MCP server '{server.Name}' is not configured.";
            return;
        }
        var existing = _config.Mcp.Servers[index];

        IsMcpBusy = true;
        try
        {
            await _mcpLifecycle.DisconnectServerAsync(server.Name);
            _config.Mcp.Servers.RemoveAt(index);
            if (SaveMcpConfig())
            {
                HasSuccess = true;
                HasError = false;
                StatusMessage = $"Disconnected and removed MCP server '{server.Name}'.";
            }
            else
            {
                _config.Mcp.Servers.Insert(index, existing);
            }
        }
        finally
        {
            IsMcpBusy = false;
            RefreshMcpServers();
        }
    }

    [RelayCommand]
    private async Task AddMcpServerAsync()
    {
        if (IsMcpBusy) return;

        var name = NewMcpServerName.Trim();
        var transport = NewMcpServerTransport.Trim().ToLowerInvariant();
        var commandOrUrl = NewMcpServerCommandOrUrl.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = "MCP server name is required.";
            return;
        }

        if (transport is not ("stdio" or "sse"))
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = "MCP transport must be 'stdio' or 'sse'.";
            return;
        }

        if (string.IsNullOrWhiteSpace(commandOrUrl))
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = transport == "sse"
                ? "MCP URL is required for SSE transport."
                : "MCP command is required for stdio transport.";
            return;
        }

        if (transport == "sse" && !Uri.TryCreate(commandOrUrl, UriKind.Absolute, out _))
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = "MCP SSE URL must be an absolute URL.";
            return;
        }

        var entry = new McpServerEntry
        {
            Name = name,
            Transport = transport,
            Command = transport == "stdio" ? commandOrUrl : null,
            Url = transport == "sse" ? commandOrUrl : null,
            Args = transport == "stdio"
                ? NewMcpServerArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>()
        };

        IsMcpBusy = true;
        try
        {
            var result = await _mcpLifecycle.ConnectServerAsync(entry);
            if (!result.Success)
            {
                HasError = true;
                HasSuccess = false;
                StatusMessage = $"Failed to connect MCP server '{entry.Name}': {result.ErrorMessage ?? "connection failed"}";
                return;
            }

            var saved = UpsertServerAndSave(entry);

            NewMcpServerName = string.Empty;
            NewMcpServerTransport = "stdio";
            NewMcpServerCommandOrUrl = string.Empty;
            NewMcpServerArgs = string.Empty;

            if (saved)
            {
                HasSuccess = true;
                HasError = false;
                StatusMessage = $"Connected and saved MCP server '{entry.Name}' ({result.ToolCount} tools).";
            }
        }
        finally
        {
            IsMcpBusy = false;
            RefreshMcpServers();
        }
    }

    private bool UpsertServerAndSave(McpServerEntry entry)
    {
        var existingIndex = _config.Mcp.Servers.FindIndex(s =>
            string.Equals(s.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        var hadExisting = existingIndex >= 0;
        McpServerEntry? existing = hadExisting ? _config.Mcp.Servers[existingIndex] : null;

        if (hadExisting)
            _config.Mcp.Servers.RemoveAt(existingIndex);

        var insertedIndex = hadExisting ? existingIndex : _config.Mcp.Servers.Count;
        _config.Mcp.Servers.Insert(insertedIndex, entry);

        if (SaveMcpConfig())
            return true;

        _config.Mcp.Servers.RemoveAt(insertedIndex);
        if (hadExisting && existing is not null)
            _config.Mcp.Servers.Insert(existingIndex, existing);

        return false;
    }

    private void RefreshMcpServers()
    {
        var statuses = _mcpLifecycle.GetServerStatuses(_config.Mcp.Servers);

        McpServers.Clear();
        foreach (var status in statuses)
        {
            McpServers.Add(new McpServerRow(
                status.ServerName,
                status.Transport,
                status.CommandOrUrl,
                status.IsConnected,
                status.ToolCount));
        }
        OnPropertyChanged(nameof(HasMcpServers));

        if (SelectedMcpServer is not null)
        {
            SelectedMcpServer = McpServers.FirstOrDefault(s =>
                string.Equals(s.Name, SelectedMcpServer.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool SaveMcpConfig()
    {
        try
        {
            ConfigLoader.Save(_config);
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            HasSuccess = false;
            StatusMessage = $"Warning: could not save MCP config: {ex.Message}";
            return false;
        }
    }

    private record SettingsSnapshot(
        string LocalProvider, string LocalModel, string LocalEndpoint,
        string CloudProvider, string CloudModel,
        string GeminiApiKey, string AnthropicApiKey, string OpenAiApiKey,
        string EmbeddingsModel, decimal DecayLambda,
        int SummarizationInterval, int RecentInteractionsFetchLimit);
}

public sealed class McpServerRow
{
    public McpServerRow(string name, string transport, string commandOrUrl, bool isConnected, int toolCount)
    {
        Name = name;
        Transport = transport;
        CommandOrUrl = commandOrUrl;
        IsConnected = isConnected;
        ToolCount = toolCount;
    }

    public string Name { get; }
    public string Transport { get; }
    public string CommandOrUrl { get; }
    public bool IsConnected { get; }
    public int ToolCount { get; }
    public string Status => IsConnected ? $"Connected ({ToolCount} tools)" : "Disconnected";
}
