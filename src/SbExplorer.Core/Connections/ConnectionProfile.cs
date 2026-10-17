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

    /// <summary>
    /// The namespace host, e.g. <c>contoso.servicebus.windows.net</c>. Entra sign-in connects to it; for
    /// connection strings it is a copy of the endpoint host, kept so the list can be shown without reading
    /// secrets (which on macOS can prompt for the keychain password).
    /// </summary>
    public string? FullyQualifiedNamespace { get; init; }
    public EntraCredentialKind EntraCredential { get; init; } = EntraCredentialKind.InteractiveBrowser;
    public string? TenantId { get; init; }

    /// <summary>
    /// Entities added by name, shown when the credential can't list entities (no Manage claim),
    /// e.g. <c>orders</c> or <c>events/Subscriptions/billing</c>.
    /// </summary>
    public IReadOnlyList<string> PinnedEntities { get; init; } = [];

    /// <summary>
    /// Browse and peek only: every operation that sends, receives, settles, deletes or changes entities is refused,
    /// both in the UI and by the services. Meant for production namespaces.
    /// </summary>
    public bool ReadOnly { get; init; }

    public string SecretKey => $"connection:{Id:N}";

    // Records compare lists by reference; compare pinned entities by content so a reloaded profile equals the saved one.
    public bool Equals(ConnectionProfile? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Auth == other.Auth
        && FullyQualifiedNamespace == other.FullyQualifiedNamespace
        && EntraCredential == other.EntraCredential
        && TenantId == other.TenantId
        && ReadOnly == other.ReadOnly
        && PinnedEntities.SequenceEqual(other.PinnedEntities);

    public override int GetHashCode() =>
        HashCode.Combine(Id, Name, Auth, FullyQualifiedNamespace, EntraCredential, TenantId, ReadOnly, PinnedEntities.Count);
}
