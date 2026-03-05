using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core;
using System.Collections.ObjectModel;

namespace Nexus.Desktop.ViewModels;

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string ModelInfo { get; set; } = string.Empty;
    public bool IsUser => Role == "user";
}

public partial class ChatViewModel : ObservableObject
{
    private readonly AgentService _agentService;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "Ready";

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatViewModel(AgentService agentService)
    {
        _agentService = agentService;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userMessage = InputText;
        InputText = string.Empty;
        IsProcessing = true;
        StatusText = "Processing...";

        Messages.Add(new ChatMessage { Role = "user", Content = userMessage });

        try
        {
            var response = await _agentService.ChatAsync(userMessage);
            Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content,
                ModelInfo = $"{response.ModelUsed} • {response.DurationMs}ms"
            });

            StatusText = response.ExtractedEntities.Count > 0
                ? $"Remembered {response.ExtractedEntities.Count} entities"
                : "Ready";
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"Error: {ex.Message}\n\nPlease ensure Ollama is running.",
                ModelInfo = "error"
            });
            StatusText = "Error";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanSend() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private void ClearHistory()
    {
        _agentService.ClearHistory();
        Messages.Clear();
        StatusText = "History cleared";
    }
}
