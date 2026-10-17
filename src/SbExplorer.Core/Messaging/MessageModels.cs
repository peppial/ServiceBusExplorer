namespace SbExplorer.Core.Messaging;

public enum SubQueueKind
{
    None,
    DeadLetter,
    TransferDeadLetter,
}

/// <summary>Where messages are read from: a queue or a subscription, optionally its dead-letter sub-queue.</summary>
public sealed record MessageSource(string EntityPath, string? Topic, string? Subscription, SubQueueKind SubQueue = SubQueueKind.None)
{
    public static MessageSource ForQueue(string queue, SubQueueKind subQueue = SubQueueKind.None) =>
        new(queue, null, null, subQueue);

    public static MessageSource ForSubscription(string topic, string subscription, SubQueueKind subQueue = SubQueueKind.None) =>
        new($"{topic}/Subscriptions/{subscription}", topic, subscription, subQueue);

    public bool IsSubscription => Subscription is not null;

    /// <summary>The entity that accepts sends for this source: the queue itself, or the subscription's topic.</summary>
    public string SendTarget => Topic ?? EntityPath;
}

public enum MessageState
{
    Active,
    Deferred,
    Scheduled,
}

/// <summary>A received or peeked message, decoupled from the SDK.</summary>
public sealed record MessageInfo
{
    public long SequenceNumber { get; init; }
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? ContentType { get; init; }
    public string? CorrelationId { get; init; }
    public string? SessionId { get; init; }
    public string? PartitionKey { get; init; }
    public string? To { get; init; }
    public string? ReplyTo { get; init; }
    public string? ReplyToSessionId { get; init; }
    public TimeSpan TimeToLive { get; init; }
    public DateTimeOffset EnqueuedTime { get; init; }
    public DateTimeOffset ScheduledEnqueueTime { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
    public int DeliveryCount { get; init; }
    public MessageState State { get; init; }
    public string? DeadLetterReason { get; init; }
    public string? DeadLetterErrorDescription { get; init; }
    public string? DeadLetterSource { get; init; }

    /// <summary>"Data" for normal messages; "Value" or "Sequence" for AMQP bodies sent by non-.NET or legacy clients.</summary>
    public string BodyType { get; init; } = "Data";

    public ReadOnlyMemory<byte> Body { get; init; }
    public IReadOnlyDictionary<string, object?> ApplicationProperties { get; init; } = new Dictionary<string, object?>();

    /// <summary>Only set for messages received in peek-lock mode.</summary>
    public string? LockToken { get; init; }
}

/// <summary>A message to send.</summary>
public sealed record OutgoingMessage
{
    public ReadOnlyMemory<byte> Body { get; init; }
    public string? ContentType { get; init; }
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? CorrelationId { get; init; }
    public string? SessionId { get; init; }
    public string? PartitionKey { get; init; }
    public string? To { get; init; }
    public string? ReplyTo { get; init; }
    public string? ReplyToSessionId { get; init; }
    public TimeSpan? TimeToLive { get; init; }
    public DateTimeOffset? ScheduledEnqueueTime { get; init; }
    public IReadOnlyDictionary<string, object?> ApplicationProperties { get; init; } = new Dictionary<string, object?>();
}
