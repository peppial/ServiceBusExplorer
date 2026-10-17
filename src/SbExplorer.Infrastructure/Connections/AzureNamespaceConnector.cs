using Azure;
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

            return session;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            // A Listen- or Send-only key, a key scoped to one entity, or an Entra data role can't list the
            // namespace. Keep the session, verified against the scoped entity when there is one.
            try
            {
                await VerifyDataAccessAsync(session, cancellationToken);
                return session.WithoutManagement(Describe(ex));
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The management API answers 401 for both a wrong key and a key without the Manage claim, so when the
    /// connection string names an entity, peek it (Listen) or open a sender (Send) to tell them apart.
    /// </summary>
    private static async Task VerifyDataAccessAsync(AzureNamespaceSession session, CancellationToken cancellationToken)
    {
        if (session.ScopedEntity is not { } entity)
        {
            return;
        }

        try
        {
            await using var receiver = session.Client.CreateReceiver(entity);
            await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound })
        {
            // Not readable (Send-only key, or a topic); a sender link proves the Send claim instead.
            await using var sender = session.Client.CreateSender(entity);
            using var _ = await sender.CreateMessageBatchAsync(cancellationToken);
        }
    }

    // Service Bus's answer, without the tracking ids and stack it is usually followed by.
    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        {
            inner = aggregate.InnerExceptions[0];
        }

        var message = inner.Message.Split('\n', 2)[0].Trim();
        var tracking = message.IndexOf("TrackingId", StringComparison.OrdinalIgnoreCase);
        return (tracking > 0 ? message[..tracking] : message).Trim().TrimEnd('.', ',') + ".";
    }

    internal static bool IsAccessDenied(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        RequestFailedException { Status: 401 or 403 } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsAccessDenied),
        _ => ex.InnerException is not null && IsAccessDenied(ex.InnerException),
    };

    private static AzureNamespaceSession CreateFromConnectionString(ConnectionProfile profile, string? secret)
    {
        if (!ConnectionStringInfo.TryParse(secret, out var info, out var error))
        {
            throw new ArgumentException(error, nameof(secret));
        }

        // EntityPath only says which entity the string was copied from; clients built with it can't reach any
        // other entity even when the key could, so it's kept as ScopedEntity and left out of the clients.
        var unscoped = ConnectionStringInfo.WithoutEntityPath(secret!);
        return new AzureNamespaceSession(
            profile,
            info!.FullyQualifiedNamespace,
            info.UseDevelopmentEmulator,
            info.EntityPath,
            new ServiceBusClient(unscoped),
            new ServiceBusAdministrationClient(ConnectionStringInfo.ForAdministration(unscoped)));
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
            ScopedEntity: null,
            new ServiceBusClient(host, credential),
            new ServiceBusAdministrationClient(host, credential));
    }
}

public sealed class AzureNamespaceSession(
    ConnectionProfile profile,
    string fullyQualifiedNamespace,
    bool IsEmulator,
    string? ScopedEntity,
    ServiceBusClient client,
    ServiceBusAdministrationClient administration,
    bool CanManage = true,
    string? LimitedReason = null) : INamespaceSession
{
    public ConnectionProfile Profile { get; } = profile;
    public string FullyQualifiedNamespace { get; } = fullyQualifiedNamespace;
    public bool IsEmulator { get; } = IsEmulator;
    public bool CanManage { get; } = CanManage;
    public string? ScopedEntity { get; } = ScopedEntity;
    public string? LimitedReason { get; } = LimitedReason;
    public ServiceBusClient Client { get; } = client;
    public ServiceBusAdministrationClient Administration { get; } = administration;
    // A read-only profile gets services that refuse every change, whatever the UI allows.
    public IEntityService Entities { get; } = profile.ReadOnly
        ? new ReadOnlyEntityService(new AzureEntityService(administration))
        : new AzureEntityService(administration);

    public IMessageService Messages { get; } = profile.ReadOnly
        ? new ReadOnlyMessageService(new AzureMessageService(client))
        : new AzureMessageService(client);

    public AzureNamespaceSession WithoutManagement(string reason) =>
        new(Profile, FullyQualifiedNamespace, IsEmulator, ScopedEntity, Client, Administration, CanManage: false, LimitedReason: reason);

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
