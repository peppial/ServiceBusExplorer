using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;
using SbExplorer.Core.Connections;
using Xunit;

namespace SbExplorer.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Shows_saved_connections_and_status()
    {
        var secrets = new FakeSecretStore();
        var store = new FakeConnectionStore(secrets);
        await store.SaveAsync(new ConnectionProfile { Name = "Orders" },
            "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v");
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector(), new FakeDialogs());
        await vm.LoadCommand.ExecuteAsync(null);

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.Equal("Not connected", window.FindControl<TextBlock>("StatusText")?.Text);
        Assert.Equal(1, window.FindControl<ListBox>("ConnectionList")?.ItemCount);
    }

    [AvaloniaFact]
    public void Connection_editor_window_renders()
    {
        var window = new ConnectionEditorWindow { DataContext = new ConnectionEditorViewModel(new FakeConnector()) };

        window.Show();

        Assert.Equal("New connection", window.Title);
    }
}
