using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;
using SbExplorer.Core.Connections;
using Xunit;

namespace SbExplorer.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Shows_menu_panels_and_log_and_lists_saved_connections()
    {
        var secrets = new FakeSecretStore();
        var store = new FakeConnectionStore(secrets);
        await store.SaveAsync(new ConnectionProfile { Name = "Orders", FullyQualifiedNamespace = "orders.servicebus.windows.net" },
            "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v");
        var log = new LogService();
        log.Info("Service Bus Explorer started.");
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector(), new FakeDialogs(), log);
        await vm.LoadCommand.ExecuteAsync(null);

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.Equal("Service Bus Explorer", window.Title);
        var saved = window.FindControl<MenuItem>("SavedConnectionsMenu")!;
        Assert.Equal("Orders  (orders.servicebus.windows.net)", Assert.IsType<MenuItem>(Assert.Single(saved.Items)).Header);
        Assert.Equal(1, window.FindControl<LogView>("LogPanel")?.FindControl<ListBox>("LogList")?.ItemCount);
    }

    [AvaloniaFact]
    public async Task Connected_window_shows_the_namespace_tree()
    {
        var secrets = new FakeSecretStore();
        var store = new FakeConnectionStore(secrets);
        var profile = new ConnectionProfile { Name = "Orders" };
        await store.SaveAsync(profile, "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v");
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector(), new FakeDialogs(), new LogService());
        var window = new MainWindow { DataContext = vm };
        window.Show();

        await vm.ConnectSavedCommand.ExecuteAsync(profile);

        var root = Assert.Single(vm.Explorer!.TreeRoots);
        Assert.Equal("sb://fake.servicebus.windows.net/", root.DisplayName);
        Assert.Equal(["Queues", "Topics"], root.Children.Select(c => c.DisplayName));
    }

    [AvaloniaFact]
    public void Connect_window_renders()
    {
        var store = new FakeConnectionStore(new FakeSecretStore());
        var window = new ConnectWindow
        {
            DataContext = new ConnectDialogViewModel(store, new FakeSecretStore(), new FakeConnector(), new FakeDialogs(), []),
        };

        window.Show();

        Assert.Equal("Connect to a Service Bus Namespace", window.Title);
    }
}
