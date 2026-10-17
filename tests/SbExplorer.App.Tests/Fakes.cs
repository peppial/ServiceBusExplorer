using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;

namespace SbExplorer.App.Tests;

internal sealed class FakeSecretStore(bool isSecure = true) : ISecretStore
{
    public Dictionary<string, string> Secrets { get; } = [];
    public bool IsSecure => isSecure;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.GetValueOrDefault(key));

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        Secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeConnectionStore(ISecretStore secrets) : IConnectionStore
{
    public List<ConnectionProfile> Profiles { get; } = [];

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConnectionProfile>>(Profiles.ToList());

    public async Task SaveAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        Profiles.Add(profile);
        if (secret is not null)
        {
            await secrets.SetAsync(profile.SecretKey, secret, cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Profiles.RemoveAll(p => p.Id == id);
        await secrets.DeleteAsync($"connection:{id:N}", cancellationToken);
    }

    public Task<string?> GetSecretAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        secrets.GetAsync(profile.SecretKey, cancellationToken);
}

internal sealed class FakeConnector : INamespaceConnector
{
    public Exception? Failure { get; set; }
    public List<FakeSession> Sessions { get; } = [];

    public Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<INamespaceSession>(Failure);
        }

        var session = new FakeSession(profile, profile.FullyQualifiedNamespace ?? "fake.servicebus.windows.net");
        Sessions.Add(session);
        return Task.FromResult<INamespaceSession>(session);
    }
}

internal sealed class FakeSession(ConnectionProfile profile, string host, FakeEntityService? entities = null, bool isEmulator = false) : INamespaceSession
{
    public bool IsEmulator { get; } = isEmulator;
    public ConnectionProfile Profile { get; } = profile;
    public string FullyQualifiedNamespace { get; } = host;
    public FakeEntityService FakeEntities { get; } = entities ?? new FakeEntityService();
    public IEntityService Entities => FakeEntities;
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    /// <summary>Fills in the editor and returns whether to "save".</summary>
    public Func<ConnectionEditorViewModel, bool> OnEditor { get; set; } = _ => false;
    public bool ConfirmResult { get; set; }
    public int ConfirmCount { get; private set; }

    public Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor) => Task.FromResult(OnEditor(editor));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }
}

internal sealed class FakeEntityService : IEntityService
{
    public List<EntityInfo> Queues { get; } = [];
    public List<EntityInfo> Topics { get; } = [];
    public Dictionary<string, List<EntityInfo>> Subscriptions { get; } = [];
    public Dictionary<string, List<EntityInfo>> Rules { get; } = [];
    public Exception? Failure { get; set; }
    public Exception? SubscriptionFailure { get; set; }
    public int SubscriptionLoads { get; private set; }
    public int RuleLoads { get; private set; }

    public static EntityInfo Queue(string name, long active = 0, long deadLetter = 0) => new()
    {
        Kind = EntityKind.Queue,
        Name = name,
        Path = name,
        Counts = new MessageCounts(active, deadLetter, 0, 0, 0),
        Properties = [new("Runtime", "Active", active.ToString())],
    };

    public static EntityInfo Topic(string name) => new() { Kind = EntityKind.Topic, Name = name, Path = name };

    public static EntityInfo Subscription(string topic, string name, long active = 0) => new()
    {
        Kind = EntityKind.Subscription,
        Name = name,
        Path = EntityInfo.SubscriptionPath(topic, name),
        Counts = new MessageCounts(active, 0, 0, 0, 0),
    };

    public static EntityInfo Rule(string topic, string subscription, string name) =>
        new() { Kind = EntityKind.Rule, Name = name, Path = EntityInfo.RulePath(topic, subscription, name) };

    public Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        Failure is null ? Task.FromResult<IReadOnlyList<EntityInfo>>(Queues.ToList()) : Task.FromException<IReadOnlyList<EntityInfo>>(Failure);

    public Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EntityInfo>>(Topics.ToList());

    public Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default)
    {
        SubscriptionLoads++;
        return SubscriptionFailure is null
            ? Task.FromResult<IReadOnlyList<EntityInfo>>(Subscriptions.GetValueOrDefault(topic)?.ToList() ?? [])
            : Task.FromException<IReadOnlyList<EntityInfo>>(SubscriptionFailure);
    }

    public Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default)
    {
        RuleLoads++;
        return Task.FromResult<IReadOnlyList<EntityInfo>>(
            Rules.GetValueOrDefault(EntityInfo.SubscriptionPath(topic, subscription))?.ToList() ?? []);
    }
}
