using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private string _activeTab = "chat";

    private readonly IServiceProvider _services;

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        NavigateToChat();
    }

    [RelayCommand]
    public void NavigateToChat()
    {
        ActiveTab = "chat";
        CurrentView = _services.GetRequiredService<ChatViewModel>();
    }

    [RelayCommand]
    public void NavigateToMemoryGraph()
    {
        ActiveTab = "memory";
        CurrentView = _services.GetRequiredService<MemoryGraphViewModel>();
    }

    [RelayCommand]
    public void NavigateToSettings()
    {
        ActiveTab = "settings";
        CurrentView = _services.GetRequiredService<SettingsViewModel>();
    }

    [RelayCommand]
    public void NavigateToActionLog()
    {
        ActiveTab = "log";
        CurrentView = _services.GetRequiredService<ActionLogViewModel>();
    }
}
