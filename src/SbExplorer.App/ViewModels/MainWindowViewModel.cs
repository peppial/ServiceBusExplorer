using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.ViewModels;

public partial class MainWindowViewModel(
    IConnectionStore store,
    ISecretStore secrets,
    INamespaceConnector connector,
    IDialogService dialogs,
    ILogService log) : ObservableObject
{
    private bool _isSavedConnection;

    /// <summary>Saved connections, for File &gt; Saved connections.</summary>
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];

    public ILogService Log => log;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private INamespaceSession? _session;

    [ObservableProperty]
    private ExplorerViewModel? _explorer;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isLogVisible = true;

    public bool IsConnected => Session is not null;
    public bool HasSavedConnections => SavedConnections.Count > 0;

    public string Title => Session is null
        ? "Service Bus Explorer"
        : $"{Session.FullyQualifiedNamespace}{(Session.Profile.ReadOnly ? " (read-only)" : string.Empty)} - Service Bus Explorer";

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            // Read first, then replace: clearing before the await let two overlapping loads both add their results.
            var profiles = await store.LoadAsync();
            SavedConnections.Clear();
            foreach (var profile in profiles)
            {
                SavedConnections.Add(profile);
            }

            OnPropertyChanged(nameof(HasSavedConnections));
        }
        catch (Exception ex)
        {
            log.Error($"Could not load saved connections: {ErrorMessages.Describe(ex)}");
        }
    }

    /// <summary>File &gt; Connect: the connect dialog, opened on the current connection when there is one.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        var dialog = new ConnectDialogViewModel(store, secrets, connector, dialogs, SavedConnections);
        if (Session is { } current && SavedConnections.Any(p => p.Id == current.Profile.Id))
        {
            await dialog.SelectAsync(current.Profile);
        }

        var confirmed = await dialogs.ShowConnectDialogAsync(dialog);
        await LoadAsync();
        if (confirmed && dialog.Result is { } request)
        {
            await ConnectToAsync(request.Profile, request.Secret, request.IsSaved);
        }
    }

    /// <summary>File &gt; Saved connections &gt; name.</summary>
    [RelayCommand]
    private async Task ConnectSavedAsync(ConnectionProfile profile)
    {
        string? secret = null;
        if (profile.Auth == AuthKind.ConnectionString)
        {
            try
            {
                secret = await store.GetSecretAsync(profile);
            }
            catch (Exception ex)
            {
                log.Error($"Could not read the connection string of \"{profile.Name}\": {ErrorMessages.Describe(ex)}");
                return;
            }

            if (secret is null)
            {
                log.Error($"No connection string is stored for \"{profile.Name}\". Use File > Connect to enter it.");
                return;
            }
        }

        await ConnectToAsync(profile, secret, isSaved: true);
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
        await session.DisposeAsync();
        log.Info($"Disconnected from {session.FullyQualifiedNamespace}.");
    }

    [RelayCommand]
    private void ClearLog() => log.Clear();

    [RelayCommand]
    private Task CopyLogAsync() => dialogs.CopyTextAsync(LogText(log.Entries));

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        if (await dialogs.SaveFileAsync("ServiceBusExplorer.log", Encoding.UTF8.GetBytes(LogText(log.Entries))))
        {
            log.Info("The log has been saved.");
        }
    }

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    [RelayCommand]
    private Task AboutAsync() => dialogs.ChooseAsync(
        "About Service Bus Explorer",
        $"Service Bus Explorer {typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3)}\n\n"
        + "A cross-platform rewrite on .NET 10 and Avalonia of the Service Bus Explorer for Windows "
        + "(github.com/paolosalvatori/ServiceBusExplorer). MIT licensed.",
        ["OK"]);

    public static string LogText(IEnumerable<LogEntry> entries) => string.Join(Environment.NewLine, entries.Select(e => e.Text));

    private CancellationTokenSource? _connecting;

    [RelayCommand]
    private void CancelConnect() => _connecting?.Cancel();

    private async Task ConnectToAsync(ConnectionProfile profile, string? secret, bool isSaved)
    {
        if (Session is not null)
        {
            await DisconnectAsync();
        }

        using var connecting = _connecting = new CancellationTokenSource();
        IsConnecting = true;
        log.Info($"Connecting to {profile.FullyQualifiedNamespace ?? profile.Name}…");
        try
        {
            Session = await connector.ConnectAsync(profile, secret, connecting.Token);
            _isSavedConnection = isSaved;
            log.Info(Session.CanManage
                ? $"Connected to sb://{Session.FullyQualifiedNamespace}/."
                : $"Connected to sb://{Session.FullyQualifiedNamespace}/, but the namespace can't be listed with this credential"
                  + (Session.ScopedEntity is { } scoped ? $" (the connection string is scoped to '{scoped}')" : string.Empty)
                  + (Session.LimitedReason is { } reason ? $". Service Bus said: {reason}" : "."));
            if (profile.ReadOnly)
            {
                log.Info("Read-only connection: messages and entities can be browsed and peeked but not changed.");
            }
            await RememberHostAsync(Session.FullyQualifiedNamespace);
            Explorer = new ExplorerViewModel(Session, dialogs, savePinnedEntities: SavePinnedAsync, log: log);
            await Explorer.RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            log.Info("Connect cancelled.");
        }
        catch (Exception ex)
        {
            log.Error($"Could not connect to {profile.FullyQualifiedNamespace ?? profile.Name}: {ErrorMessages.Describe(ex)}");
        }
        finally
        {
            dialogs.CloseDeviceCode();
            IsConnecting = false;
            _connecting = null;
        }
    }

    // Saved profiles from before the host was stored get it after their first successful connect.
    private async Task RememberHostAsync(string host)
    {
        if (!_isSavedConnection || Session is null || Session.Profile.FullyQualifiedNamespace is not null)
        {
            return;
        }

        await SaveProfileAsync(Session.Profile with { FullyQualifiedNamespace = host });
    }

    private Task SavePinnedAsync(IReadOnlyList<string> pinned) =>
        _isSavedConnection && Session is not null
            ? SaveProfileAsync(Current() with { PinnedEntities = pinned })
            : Task.CompletedTask;

    private ConnectionProfile Current() => SavedConnections.FirstOrDefault(p => p.Id == Session!.Profile.Id) ?? Session!.Profile;

    private async Task SaveProfileAsync(ConnectionProfile profile)
    {
        await store.SaveAsync(profile, secret: null);
        var index = SavedConnections.ToList().FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            SavedConnections[index] = profile;
        }
    }
}
