namespace SbExplorer.Core.Connections;

public enum AuthKind
{
    ConnectionString,
    Entra,
}

public enum EntraCredentialKind
{
    InteractiveBrowser,
    DeviceCode,
    AzureCli,
    Default,
}

/// <summary>
/// A saved namespace connection. Secrets (connection strings) are never stored on the profile;
/// they live in an <see cref="ISecretStore"/> keyed by <see cref="SecretKey"/>.
/// </summary>
public sealed record ConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public AuthKind Auth { get; init; }

    /// <summary>For Entra auth, e.g. <c>contoso.servicebus.windows.net</c>.</summary>
    public string? FullyQualifiedNamespace { get; init; }
    public EntraCredentialKind EntraCredential { get; init; } = EntraCredentialKind.InteractiveBrowser;
    public string? TenantId { get; init; }

    public string SecretKey => $"connection:{Id:N}";
}
