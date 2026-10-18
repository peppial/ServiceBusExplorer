using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using Xunit;

namespace SbExplorer.App.Tests;

public class MainWindowViewModelTests
{
    private const string ConnectionString =
        "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";

    private readonly FakeSecretStore _secrets = new();
    private readonly FakeConnector _connector = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly LogService _log = new();
    private readonly FakeConnectionStore _store;
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelTests()
    {
        _store = new FakeConnectionStore(_secrets);
        _vm = new MainWindowViewModel(_store, _secrets, _connector, _dialogs, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private string LastLog => _log.Entries[^1].Message;

    private async Task<ConnectionProfile> SeedAsync(string name = "Orders", string? host = "orders.servicebus.windows.net")
    {
        var profile = new ConnectionProfile { Name = name, FullyQualifiedNamespace = host };
        await _store.SaveAsync(profile, ConnectionString, Ct);
        await _vm.LoadCommand.ExecuteAsync(null);
        return profile;
    }

    [Fact]
    public async Task Load_lists_saved_connections_without_reading_secrets()
    {
        await SeedAsync();

        Assert.Equal("Orders", Assert.Single(_vm.SavedConnections).Name);
        Assert.True(_vm.HasSavedConnections);
        Assert.Equal(0, _secrets.Reads);
        Assert.Equal("Service Bus Explorer", _vm.Title);
    }

    [Fact]
    public async Task Overlapping_loads_list_each_saved_connection_once()
    {
        await SeedAsync("Local emulator", "localhost");
        await SeedAsync("test");
        _store.LoadDelay = TimeSpan.FromMilliseconds(50);

        // Startup loads when the app starts and again when the window opens.
        await Task.WhenAll(_vm.LoadCommand.ExecuteAsync(null), _vm.LoadCommand.ExecuteAsync(null));

        Assert.Equal(["Local emulator", "test"], _vm.SavedConnections.Select(p => p.Name));
    }

    [Fact]
    public async Task Saved_connection_menu_connects_and_loads_the_tree()
    {
        var profile = await SeedAsync();

        await _vm.ConnectSavedCommand.ExecuteAsync(profile);

        Assert.True(_vm.IsConnected);
        Assert.NotNull(_vm.Explorer);
        Assert.Equal(ConnectionString, _connector.Requests.Single().Secret);
        Assert.Equal("orders.servicebus.windows.net - Service Bus Explorer", _vm.Title);
        Assert.Contains(_log.Entries, e => e.Message == "Connected to sb://orders.servicebus.windows.net/.");

        // A device-code window, if sign-in showed one, goes away once connecting is over.
        Assert.Equal(1, _dialogs.DeviceCodeCloses);
    }

    [Fact]
    public async Task Connect_dialog_ok_saves_the_connection_by_default()
    {
        _dialogs.OnConnectDialog = async dialog =>
        {
            dialog.Editor.ConnectionString = ConnectionString;
            await dialog.OkCommand.ExecuteAsync(null);
            return true;
        };

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(_vm.IsConnected);
        var saved = Assert.Single(_store.Profiles);
        Assert.Equal("orders.servicebus.windows.net", saved.Name);
        Assert.Equal(ConnectionString, _secrets.Secrets[saved.SecretKey]);
        Assert.Equal("orders.servicebus.windows.net", Assert.Single(_vm.SavedConnections).Name);
    }

    [Fact]
    public async Task Connect_dialog_can_connect_without_saving()
    {
        _dialogs.OnConnectDialog = async dialog =>
        {
            dialog.Editor.ConnectionString = ConnectionString;
            dialog.SaveOnConnect = false;
            await dialog.OkCommand.ExecuteAsync(null);
            return true;
        };

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(_vm.IsConnected);
        Assert.Empty(_store.Profiles);
    }

    [Fact]
    public async Task Cancelled_connect_dialog_does_nothing_but_picks_up_saved_changes()
    {
        _dialogs.OnConnectDialog = async dialog =>
        {
            dialog.Editor.Name = "Saved in dialog";
            dialog.Editor.ConnectionString = ConnectionString;
            await dialog.SaveCommand.ExecuteAsync(null);
            return false;
        };

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(_vm.IsConnected);
        Assert.Equal("Saved in dialog", Assert.Single(_vm.SavedConnections).Name);
    }

    [Fact]
    public async Task Connect_failure_is_logged_as_an_error()
    {
        var profile = await SeedAsync();
        _connector.Failure = new InvalidOperationException("No route to host\nstack trace noise");

        await _vm.ConnectSavedCommand.ExecuteAsync(profile);

        Assert.False(_vm.IsConnected);
        Assert.True(_log.Entries[^1].IsError);
        Assert.Equal("Could not connect to orders.servicebus.windows.net: No route to host", LastLog);
    }

    [Fact]
    public async Task Missing_secret_is_reported_instead_of_connecting()
    {
        var profile = new ConnectionProfile { Name = "Orders" };
        await _store.SaveAsync(profile, secret: null, Ct);
        await _vm.LoadCommand.ExecuteAsync(null);

        await _vm.ConnectSavedCommand.ExecuteAsync(profile);

        Assert.Empty(_connector.Requests);
        Assert.Contains("No connection string is stored", LastLog);
    }

    [Fact]
    public async Task Disconnect_disposes_session_and_clears_the_tree()
    {
        await _vm.ConnectSavedCommand.ExecuteAsync(await SeedAsync());

        await _vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(_vm.IsConnected);
        Assert.Null(_vm.Explorer);
        Assert.True(Assert.Single(_connector.Sessions).Disposed);
        Assert.StartsWith("Disconnected from", LastLog);
    }

    [Fact]
    public async Task Connecting_elsewhere_closes_the_current_session()
    {
        var first = await SeedAsync("First");
        var second = await SeedAsync("Second");
        await _vm.ConnectSavedCommand.ExecuteAsync(first);

        await _vm.ConnectSavedCommand.ExecuteAsync(second);

        Assert.True(_connector.Sessions[0].Disposed);
        Assert.Equal("Second", _vm.Session!.Profile.Name);
    }

    [Fact]
    public async Task Connecting_backfills_the_host_of_older_saved_profiles()
    {
        var profile = await SeedAsync(host: null);

        await _vm.ConnectSavedCommand.ExecuteAsync(profile);

        Assert.Equal("fake.servicebus.windows.net", _store.Profiles.Single().FullyQualifiedNamespace);
        Assert.Equal("fake.servicebus.windows.net", _vm.SavedConnections.Single().FullyQualifiedNamespace);
    }

    [Fact]
    public async Task Log_commands_copy_save_and_clear()
    {
        _log.Info("one");
        _log.Error("two");

        await _vm.CopyLogCommand.ExecuteAsync(null);
        await _vm.SaveLogCommand.ExecuteAsync(null);
        Assert.EndsWith("> two", _dialogs.CopiedText);
        Assert.Equal("ServiceBusExplorer.log", _dialogs.SavedFiles.Single().Name);

        _vm.ClearLogCommand.Execute(null);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void Log_window_can_be_hidden()
    {
        _vm.ToggleLogCommand.Execute(null);

        Assert.False(_vm.IsLogVisible);
    }
}
