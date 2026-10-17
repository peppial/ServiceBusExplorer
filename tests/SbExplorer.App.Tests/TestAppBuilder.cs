using Avalonia;
using Avalonia.Headless;
using SbExplorer.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SbExplorer.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
