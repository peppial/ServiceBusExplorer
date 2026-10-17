using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using SbExplorer.Infrastructure.Management;
using SbExplorer.Infrastructure.Messaging;

namespace SbExplorer.Infrastructure.Connections;

public sealed class AzureNamespaceConnector : INamespaceConnector
{
    public async Task<INamespaceSession> ConnectAsync(
        ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        var session = profile.Auth switch
        {
            AuthKind.ConnectionString => CreateFromConnectionString(profile, secret),
            AuthKind.Entra => CreateFromEntra(profile),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.Auth, "Unknown auth kind."),
        };

        try
        {
            // Listing one queue proves both reachability and management rights.
            await foreach (var _ in session.Administration.GetQueuesAsync(cancellationToken).AsPages(pageSizeHint: 1))
            {
                break;
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }

        return session;
    }

    private static AzureNamespaceSession CreateFromConnectionString(ConnectionProfile profile, string? secret)
    {
        if (!ConnectionStringInfo.TryParse(secret, out var info, out var error))
        {
            throw new ArgumentException(error, nameof(secret));
        }

        return new AzureNamespaceSession(
            profile,
            info!.FullyQualifiedNamespace,
            info.UseDevelopmentEmulator,
            new ServiceBusClient(secret),
            new ServiceBusAdministrationClient(ConnectionStringInfo.ForAdministration(secret!)));
    }

    private static AzureNamespaceSession CreateFromEntra(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.FullyQualifiedNamespace))
        {
            throw new ArgumentException("A namespace host name is required for Microsoft Entra sign-in.", nameof(profile));
        }

        var host = profile.FullyQualifiedNamespace.Trim();
        var credential = CredentialFactory.Create(profile);
        return new AzureNamespaceSession(
            profile,
            host,
            IsEmulator: false,
            new ServiceBusClient(host, credential),
            new ServiceBusAdministrationClient(host, credential));
    }
}

public sealed class AzureNamespaceSession(
    ConnectionProfile profile,
    string fullyQualifiedNamespace,
    bool IsEmulator,
    ServiceBusClient client,
    ServiceBusAdministrationClient administration) : INamespaceSession
{
    public ConnectionProfile Profile { get; } = profile;
    public string FullyQualifiedNamespace { get; } = fullyQualifiedNamespace;
    public bool IsEmulator { get; } = IsEmulator;
    public ServiceBusClient Client { get; } = client;
    public ServiceBusAdministrationClient Administration { get; } = administration;
    public IEntityService Entities { get; } = new AzureEntityService(administration);
    public IMessageService Messages { get; } = new AzureMessageService(client);

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
