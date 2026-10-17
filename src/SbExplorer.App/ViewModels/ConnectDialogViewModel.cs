using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.ViewModels;

/// <summary>What the user chose to connect to; <see cref="IsSaved"/> when it's a stored connection.</summary>
public sealed record ConnectRequest(ConnectionProfile Profile, string? Secret, bool IsSaved);

/// <summary>
/// "Connect to a Service Bus Namespace": pick a saved namespace or enter settings, optionally save them,
/// then connect with OK.
/// </summary>
public partial class ConnectDialogViewModel : ObservableObject
{
    private readonly IConnectionStore _store;
    private readonly INamespaceConnector _connector;
    private readonly IDialogService _dialogs;
    private bool _suppressLoad;

    public ConnectDialogViewModel(
        IConnectionStore store,
        ISecretStore secrets,
        INamespaceConnector connector,
        IDialogService dialogs,
        IEnumerable<ConnectionProfile> profiles)
    {
        _store = store;
        _connector = connector;
        _dialogs = dialogs;
        SecretsAreInsecure = !secrets.IsSecure;
        Profiles = new ObservableCollection<ConnectionProfile>(profiles);
        _editor = new ConnectionEditorViewModel(connector);
        _editor.PropertyChanged += OnEditorChanged;
    }

    public ObservableCollection<ConnectionProfile> Profiles { get; }
    public bool SecretsAreInsecure { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(SaveCommand))]
    private ConnectionEditorViewModel _editor;

    /// <summary>OK also saves the connection (named after the namespace when no name is given).</summary>
    [ObservableProperty]
    private bool _saveOnConnect = true;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _hasError;

    public ConnectRequest? Result { get; private set; }

    /// <summary>Raised when the user confirms; the view closes the dialog.</summary>
    public event EventHandler? Confirmed;

    /// <summary>Opens the dialog on a saved connection, as File &gt; Connect does for the current one.</summary>
    public Task SelectAsync(ConnectionProfile profile)
    {
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        return LoadSelectedAsync();
    }

    async partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (!_suppressLoad)
        {
            await LoadSelectedAsync();
        }
    }

    [RelayCommand]
    private void New()
    {
        _suppressLoad = true;
        SelectedProfile = null;
        _suppressLoad = false;
        SetEditor(new ConnectionEditorViewModel(_connector));
        SetStatus(null);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var profile = Editor.ToProfile();
            await _store.SaveAsync(profile, Editor.Secret);
            var index = Profiles.ToList().FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                Profiles[index] = profile;
            }
            else
            {
                Profiles.Add(profile);
            }

            _suppressLoad = true;
            SelectedProfile = profile;
            _suppressLoad = false;
            SetStatus($"Saved \"{profile.Name}\".");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save: {ErrorMessages.Describe(ex)}", isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var profile = SelectedProfile!;
        if (!await _dialogs.ConfirmAsync("Delete connection", $"Delete the saved connection \"{profile.Name}\"? This only removes it from this app.", "Delete"))
        {
            return;
        }

        await _store.DeleteAsync(profile.Id);
        Profiles.Remove(profile);
        New();
        SetStatus($"Deleted \"{profile.Name}\".");
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task OkAsync()
    {
        var profile = Editor.ToProfile();
        if (SaveOnConnect)
        {
            try
            {
                await _store.SaveAsync(profile, Editor.Secret);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not save: {ErrorMessages.Describe(ex)}", isError: true);
                return;
            }
        }

        Result = new ConnectRequest(profile, Editor.Secret, SaveOnConnect || Profiles.Any(p => p.Id == profile.Id));
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadSelectedAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        try
        {
            // Reading the secret can show a keychain prompt, so it only happens when a connection is picked.
            var secret = profile.Auth == AuthKind.ConnectionString ? await _store.GetSecretAsync(profile) : null;
            SetEditor(new ConnectionEditorViewModel(_connector, profile, secret));
            SetStatus(profile.Auth == AuthKind.ConnectionString && secret is null
                ? "No connection string is stored for this connection; enter it again."
                : null, isError: secret is null && profile.Auth == AuthKind.ConnectionString);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not read the saved connection string: {ErrorMessages.Describe(ex)}", isError: true);
        }
    }

    private void SetEditor(ConnectionEditorViewModel editor)
    {
        Editor.PropertyChanged -= OnEditorChanged;
        Editor = editor;
        Editor.PropertyChanged += OnEditorChanged;
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionEditorViewModel.IsValid) or nameof(ConnectionEditorViewModel.CanSave))
        {
            OkCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private void SetStatus(string? message, bool isError = false)
    {
        Status = message;
        HasError = isError;
    }

    private bool CanConnect() => Editor.IsValid;
    private bool CanSave() => Editor.CanSave;
    private bool HasSelection() => SelectedProfile is not null;
}
