using Avalonia;
using Avalonia.Headless;
using Nexus.Desktop;

[assembly: AvaloniaTestApplication(typeof(Nexus.Desktop.Tests.TestAppBuilder))]

namespace Nexus.Desktop.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
