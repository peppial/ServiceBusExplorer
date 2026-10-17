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
    private readonly FakeConnectionStore _store;
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelTests()
    {
        _store = new FakeConnectionStore(_secrets);
        _vm = new MainWindowViewModel(_store, _secrets, _connector, _dialogs);
    }

    [Fact]
    public async Task Load_lists_saved_connections_with_their_host()
    {
        await _store.SaveAsync(new ConnectionProfile { Name = "Orders" }, ConnectionString, TestContext.Current.CancellationToken);
        await _store.SaveAsync(
            new ConnectionProfile { Name = "Billing", Auth = AuthKind.Entra, FullyQualifiedNamespace = "billing.servicebus.windows.net" },
            null,
            TestContext.Current.CancellationToken);

        await _vm.LoadCommand.ExecuteAsync(null);

        Assert.Collection(
            _vm.Connections,
            c => Assert.Equal(("Orders", "orders.servicebus.windows.net"), (c.Name, c.Host)),
            c => Assert.Equal(("Billing", "billing.servicebus.windows.net"), (c.Name, c.Host)));
    }

    [Fact]
    public async Task Add_saves_profile_and_secret_and_selects_it()
    {
        _dialogs.OnEditor = editor =>
        {
            editor.Name = "Orders";
            editor.ConnectionString = ConnectionString;
            return true;
        };

        await _vm.AddCommand.ExecuteAsync(null);

        var saved = Assert.Single(_store.Profiles);
        Assert.Equal("Orders", saved.Name);
        Assert.Equal(ConnectionString, _secrets.Secrets[saved.SecretKey]);
        Assert.Same(_vm.Connections.Single(), _vm.SelectedConnection);
    }

    [Fact]
    public async Task Cancelled_add_saves_nothing()
    {
        await _vm.AddCommand.ExecuteAsync(null);

        Assert.Empty(_store.Profiles);
        Assert.Empty(_vm.Connections);
    }

    [Fact]
    public async Task Delete_requires_confirmation()
    {
        await SeedAndSelectAsync();

        _dialogs.ConfirmResult = false;
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Single(_store.Profiles);

        _dialogs.ConfirmResult = true;
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(_store.Profiles);
        Assert.Empty(_vm.Connections);
        Assert.Empty(_secrets.Secrets);
    }

    [Fact]
    public async Task Connect_opens_session_and_marks_connection()
    {
        await SeedAndSelectAsync();

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(_vm.IsConnected);
        Assert.True(_vm.SelectedConnection!.IsConnected);
        Assert.False(_vm.HasError);
        Assert.StartsWith("Connected to", _vm.Status);
    }

    [Fact]
    public async Task Connect_failure_shows_error_and_stays_disconnected()
    {
        await SeedAndSelectAsync();
        _connector.Failure = new InvalidOperationException("No route to host\nstack trace noise");

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(_vm.IsConnected);
        Assert.True(_vm.HasError);
        Assert.Equal("Could not connect to Orders: No route to host", _vm.Status);
    }

    [Fact]
    public async Task Disconnect_disposes_session()
    {
        await SeedAndSelectAsync();
        await _vm.ConnectCommand.ExecuteAsync(null);

        await _vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(_vm.IsConnected);
        Assert.True(Assert.Single(_connector.Sessions).Disposed);
        Assert.False(_vm.Connections.Single().IsConnected);
    }

    [Fact]
    public async Task Connecting_to_another_namespace_closes_the_first()
    {
        await SeedAndSelectAsync();
        await _vm.ConnectCommand.ExecuteAsync(null);
        var first = _vm.SelectedConnection!;

        await _store.SaveAsync(new ConnectionProfile { Name = "Second" }, ConnectionString, TestContext.Current.CancellationToken);
        await _vm.LoadCommand.ExecuteAsync(null);
        _vm.SelectedConnection = _vm.Connections.Single(c => c.Name == "Second");
        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(_connector.Sessions[0].Disposed);
        Assert.Equal("Second", _vm.Session!.Profile.Name);
    }

    [Fact]
    public void Warns_when_secrets_are_not_in_a_keychain()
    {
        var vm = new MainWindowViewModel(_store, new FakeSecretStore(isSecure: false), _connector, _dialogs);

        Assert.True(vm.SecretsAreInsecure);
    }

    private async Task SeedAndSelectAsync()
    {
        await _store.SaveAsync(new ConnectionProfile { Name = "Orders" }, ConnectionString, TestContext.Current.CancellationToken);
        await _vm.LoadCommand.ExecuteAsync(null);
        _vm.SelectedConnection = _vm.Connections.Single();
    }
}
