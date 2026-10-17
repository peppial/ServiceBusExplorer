using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Connections;

/// <summary>An open connection to one namespace.</summary>
public interface INamespaceSession : IAsyncDisposable
{
    ConnectionProfile Profile { get; }
    string FullyQualifiedNamespace { get; }

    /// <summary>The local Service Bus emulator, which reports zero for most runtime message counts.</summary>
    bool IsEmulator { get; }

    /// <summary>
    /// False when the credential was refused by the management API (no Manage claim, or only a data role
    /// under Entra). Entities can't be listed then, but messages can still be sent or received by name.
    /// </summary>
    bool CanManage { get; }

    /// <summary>The entity a connection string is scoped to (its EntityPath), if any.</summary>
    string? ScopedEntity { get; }

    /// <summary>Why management access was refused, from Service Bus's answer; null when it wasn't.</summary>
    string? LimitedReason { get; }

    IEntityService Entities { get; }
    IMessageService Messages { get; }
}

public interface INamespaceConnector
{
    /// <summary>Opens a session and verifies it by making one management call.</summary>
    /// <param name="secret">The connection string, for <see cref="AuthKind.ConnectionString"/> profiles.</param>
    Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default);
}
