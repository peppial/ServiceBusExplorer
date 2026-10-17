using CommunityToolkit.Mvvm.ComponentModel;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.ViewModels;

public partial class ConnectionItemViewModel(ConnectionProfile profile, string? host) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(AuthLabel))]
    private ConnectionProfile _profile = profile;

    [ObservableProperty]
    private string? _host = host;

    [ObservableProperty]
    private bool _isConnected;

    public string Name => Profile.Name;
    public string AuthLabel => Profile.Auth == AuthKind.Entra ? "Entra ID" : "Connection string";
}
