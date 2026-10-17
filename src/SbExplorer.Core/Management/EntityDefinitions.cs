namespace SbExplorer.Core.Management;

/// <summary>Values Service Bus accepts for an entity's status.</summary>
public enum EntityStatusValue
{
    Active,
    Disabled,
    SendDisabled,
    ReceiveDisabled,
}

/// <summary>
/// Editable settings of a queue. Mirrors CreateQueueOptions; <see cref="RequiresSession"/>,
/// <see cref="RequiresDuplicateDetection"/> and <see cref="EnablePartitioning"/> can only be set at creation.
/// </summary>
public sealed record QueueDefinition
{
    public required string Name { get; init; }
    public TimeSpan LockDuration { get; init; } = TimeSpan.FromMinutes(1);
    public int MaxDeliveryCount { get; init; } = 10;
    public TimeSpan DefaultMessageTimeToLive { get; init; } = TimeSpan.MaxValue;
    public bool DeadLetteringOnMessageExpiration { get; init; }
    public bool RequiresSession { get; init; }
    public bool RequiresDuplicateDetection { get; init; }
    public TimeSpan DuplicateDetectionHistoryTimeWindow { get; init; } = TimeSpan.FromMinutes(10);
    public bool EnablePartitioning { get; init; }
    public long MaxSizeInMegabytes { get; init; } = 1024;
    public long? MaxMessageSizeInKilobytes { get; init; }
    public TimeSpan AutoDeleteOnIdle { get; init; } = TimeSpan.MaxValue;
    public string? ForwardTo { get; init; }
    public string? ForwardDeadLetteredMessagesTo { get; init; }
    public bool EnableBatchedOperations { get; init; } = true;
    public string? UserMetadata { get; init; }
    public EntityStatusValue Status { get; init; } = EntityStatusValue.Active;
}

public sealed record TopicDefinition
{
    public required string Name { get; init; }
    public TimeSpan DefaultMessageTimeToLive { get; init; } = TimeSpan.MaxValue;
    public bool RequiresDuplicateDetection { get; init; }
    public TimeSpan DuplicateDetectionHistoryTimeWindow { get; init; } = TimeSpan.FromMinutes(10);
    public bool EnablePartitioning { get; init; }
    public bool SupportOrdering { get; init; }
    public long MaxSizeInMegabytes { get; init; } = 1024;
    public long? MaxMessageSizeInKilobytes { get; init; }
    public TimeSpan AutoDeleteOnIdle { get; init; } = TimeSpan.MaxValue;
    public bool EnableBatchedOperations { get; init; } = true;
    public string? UserMetadata { get; init; }
    public EntityStatusValue Status { get; init; } = EntityStatusValue.Active;

    /// <summary>Only used by import/export, so a topic travels with its subscriptions.</summary>
    public IReadOnlyList<SubscriptionDefinition> Subscriptions { get; init; } = [];
}

public sealed record SubscriptionDefinition
{
    public required string Topic { get; init; }
    public required string Name { get; init; }
    public TimeSpan LockDuration { get; init; } = TimeSpan.FromMinutes(1);
    public int MaxDeliveryCount { get; init; } = 10;
    public TimeSpan DefaultMessageTimeToLive { get; init; } = TimeSpan.MaxValue;
    public bool DeadLetteringOnMessageExpiration { get; init; }
    public bool DeadLetteringOnFilterEvaluationExceptions { get; init; } = true;
    public bool RequiresSession { get; init; }
    public TimeSpan AutoDeleteOnIdle { get; init; } = TimeSpan.MaxValue;
    public string? ForwardTo { get; init; }
    public string? ForwardDeadLetteredMessagesTo { get; init; }
    public bool EnableBatchedOperations { get; init; } = true;
    public string? UserMetadata { get; init; }
    public EntityStatusValue Status { get; init; } = EntityStatusValue.Active;

    /// <summary>
    /// On create, replaces the default match-all rule when set. On import/export, all of the subscription's rules.
    /// </summary>
    public IReadOnlyList<RuleDefinition> Rules { get; init; } = [];
}

public enum RuleFilterKind
{
    Sql,
    Correlation,
    True,
    False,
}

public sealed record RuleDefinition
{
    public const string DefaultName = "$Default";

    public required string Name { get; init; }
    public RuleFilterKind FilterKind { get; init; } = RuleFilterKind.Sql;
    public string? SqlExpression { get; init; }
    public string? CorrelationId { get; init; }
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? SessionId { get; init; }
    public string? To { get; init; }
    public string? ReplyTo { get; init; }
    public string? ReplyToSessionId { get; init; }
    public string? ContentType { get; init; }

    /// <summary>Correlation filter application properties, matched as strings.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    public string? ActionSqlExpression { get; init; }

    public bool HasCorrelationCriteria =>
        new[] { CorrelationId, MessageId, Subject, SessionId, To, ReplyTo, ReplyToSessionId, ContentType }.Any(v => !string.IsNullOrWhiteSpace(v))
        || Properties.Count > 0;
}
