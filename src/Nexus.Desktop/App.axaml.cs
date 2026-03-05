using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Core.Config;
using Nexus.Desktop.ViewModels;
using Nexus.Desktop.Views;

namespace Nexus.Desktop;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = ConfigLoader.Load();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddNexusAgent(config);
        serviceCollection.AddSingleton<MainWindowViewModel>();
        serviceCollection.AddTransient<ChatViewModel>();
        serviceCollection.AddTransient<MemoryGraphViewModel>();
        serviceCollection.AddTransient<SettingsViewModel>();
        serviceCollection.AddTransient<ActionLogViewModel>();
        Services = serviceCollection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
