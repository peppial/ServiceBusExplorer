namespace SbExplorer.Core.Connections;

/// <summary>An open connection to one namespace. Later steps hang entity and message services off this.</summary>
public interface INamespaceSession : IAsyncDisposable
{
    ConnectionProfile Profile { get; }
    string FullyQualifiedNamespace { get; }
}

public interface INamespaceConnector
{
    /// <summary>Opens a session and verifies it by making one management call.</summary>
    /// <param name="secret">The connection string, for <see cref="AuthKind.ConnectionString"/> profiles.</param>
    Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default);
}
