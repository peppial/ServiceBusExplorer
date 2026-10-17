using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using Xunit;

namespace SbExplorer.App.Tests;

public class ConnectDialogViewModelTests
{
    private const string ConnectionString = "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";

    private readonly FakeSecretStore _secrets = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeConnectionStore _store;

    public ConnectDialogViewModelTests() => _store = new FakeConnectionStore(_secrets);

    private ConnectDialogViewModel Create() =>
        new(_store, _secrets, new FakeConnector(), _dialogs, _store.Profiles.ToList());

    [Fact]
    public async Task Picking_a_saved_namespace_loads_its_settings()
    {
        var saved = new ConnectionProfile { Name = "Orders", FullyQualifiedNamespace = "orders.servicebus.windows.net" };
        await _store.SaveAsync(saved, ConnectionString, TestContext.Current.CancellationToken);
        var dialog = Create();
        Assert.False(dialog.OkCommand.CanExecute(null));

        await dialog.SelectAsync(saved);

        Assert.Equal(("Orders", ConnectionString), (dialog.Editor.Name, dialog.Editor.ConnectionString));
        Assert.True(dialog.OkCommand.CanExecute(null));
        dialog.OkCommand.Execute(null);
        Assert.True(dialog.Result!.IsSaved);
        Assert.Equal(saved.Id, dialog.Result.Profile.Id);
    }

    [Fact]
    public async Task Save_needs_a_name_and_adds_the_connection()
    {
        var dialog = Create();
        dialog.Editor.ConnectionString = ConnectionString;
        Assert.False(dialog.SaveCommand.CanExecute(null));

        dialog.Editor.Name = "Orders";
        await dialog.SaveCommand.ExecuteAsync(null);

        var profile = Assert.Single(_store.Profiles);
        Assert.Equal("orders.servicebus.windows.net", profile.FullyQualifiedNamespace);
        Assert.Same(dialog.Profiles.Single(), dialog.SelectedProfile);
        Assert.Equal(ConnectionString, _secrets.Secrets[profile.SecretKey]);
    }

    [Fact]
    public async Task Delete_confirms_and_resets_the_form()
    {
        var saved = new ConnectionProfile { Name = "Orders" };
        await _store.SaveAsync(saved, ConnectionString, TestContext.Current.CancellationToken);
        var dialog = Create();
        await dialog.SelectAsync(saved);

        await dialog.DeleteCommand.ExecuteAsync(null);
        Assert.Single(_store.Profiles);

        _dialogs.ConfirmResult = true;
        await dialog.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(_store.Profiles);
        Assert.Null(dialog.SelectedProfile);
        Assert.Equal(string.Empty, dialog.Editor.ConnectionString);
    }

    [Fact]
    public void Ok_without_a_saved_profile_is_not_saved()
    {
        var dialog = Create();
        dialog.Editor.ConnectionString = ConnectionEditorViewModel.EmulatorConnectionString;

        dialog.OkCommand.Execute(null);

        Assert.False(dialog.Result!.IsSaved);
        Assert.Equal("localhost", dialog.Result.Profile.Name);
    }

    [Fact]
    public void Authentication_dropdown_switches_between_sas_and_entra()
    {
        var dialog = Create();

        dialog.Editor.AuthIndex = 1;

        Assert.True(dialog.Editor.UseEntra);
        Assert.False(dialog.Editor.UseConnectionString);
    }
}
