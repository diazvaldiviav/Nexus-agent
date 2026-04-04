using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Abstractions;
using Nexus.Core.Services;

namespace Nexus.Desktop.ViewModels;

public partial class ChatMessage : ObservableObject
{
    public string Role { get; init; } = "user";
    [ObservableProperty]
    private string _content = string.Empty;
    [ObservableProperty]
    private string _modelInfo = string.Empty;
    public bool IsUser => Role == "user";
    public bool IsAssistantNormal => !IsUser && !IsError;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssistantNormal))]
    private bool _isError;
}

public partial class ChatViewModel : ObservableObject
{
    private readonly IAgentService _agentService;
    private string _lastUserMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _errorDetail = "";

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public bool HasMessages => Messages.Count > 0;

    public ChatViewModel(IAgentService agentService)
    {
        _agentService = agentService;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));
    }

    protected virtual void DispatchToUI(Action action)
        => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    [RelayCommand]
    private void SetExamplePrompt(string prompt)
    {
        InputText = prompt;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        DispatchToUI(() => { HasError = false; ErrorMessage = ""; ErrorDetail = ""; });

        var userMessage = InputText;
        _lastUserMessage = userMessage;
        InputText = string.Empty;
        IsProcessing = true;
        StatusText = "Processing...";

        Messages.Add(new ChatMessage { Role = "user", Content = userMessage });

        var assistantMessage = new ChatMessage { Role = "assistant" };
        Messages.Add(assistantMessage);

        var accumulated = new StringBuilder();
        var extractedEntityCount = 0;

        void OnEntitiesExtracted(int count)
        {
            var total = Interlocked.Add(ref extractedEntityCount, count);
            DispatchToUI(() =>
                StatusText = $"Remembered {total} entities");
        }

        try
        {
            await foreach (var token in _agentService.ChatStreamAsync(userMessage, OnEntitiesExtracted))
            {
                // Route tool execution status to StatusText only; do not accumulate into content.
                if (token.Contains("[Executing tool:"))
                {
                    DispatchToUI(() =>
                        StatusText = token.Trim());
                    continue;
                }

                accumulated.Append(token);
                var displayText = ToolCallParser.GetTextBeforeToolCall(accumulated.ToString());
                DispatchToUI(() =>
                    assistantMessage.Content = displayText);
            }

            if (Volatile.Read(ref extractedEntityCount) == 0)
            {
                DispatchToUI(() => StatusText = "Ready");
            }
        }
        catch (Exception ex)
        {
            var (_, userMsg, detail) = ErrorClassifier.Classify(ex);
            DispatchToUI(() =>
            {
                HasError = true;
                ErrorMessage = userMsg;
                ErrorDetail = detail;
                assistantMessage.Content = userMsg;
                assistantMessage.IsError = true;
                assistantMessage.ModelInfo = "error";
                StatusText = "Error";
            });
        }
        finally
        {
            DispatchToUI(() => IsProcessing = false);
        }
    }

    private bool CanSend() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastUserMessage)) return;
        if (IsProcessing) return;
        InputText = _lastUserMessage;
        DismissError();
        await SendAsync();
    }

    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        ErrorMessage = "";
        ErrorDetail = "";
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        IsProcessing = true;
        StatusText = "Saving summary...";
        try
        {
            await _agentService.ClearHistoryAsync();
            Messages.Clear();
            StatusText = "History cleared";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
