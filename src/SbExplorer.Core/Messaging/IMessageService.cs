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

    /// <summary>Sends to a queue or topic, batching as needed.</summary>
    Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default);
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
