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

    /// <summary>
    /// Entities added by name, shown when the credential can't list entities (no Manage claim),
    /// e.g. <c>orders</c> or <c>events/Subscriptions/billing</c>.
    /// </summary>
    public IReadOnlyList<string> PinnedEntities { get; init; } = [];

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
        && PinnedEntities.SequenceEqual(other.PinnedEntities);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Auth, FullyQualifiedNamespace, EntraCredential, TenantId, PinnedEntities.Count);
}
