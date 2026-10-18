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
    public int Reads { get; private set; }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Secrets.GetValueOrDefault(key));
    }

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

    /// <summary>Makes loading take a while, so overlapping loads can be tested.</summary>
    public TimeSpan LoadDelay { get; set; }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (LoadDelay > TimeSpan.Zero)
        {
            await Task.Delay(LoadDelay, cancellationToken);
        }

        return Profiles.ToList();
    }

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
    public bool CanManage { get; set; } = true;
    public string? ScopedEntity { get; set; }
    public List<FakeSession> Sessions { get; } = [];

    public List<(ConnectionProfile Profile, string? Secret)> Requests { get; } = [];

    public Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        Requests.Add((profile, secret));
        if (Failure is not null)
        {
            return Task.FromException<INamespaceSession>(Failure);
        }

        var session = new FakeSession(profile, profile.FullyQualifiedNamespace ?? "fake.servicebus.windows.net")
        {
            CanManage = CanManage,
            ScopedEntity = ScopedEntity,
        };
        Sessions.Add(session);
        return Task.FromResult<INamespaceSession>(session);
    }
}

internal sealed class FakeSession(ConnectionProfile profile, string host, FakeEntityService? entities = null, bool isEmulator = false) : INamespaceSession
{
    public bool IsEmulator { get; } = isEmulator;
    public bool CanManage { get; init; } = true;
    public string? ScopedEntity { get; init; }
    public string? LimitedReason { get; init; }
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
    /// <summary>Drives the connect dialog and returns whether the user chose OK.</summary>
    public Func<ConnectDialogViewModel, Task<bool>> OnConnectDialog { get; set; } = _ => Task.FromResult(false);

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

    public Task<bool> ShowConnectDialogAsync(ConnectDialogViewModel dialog) => OnConnectDialog(dialog);

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

    /// <summary>Fills in the Retrieve messages dialog and returns whether to proceed.</summary>
    public Func<ReceiveOptionsViewModel, bool> OnReceiveOptions { get; set; } = _ => true;
    public string? OpenFileText { get; set; }
    public int? ChooseResult { get; set; }

    public Task<bool> ShowReceiveOptionsAsync(ReceiveOptionsViewModel options) => Task.FromResult(OnReceiveOptions(options));

    public Task<string?> OpenTextFileAsync(string title) => Task.FromResult(OpenFileText);

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> options) => Task.FromResult(ChooseResult);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }

    public List<string> DeviceCodes { get; } = [];
    public int DeviceCodeCloses { get; private set; }

    public Task ShowDeviceCodeAsync(string message, string code, Uri verificationUri)
    {
        DeviceCodes.Add(code);
        return Task.CompletedTask;
    }

    public void CloseDeviceCode() => DeviceCodeCloses++;
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

    // ---- definitions: stored by path; creating one also adds it to the tree lists ----

    public Dictionary<string, object> Definitions { get; } = [];
    public List<string> Calls { get; } = [];
    public Exception? WriteFailure { get; set; }

    /// <summary>Entities a scoped key may read even though it can't list the namespace.</summary>
    public Dictionary<string, EntityInfo> Readable { get; } = [];

    public Task<EntityInfo> GetEntityAsync(EntityInfo reference, CancellationToken cancellationToken = default) =>
        Readable.TryGetValue(reference.Path, out var entity) && entity.Kind == reference.Kind
            ? Task.FromResult(entity)
            : Task.FromException<EntityInfo>(new UnauthorizedAccessException("Unauthorized"));

    public Task<QueueDefinition> GetQueueAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult((QueueDefinition)Definitions[name]);

    public Task<TopicDefinition> GetTopicAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult((TopicDefinition)Definitions[name]);

    public Task<SubscriptionDefinition> GetSubscriptionAsync(string topic, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult((SubscriptionDefinition)Definitions[EntityInfo.SubscriptionPath(topic, name)]);

    public Task<RuleDefinition> GetRuleAsync(string topic, string subscription, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult((RuleDefinition)Definitions[EntityInfo.RulePath(topic, subscription, name)]);

    public Task CreateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) => Write($"create queue {queue.Name}", () =>
    {
        Definitions[queue.Name] = queue;
        Queues.Add(Queue(queue.Name) with { RequiresSession = queue.RequiresSession });
    });

    public Task CreateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) => Write($"create topic {topic.Name}", () =>
    {
        Definitions[topic.Name] = topic;
        Topics.Add(Topic(topic.Name));
    });

    public Task CreateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        Write($"create subscription {subscription.Topic}/{subscription.Name} rules={subscription.Rules.Count}", () =>
        {
            var path = EntityInfo.SubscriptionPath(subscription.Topic, subscription.Name);
            Definitions[path] = subscription;
            (Subscriptions.TryGetValue(subscription.Topic, out var list) ? list : Subscriptions[subscription.Topic] = [])
                .Add(Subscription(subscription.Topic, subscription.Name));
            var rule = subscription.Rules.FirstOrDefault() ?? new RuleDefinition { Name = RuleDefinition.DefaultName, FilterKind = RuleFilterKind.True };
            AddRule(subscription.Topic, subscription.Name, rule);
        });

    public Task CreateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) =>
        Write($"create rule {topic}/{subscription}/{rule.Name}", () => AddRule(topic, subscription, rule));

    public Task UpdateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) =>
        Write($"update queue {queue.Name}", () => Definitions[queue.Name] = queue);

    public Task UpdateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) =>
        Write($"update topic {topic.Name}", () => Definitions[topic.Name] = topic);

    public Task UpdateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        Write($"update subscription {subscription.Topic}/{subscription.Name}",
            () => Definitions[EntityInfo.SubscriptionPath(subscription.Topic, subscription.Name)] = subscription);

    public Task UpdateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) =>
        Write($"update rule {topic}/{subscription}/{rule.Name}", () => Definitions[EntityInfo.RulePath(topic, subscription, rule.Name)] = rule);

    public Task DeleteAsync(EntityInfo entity, CancellationToken cancellationToken = default) => Write($"delete {entity.Path}", () =>
    {
        Definitions.Remove(entity.Path);
        Queues.RemoveAll(q => q.Path == entity.Path);
        Topics.RemoveAll(t => t.Path == entity.Path);
        foreach (var list in Subscriptions.Values)
        {
            list.RemoveAll(s => s.Path == entity.Path);
        }
    });

    public Task SetStatusAsync(EntityInfo entity, EntityStatusValue status, CancellationToken cancellationToken = default) =>
        Write($"status {entity.Path} {status}", () =>
        {
            var index = Queues.FindIndex(q => q.Path == entity.Path);
            if (index >= 0)
            {
                Queues[index] = Queues[index] with { Status = status.ToString() };
            }
        });

    private void AddRule(string topic, string subscription, RuleDefinition rule)
    {
        var key = EntityInfo.SubscriptionPath(topic, subscription);
        Definitions[EntityInfo.RulePath(topic, subscription, rule.Name)] = rule;
        (Rules.TryGetValue(key, out var list) ? list : Rules[key] = []).Add(Rule(topic, subscription, rule.Name));
    }

    private Task Write(string call, Action apply)
    {
        Calls.Add(call);
        if (WriteFailure is not null)
        {
            return Task.FromException(WriteFailure);
        }

        apply();
        return Task.CompletedTask;
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

        return Task.FromResult<IReadOnlyList<MessageInfo>>(InSource(source)
            .Where(m => m.SequenceNumber >= (fromSequenceNumber ?? 0))
            .Take(maxMessages)
            .ToList());
    }

    public Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        var taken = InSource(source).Take(maxMessages).ToList();
        Messages(Key(source)).RemoveAll(taken.Contains);
        return Task.FromResult<IReadOnlyList<MessageInfo>>(taken);
    }

    public Task<IReceivedBatch> ReceivePeekLockAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        var locked = InSource(source).Take(maxMessages)
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

    public List<(string Entity, long[] Sequences)> Cancelled { get; } = [];
    public List<(string Target, long[] Sequences)> Resubmitted { get; } = [];

    /// <summary>Sequence numbers delete/resubmit pretend were already gone.</summary>
    public HashSet<long> Missing { get; } = [];

    public Task<IReceivedBatch> ReceiveDeferredAsync(MessageSource source, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        var locked = Messages(Key(source)).Where(m => sequenceNumbers.Contains(m.SequenceNumber))
            .Select(m => m with { LockToken = Guid.NewGuid().ToString(), LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1) })
            .ToList();
        var batch = new FakeBatch(locked);
        Batches.Add(batch);
        return Task.FromResult<IReceivedBatch>(batch);
    }

    public Task CancelScheduledAsync(string entityName, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        Cancelled.Add((entityName, sequenceNumbers.ToArray()));
        return Task.CompletedTask;
    }

    public Task<long> PurgeAsync(
        MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default, DateTimeOffset? enqueuedBefore = null)
    {
        var doomed = InSource(source).ToList();
        Messages(Key(source)).RemoveAll(doomed.Contains);
        progress?.Report(doomed.Count);
        return Task.FromResult((long)doomed.Count);
    }

    public int PurgeAllSessionsCalls { get; private set; }
    public Dictionary<string, byte[]> SessionStates { get; } = [];

    public Task<long> PurgeAllSessionsAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        PurgeAllSessionsCalls++;
        return PurgeAsync(source, progress, cancellationToken, null);
    }

    public Task<ReadOnlyMemory<byte>?> GetSessionStateAsync(MessageSource source, CancellationToken cancellationToken = default) =>
        Task.FromResult(SessionStates.TryGetValue(source.SessionId!, out var state) ? new ReadOnlyMemory<byte>(state) : (ReadOnlyMemory<byte>?)null);

    public Task SetSessionStateAsync(MessageSource source, ReadOnlyMemory<byte>? state, CancellationToken cancellationToken = default)
    {
        if (state is { } bytes)
        {
            SessionStates[source.SessionId!] = bytes.ToArray();
        }
        else
        {
            SessionStates.Remove(source.SessionId!);
        }

        return Task.CompletedTask;
    }

    private IEnumerable<MessageInfo> InSource(MessageSource source) =>
        Messages(Key(source)).Where(m => source.SessionId is null || m.SessionId == source.SessionId);

    public Task<IReadOnlyList<long>> DeleteAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        var done = sequenceNumbers.Where(s => !Missing.Contains(s)).ToList();
        Messages(Key(source)).RemoveAll(m => done.Contains(m.SequenceNumber));
        return Task.FromResult<IReadOnlyList<long>>(done);
    }

    public Task<IReadOnlyList<long>> ResubmitAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, string targetEntity, CancellationToken cancellationToken = default)
    {
        var done = sequenceNumbers.Where(s => !Missing.Contains(s)).ToList();
        Messages(Key(source)).RemoveAll(m => done.Contains(m.SequenceNumber));
        Resubmitted.Add((targetEntity, done.ToArray()));
        return Task.FromResult<IReadOnlyList<long>>(done);
    }

    public static string Key(MessageSource source) => source.SubQueue switch
    {
        SubQueueKind.DeadLetter => $"{source.EntityPath}/$DeadLetterQueue",
        SubQueueKind.TransferDeadLetter => $"{source.EntityPath}/$Transfer/$DeadLetterQueue",
        _ => source.EntityPath,
    };
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
