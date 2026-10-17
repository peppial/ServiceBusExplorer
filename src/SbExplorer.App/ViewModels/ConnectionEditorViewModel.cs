using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.ViewModels;

public sealed record CredentialOption(EntraCredentialKind Kind, string Label);

public partial class ConnectionEditorViewModel : ObservableObject
{
    public const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private readonly INamespaceConnector _connector;
    private readonly Guid _id;
    private readonly IReadOnlyList<string> _pinnedEntities;

    public ConnectionEditorViewModel(INamespaceConnector connector, ConnectionProfile? existing = null, string? existingSecret = null)
    {
        _connector = connector;
        _id = existing?.Id ?? Guid.NewGuid();
        _pinnedEntities = existing?.PinnedEntities ?? [];
        IsNew = existing is null;

        if (existing is not null)
        {
            _name = existing.Name;
            _useEntra = existing.Auth == AuthKind.Entra;
            _connectionString = existingSecret ?? string.Empty;
            _namespace = existing.FullyQualifiedNamespace ?? string.Empty;
            _tenantId = existing.TenantId ?? string.Empty;
            _readOnly = existing.ReadOnly;
            _selectedCredential = CredentialOptions.First(o => o.Kind == existing.EntraCredential);
        }
        else
        {
            _selectedCredential = CredentialOptions[0];
        }
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "New connection" : "Edit connection";

    public IReadOnlyList<CredentialOption> CredentialOptions { get; } =
    [
        new(EntraCredentialKind.InteractiveBrowser, "Interactive browser sign-in"),
        new(EntraCredentialKind.DeviceCode, "Device code"),
        new(EntraCredentialKind.AzureCli, "Azure CLI (az login)"),
        new(EntraCredentialKind.Default, "DefaultAzureCredential chain"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid), nameof(CanSave), nameof(UseConnectionString), nameof(AuthIndex))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _useEntra;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private string _namespace = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    /// <summary>Browse and peek only; recommended for production namespaces.</summary>
    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private CredentialOption _selectedCredential;

    [ObservableProperty]
    private bool _revealConnectionString;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _testSucceeded;

    public bool UseConnectionString
    {
        get => !UseEntra;
        set => UseEntra = !value;
    }

    public IReadOnlyList<string> AuthOptions { get; } = ["Shared Access Signature", "Microsoft Entra ID"];

    /// <summary>The Authentication dropdown: 0 = connection string (SAS), 1 = Entra.</summary>
    public int AuthIndex
    {
        get => UseEntra ? 1 : 0;
        set => UseEntra = value == 1;
    }

    /// <summary>Connecting only needs valid settings; saving also needs a name.</summary>
    public bool CanSave => IsValid && !string.IsNullOrWhiteSpace(Name);

    public string? ValidationError
    {
        get
        {
            if (UseEntra)
            {
                return string.IsNullOrWhiteSpace(Namespace) ? "Enter the namespace host name." : null;
            }

            return ConnectionStringInfo.TryParse(ConnectionString, out _, out var error) ? null : error;
        }
    }

    public bool IsValid => ValidationError is null;

    public ConnectionProfile ToProfile() => new()
    {
        Id = _id,
        Name = string.IsNullOrWhiteSpace(Name) ? DisplayHost ?? "Service Bus namespace" : Name.Trim(),
        Auth = UseEntra ? AuthKind.Entra : AuthKind.ConnectionString,
        FullyQualifiedNamespace = UseEntra
            ? NormalizeHost(Namespace)
            : ConnectionStringInfo.TryParse(ConnectionString, out var info, out _) ? info!.FullyQualifiedNamespace : null,
        EntraCredential = SelectedCredential.Kind,
        TenantId = UseEntra && !string.IsNullOrWhiteSpace(TenantId) ? TenantId.Trim() : null,
        PinnedEntities = _pinnedEntities,
        ReadOnly = ReadOnly,
    };

    public string? Secret => UseEntra ? null : ConnectionString.Trim();

    private string? DisplayHost => UseEntra
        ? string.IsNullOrWhiteSpace(Namespace) ? null : NormalizeHost(Namespace)
        : ConnectionStringInfo.TryParse(ConnectionString, out var info, out _) ? info!.FullyQualifiedNamespace : null;

    [RelayCommand]
    private void UseEmulator()
    {
        UseEntra = false;
        ConnectionString = EmulatorConnectionString;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = "Local emulator";
        }
    }


    [RelayCommand(CanExecute = nameof(IsValid), IncludeCancelCommand = true)]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        TestResult = "Connecting…";
        TestSucceeded = false;
        try
        {
            await using var session = await _connector.ConnectAsync(ToProfile(), Secret, cancellationToken);
            TestSucceeded = true;
            TestResult = session.CanManage
                ? $"Connected to {session.FullyQualifiedNamespace}."
                : $"Connected to {session.FullyQualifiedNamespace}, but without management access: entities must be added by name.";
        }
        catch (OperationCanceledException)
        {
            TestResult = "Cancelled.";
        }
        catch (Exception ex)
        {
            TestResult = ErrorMessages.Describe(ex);
        }
    }

    // Accept a pasted portal URL or bare namespace name as well as the host name.
    private static string NormalizeHost(string value)
    {
        var host = value.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
        }

        return host.Contains('.') ? host : $"{host}.servicebus.windows.net";
    }
}
