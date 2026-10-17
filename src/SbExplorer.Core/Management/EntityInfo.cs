namespace SbExplorer.Core.Management;

public enum EntityKind
{
    Queue,
    Topic,
    Subscription,
    Rule,
}

public sealed record MessageCounts(long Active, long DeadLetter, long Scheduled, long Transfer, long TransferDeadLetter)
{
    public long Total => Active + DeadLetter + Scheduled + Transfer + TransferDeadLetter;
}

/// <summary>One row on the properties panel.</summary>
public sealed record EntityProperty(string Group, string Name, string Value);

/// <summary>
/// SDK-independent snapshot of a queue, topic, subscription or rule, including runtime counts when the
/// namespace reports them.
/// </summary>
public sealed record EntityInfo
{
    public required EntityKind Kind { get; init; }

    /// <summary>The entity's name within its parent, e.g. <c>all-events</c> for a subscription.</summary>
    public required string Name { get; init; }

    /// <summary>Unique path: <c>orders</c>, <c>events/Subscriptions/all-events</c>, <c>…/Rules/$Default</c>.</summary>
    public required string Path { get; init; }

    public string Status { get; init; } = "Active";
    public MessageCounts? Counts { get; init; }
    public bool RequiresSession { get; init; }
    public string? ForwardTo { get; init; }
    public IReadOnlyList<EntityProperty> Properties { get; init; } = [];

    public bool IsDisabled => !string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    public static string SubscriptionPath(string topic, string subscription) => $"{topic}/Subscriptions/{subscription}";

    public static string RulePath(string topic, string subscription, string rule) =>
        $"{SubscriptionPath(topic, subscription)}/Rules/{rule}";
}
