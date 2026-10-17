namespace SbExplorer.Core.Messaging;

public interface IMessageService
{
    /// <summary>Reads messages without locking or removing them.</summary>
    Task<IReadOnlyList<MessageInfo>> PeekAsync(
        MessageSource source, int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="maxMessages"/>; stops early once the entity is drained.</summary>
    Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(
        MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default);

    /// <summary>Locks up to <paramref name="maxMessages"/>; settle them through the returned batch.</summary>
    Task<IReceivedBatch> ReceivePeekLockAsync(
        MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default);

    /// <summary>Sends to a queue or topic, batching as needed. Messages with a ScheduledEnqueueTime are scheduled.</summary>
    Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Locks deferred messages by sequence number; settle them through the returned batch.</summary>
    Task<IReceivedBatch> ReceiveDeferredAsync(
        MessageSource source, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default);

    /// <summary>Cancels scheduled messages on a queue or topic.</summary>
    Task CancelScheduledAsync(string entityName, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default);

    /// <summary>Deletes every message in the source (or in its one session); reports the running total.</summary>
    Task<long> PurgeAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes every message in every session of a session-enabled entity, one session at a time.</summary>
    Task<long> PurgeAllSessionsAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a session's state; null when none is set. Briefly locks the session.</summary>
    Task<ReadOnlyMemory<byte>?> GetSessionStateAsync(MessageSource source, CancellationToken cancellationToken = default);

    /// <summary>Replaces a session's state; null clears it. Briefly locks the session.</summary>
    Task SetSessionStateAsync(MessageSource source, ReadOnlyMemory<byte>? state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes specific messages. Service Bus can't receive an arbitrary message by sequence number, so this
    /// locks messages until it finds the requested ones and abandons the rest.
    /// </summary>
    Task<IReadOnlyList<long>> DeleteAsync(
        MessageSource source, IReadOnlyCollection<long> sequenceNumbers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends copies of the given messages to <paramref name="targetEntity"/> and removes the originals,
    /// usually to replay dead-lettered messages. A copy is only removed after it was sent.
    /// </summary>
    Task<IReadOnlyList<long>> ResubmitAsync(
        MessageSource source,
        IReadOnlyCollection<long> sequenceNumbers,
        string targetEntity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Messages locked by one receiver. Lock tokens only work on the receiver that issued them, so the batch owns it.
/// Disposing abandons anything still unsettled so other consumers get it straight back.
/// </summary>
public interface IReceivedBatch : IAsyncDisposable
{
    IReadOnlyList<MessageInfo> Messages { get; }
    Task CompleteAsync(MessageInfo message, CancellationToken cancellationToken = default);
    Task AbandonAsync(MessageInfo message, CancellationToken cancellationToken = default);
    Task DeadLetterAsync(MessageInfo message, string reason, string? description, CancellationToken cancellationToken = default);

    /// <summary>Defers the message; it can then only be received by sequence number.</summary>
    Task DeferAsync(MessageInfo message, CancellationToken cancellationToken = default);
}
