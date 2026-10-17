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

    IEntityService Entities { get; }
    IMessageService Messages { get; }
}

public interface INamespaceConnector
{
    /// <summary>Opens a session and verifies it by making one management call.</summary>
    /// <param name="secret">The connection string, for <see cref="AuthKind.ConnectionString"/> profiles.</param>
    Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default);
}
