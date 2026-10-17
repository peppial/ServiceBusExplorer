using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

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
    public FakeMessageService FakeMessages { get; } = new();
    public IMessageService Messages => FakeMessages;
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

    /// <summary>Receives the send dialog; by default sends whatever it was prefilled with.</summary>
    public Func<SendMessageViewModel, Task<bool>> OnSend { get; set; } = async sender =>
    {
        await sender.SendCommand.ExecuteAsync(null);
        return sender.SendError is null;
    };

    public bool ConfirmResult { get; set; }
    public int ConfirmCount { get; private set; }
    public string? PromptResult { get; set; }
    public string? CopiedText { get; private set; }
    public List<(string Name, byte[] Content)> SavedFiles { get; } = [];
    public SendMessageViewModel? LastSendDialog { get; private set; }

    public Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor) => Task.FromResult(OnEditor(editor));

    public Task<bool> ShowSendMessageAsync(SendMessageViewModel sender)
    {
        LastSendDialog = sender;
        return OnSend(sender);
    }

    public Task<string?> PromptAsync(string title, string label, string initialValue) => Task.FromResult(PromptResult);

    public Task<bool> SaveFileAsync(string suggestedName, ReadOnlyMemory<byte> content)
    {
        SavedFiles.Add((suggestedName, content.ToArray()));
        return Task.FromResult(true);
    }

    public Task CopyTextAsync(string text)
    {
        CopiedText = text;
        return Task.CompletedTask;
    }

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

/// <summary>An in-memory entity: messages keyed by source path, with sequence numbers assigned on send.</summary>
internal sealed class FakeMessageService : IMessageService
{
    private long _nextSequence = 1;

    public Dictionary<string, List<MessageInfo>> Entities { get; } = [];
    public List<(string Entity, OutgoingMessage Message)> Sent { get; } = [];
    public List<FakeBatch> Batches { get; } = [];
    public Exception? Failure { get; set; }

    public void Seed(string path, int count, Func<int, MessageInfo>? create = null)
    {
        var list = Messages(path);
        for (var i = 0; i < count; i++)
        {
            var message = create?.Invoke(i) ?? new MessageInfo { MessageId = $"m{i}", Body = "{\"n\":1}"u8.ToArray(), ContentType = "application/json" };
            list.Add(message with { SequenceNumber = _nextSequence++ });
        }
    }

    public List<MessageInfo> Messages(string path) =>
        Entities.TryGetValue(path, out var list) ? list : Entities[path] = [];

    public Task<IReadOnlyList<MessageInfo>> PeekAsync(MessageSource source, int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<MessageInfo>>(Failure);
        }

        return Task.FromResult<IReadOnlyList<MessageInfo>>(Messages(Key(source))
            .Where(m => m.SequenceNumber >= (fromSequenceNumber ?? 0))
            .Take(maxMessages)
            .ToList());
    }

    public Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        var list = Messages(Key(source));
        var taken = list.Take(maxMessages).ToList();
        list.RemoveRange(0, taken.Count);
        return Task.FromResult<IReadOnlyList<MessageInfo>>(taken);
    }

    public Task<IReceivedBatch> ReceivePeekLockAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        var locked = Messages(Key(source)).Take(maxMessages)
            .Select(m => m with { LockToken = Guid.NewGuid().ToString(), LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1) })
            .ToList();
        var batch = new FakeBatch(locked);
        Batches.Add(batch);
        return Task.FromResult<IReceivedBatch>(batch);
    }

    public Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Sent.AddRange(messages.Select(m => (entityName, m)));
        return Task.CompletedTask;
    }

    private static string Key(MessageSource source) =>
        source.SubQueue == SubQueueKind.None ? source.EntityPath : $"{source.EntityPath}/$DeadLetterQueue";
}

internal sealed class FakeBatch(IReadOnlyList<MessageInfo> messages) : IReceivedBatch
{
    public IReadOnlyList<MessageInfo> Messages { get; } = messages;
    public List<(string Action, long Sequence, string? Reason)> Settled { get; } = [];
    public bool Disposed { get; private set; }

    public Task CompleteAsync(MessageInfo message, CancellationToken cancellationToken = default) => Record("Complete", message);
    public Task AbandonAsync(MessageInfo message, CancellationToken cancellationToken = default) => Record("Abandon", message);
    public Task DeferAsync(MessageInfo message, CancellationToken cancellationToken = default) => Record("Defer", message);

    public Task DeadLetterAsync(MessageInfo message, string reason, string? description, CancellationToken cancellationToken = default) =>
        Record("DeadLetter", message, reason);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private Task Record(string action, MessageInfo message, string? reason = null)
    {
        Settled.Add((action, message.SequenceNumber, reason));
        return Task.CompletedTask;
    }
}
