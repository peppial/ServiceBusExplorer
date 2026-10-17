using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Connections;

/// <summary>Thrown when a read-only connection is asked to change something.</summary>
public sealed class ReadOnlyConnectionException(string operation)
    : InvalidOperationException($"This connection is read-only, so {operation} is not allowed. Edit the connection to turn read-only off.");

/// <summary>
/// Message operations for a read-only connection: peeking works, everything that locks, removes or sends is
/// refused before it reaches Service Bus. Reading session state is allowed; it locks the session only briefly.
/// </summary>
public sealed class ReadOnlyMessageService(IMessageService inner) : IMessageService
{
    public Task<IReadOnlyList<MessageInfo>> PeekAsync(
        MessageSource source, int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default) =>
        inner.PeekAsync(source, maxMessages, fromSequenceNumber, cancellationToken);

    public Task<ReadOnlyMemory<byte>?> GetSessionStateAsync(MessageSource source, CancellationToken cancellationToken = default) =>
        inner.GetSessionStateAsync(source, cancellationToken);

    public Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("receiving and deleting messages");

    public Task<IReceivedBatch> ReceivePeekLockAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("receiving (locking) messages");

    public Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("sending messages");

    public Task<IReceivedBatch> ReceiveDeferredAsync(MessageSource source, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("receiving deferred messages");

    public Task CancelScheduledAsync(string entityName, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("cancelling scheduled messages");

    public Task<long> PurgeAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default, DateTimeOffset? enqueuedBefore = null) =>
        throw new ReadOnlyConnectionException("purging");

    public Task<long> PurgeAllSessionsAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("purging");

    public Task SetSessionStateAsync(MessageSource source, ReadOnlyMemory<byte>? state, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("changing session state");

    public Task<IReadOnlyList<long>> DeleteAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("deleting messages");

    public Task<IReadOnlyList<long>> ResubmitAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, string targetEntity, CancellationToken cancellationToken = default) =>
        throw new ReadOnlyConnectionException("resubmitting messages");
}

/// <summary>Entity operations for a read-only connection: listing and reading work, changes are refused.</summary>
public sealed class ReadOnlyEntityService(IEntityService inner) : IEntityService
{
    public Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default) => inner.GetQueuesAsync(cancellationToken);
    public Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default) => inner.GetTopicsAsync(cancellationToken);

    public Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default) =>
        inner.GetSubscriptionsAsync(topic, cancellationToken);

    public Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default) =>
        inner.GetRulesAsync(topic, subscription, cancellationToken);

    public Task<EntityInfo> GetEntityAsync(EntityInfo reference, CancellationToken cancellationToken = default) =>
        inner.GetEntityAsync(reference, cancellationToken);

    public Task<QueueDefinition> GetQueueAsync(string name, CancellationToken cancellationToken = default) => inner.GetQueueAsync(name, cancellationToken);
    public Task<TopicDefinition> GetTopicAsync(string name, CancellationToken cancellationToken = default) => inner.GetTopicAsync(name, cancellationToken);

    public Task<SubscriptionDefinition> GetSubscriptionAsync(string topic, string name, CancellationToken cancellationToken = default) =>
        inner.GetSubscriptionAsync(topic, name, cancellationToken);

    public Task<RuleDefinition> GetRuleAsync(string topic, string subscription, string name, CancellationToken cancellationToken = default) =>
        inner.GetRuleAsync(topic, subscription, name, cancellationToken);

    public Task CreateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) => Refuse("creating queues");
    public Task CreateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) => Refuse("creating topics");
    public Task CreateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => Refuse("creating subscriptions");
    public Task CreateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) => Refuse("creating rules");
    public Task UpdateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) => Refuse("changing queues");
    public Task UpdateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) => Refuse("changing topics");
    public Task UpdateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => Refuse("changing subscriptions");
    public Task UpdateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) => Refuse("changing rules");
    public Task DeleteAsync(EntityInfo entity, CancellationToken cancellationToken = default) => Refuse("deleting entities");
    public Task SetStatusAsync(EntityInfo entity, EntityStatusValue status, CancellationToken cancellationToken = default) => Refuse("changing status");

    private static Task Refuse(string operation) => throw new ReadOnlyConnectionException(operation);
}
