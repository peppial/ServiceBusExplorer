using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.ViewModels;

public partial class MainWindowViewModel(
    IConnectionStore store,
    ISecretStore secrets,
    INamespaceConnector connector,
    IDialogService dialogs) : ObservableObject
{
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];

    public bool SecretsAreInsecure => !secrets.IsSecure;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(ConnectCommand))]
    private ConnectionItemViewModel? _selectedConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private INamespaceSession? _session;

    [ObservableProperty]
    private ExplorerViewModel? _explorer;

    [ObservableProperty]
    private string _status = "Not connected";

    [ObservableProperty]
    private bool _hasError;

    public bool IsConnected => Session is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Connections.Clear();
            foreach (var profile in await store.LoadAsync())
            {
                Connections.Add(new ConnectionItemViewModel(profile, await HostOfAsync(profile)));
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load saved connections: {ErrorMessages.Describe(ex)}", isError: true);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editor = new ConnectionEditorViewModel(connector);
        if (!await dialogs.ShowConnectionEditorAsync(editor))
        {
            return;
        }

        var profile = editor.ToProfile();
        await store.SaveAsync(profile, editor.Secret);
        var item = new ConnectionItemViewModel(profile, await HostOfAsync(profile));
        Connections.Add(item);
        SelectedConnection = item;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        var item = SelectedConnection!;
        var editor = new ConnectionEditorViewModel(connector, item.Profile, await store.GetSecretAsync(item.Profile));
        if (!await dialogs.ShowConnectionEditorAsync(editor))
        {
            return;
        }

        item.Profile = editor.ToProfile();
        await store.SaveAsync(item.Profile, editor.Secret);
        item.Host = await HostOfAsync(item.Profile);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var item = SelectedConnection!;
        if (!await dialogs.ConfirmAsync("Delete connection", $"Delete the saved connection \"{item.Name}\"? This only removes it from this app.", "Delete"))
        {
            return;
        }

        if (item.IsConnected)
        {
            await DisconnectAsync();
        }

        await store.DeleteAsync(item.Profile.Id);
        Connections.Remove(item);
    }

    [RelayCommand(CanExecute = nameof(HasSelection), IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var item = SelectedConnection!;
        if (Session is not null)
        {
            await DisconnectAsync();
        }

        SetStatus($"Connecting to {item.Name}…");
        try
        {
            var secret = await store.GetSecretAsync(item.Profile, cancellationToken);
            if (item.Profile.Auth == AuthKind.ConnectionString && secret is null)
            {
                SetStatus($"No connection string is stored for \"{item.Name}\". Edit the connection to add it.", isError: true);
                return;
            }

            Session = await connector.ConnectAsync(item.Profile, secret, cancellationToken);
            item.IsConnected = true;
            SetStatus(Session.CanManage
                ? $"Connected to {Session.FullyQualifiedNamespace}"
                : $"Connected to {Session.FullyQualifiedNamespace} without management access (the credential has no Manage claim)");
            Explorer = new ExplorerViewModel(Session, dialogs, savePinnedEntities: pinned => SavePinnedAsync(item, pinned));
            await Explorer.RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Connect cancelled");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not connect to {item.Name}: {ErrorMessages.Describe(ex)}", isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task DisconnectAsync()
    {
        if (Session is null)
        {
            return;
        }

        var session = Session;
        if (Explorer is { } explorer)
        {
            explorer.RefreshCancelCommand.Execute(null);
            Explorer = null;
            await explorer.DisposeAsync();
        }

        Session = null;
        foreach (var item in Connections.Where(c => c.Profile.Id == session.Profile.Id))
        {
            item.IsConnected = false;
        }

        await session.DisposeAsync();
        SetStatus("Not connected");
    }

    private bool HasSelection() => SelectedConnection is not null;

    private async Task SavePinnedAsync(ConnectionItemViewModel item, IReadOnlyList<string> pinned)
    {
        item.Profile = item.Profile with { PinnedEntities = pinned };
        await store.SaveAsync(item.Profile, secret: null);
    }

    private void SetStatus(string message, bool isError = false)
    {
        Status = message;
        HasError = isError;
    }

    private async Task<string?> HostOfAsync(ConnectionProfile profile)
    {
        if (profile.Auth == AuthKind.Entra)
        {
            return profile.FullyQualifiedNamespace;
        }

        return ConnectionStringInfo.TryParse(await store.GetSecretAsync(profile), out var info, out _)
            ? info!.FullyQualifiedNamespace
            : null;
    }
}
