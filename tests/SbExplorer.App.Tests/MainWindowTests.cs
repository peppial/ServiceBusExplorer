using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;
using Xunit;

namespace SbExplorer.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Shows_not_connected_status_on_startup()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        var status = window.FindControl<TextBlock>("StatusText");

        Assert.Equal("Not connected", status?.Text);
    }
}
