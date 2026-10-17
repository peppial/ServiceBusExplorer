using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Management;
using static SbExplorer.Core.Management.DisplayFormat;

namespace SbExplorer.Infrastructure.Management;

/// <summary>Maps administration SDK models to <see cref="EntityInfo"/>.</summary>
public static class EntityMapper
{
    private const string Settings = "Settings";
    private const string Runtime = "Runtime";
    private const string Filter = "Filter";

    public static EntityInfo Queue(QueueProperties queue, QueueRuntimeProperties? runtime)
    {
        var properties = new List<EntityProperty>
        {
            new(Settings, "Status", queue.Status.ToString()),
            new(Settings, "Lock duration", Duration(queue.LockDuration)),
            new(Settings, "Max delivery count", Count(queue.MaxDeliveryCount)),
            new(Settings, "Default message time to live", Duration(queue.DefaultMessageTimeToLive)),
            new(Settings, "Dead-letter on expiration", YesNo(queue.DeadLetteringOnMessageExpiration)),
            new(Settings, "Requires session", YesNo(queue.RequiresSession)),
            new(Settings, "Duplicate detection", queue.RequiresDuplicateDetection ? Duration(queue.DuplicateDetectionHistoryTimeWindow) : "Off"),
            new(Settings, "Partitioned", YesNo(queue.EnablePartitioning)),
            new(Settings, "Max size", Size(queue.MaxSizeInMegabytes * 1024 * 1024)),
            new(Settings, "Auto-delete on idle", Duration(queue.AutoDeleteOnIdle)),
            new(Settings, "Forward to", OrDash(EntityName(queue.ForwardTo))),
            new(Settings, "Forward dead-lettered to", OrDash(EntityName(queue.ForwardDeadLetteredMessagesTo))),
            new(Settings, "Batched operations", YesNo(queue.EnableBatchedOperations)),
        };
        if (queue.MaxMessageSizeInKilobytes is { } maxMessageKb)
        {
            properties.Add(new(Settings, "Max message size", Size(maxMessageKb * 1024)));
        }

        properties.Add(new(Settings, "User metadata", OrDash(queue.UserMetadata)));

        MessageCounts? counts = null;
        if (runtime is not null)
        {
            counts = new MessageCounts(
                runtime.ActiveMessageCount,
                runtime.DeadLetterMessageCount,
                runtime.ScheduledMessageCount,
                runtime.TransferMessageCount,
                runtime.TransferDeadLetterMessageCount);
            AddCounts(properties, counts);
            properties.Add(new(Runtime, "Size In Bytes", Count(runtime.SizeInBytes)));
            AddTimestamps(properties, runtime.CreatedAt, runtime.UpdatedAt, runtime.AccessedAt);
        }

        return new EntityInfo
        {
            Kind = EntityKind.Queue,
            Name = queue.Name,
            Path = queue.Name,
            Status = queue.Status.ToString(),
            Counts = counts,
            RequiresSession = queue.RequiresSession,
            MaxDeliveryCount = queue.MaxDeliveryCount,
            ForwardTo = EntityName(queue.ForwardTo),
            Properties = properties,
        };
    }

    public static EntityInfo Topic(TopicProperties topic, TopicRuntimeProperties? runtime)
    {
        var properties = new List<EntityProperty>
        {
            new(Settings, "Status", topic.Status.ToString()),
            new(Settings, "Default message time to live", Duration(topic.DefaultMessageTimeToLive)),
            new(Settings, "Duplicate detection", topic.RequiresDuplicateDetection ? Duration(topic.DuplicateDetectionHistoryTimeWindow) : "Off"),
            new(Settings, "Partitioned", YesNo(topic.EnablePartitioning)),
            new(Settings, "Support ordering", YesNo(topic.SupportOrdering)),
            new(Settings, "Max size", Size(topic.MaxSizeInMegabytes * 1024 * 1024)),
            new(Settings, "Auto-delete on idle", Duration(topic.AutoDeleteOnIdle)),
            new(Settings, "Batched operations", YesNo(topic.EnableBatchedOperations)),
        };
        if (topic.MaxMessageSizeInKilobytes is { } maxMessageKb)
        {
            properties.Add(new(Settings, "Max message size", Size(maxMessageKb * 1024)));
        }

        properties.Add(new(Settings, "User metadata", OrDash(topic.UserMetadata)));

        if (runtime is not null)
        {
            properties.Add(new(Runtime, "Subscription Count", Count(runtime.SubscriptionCount)));
            properties.Add(new(Runtime, "Scheduled Message Count", Count(runtime.ScheduledMessageCount)));
            properties.Add(new(Runtime, "Size In Bytes", Count(runtime.SizeInBytes)));
            AddTimestamps(properties, runtime.CreatedAt, runtime.UpdatedAt, runtime.AccessedAt);
        }

        return new EntityInfo
        {
            Kind = EntityKind.Topic,
            Name = topic.Name,
            Path = topic.Name,
            Status = topic.Status.ToString(),
            Properties = properties,
        };
    }

    public static EntityInfo Subscription(SubscriptionProperties subscription, SubscriptionRuntimeProperties? runtime)
    {
        var properties = new List<EntityProperty>
        {
            new(Settings, "Status", subscription.Status.ToString()),
            new(Settings, "Lock duration", Duration(subscription.LockDuration)),
            new(Settings, "Max delivery count", Count(subscription.MaxDeliveryCount)),
            new(Settings, "Default message time to live", Duration(subscription.DefaultMessageTimeToLive)),
            new(Settings, "Dead-letter on expiration", YesNo(subscription.DeadLetteringOnMessageExpiration)),
            new(Settings, "Dead-letter on filter errors", YesNo(subscription.EnableDeadLetteringOnFilterEvaluationExceptions)),
            new(Settings, "Requires session", YesNo(subscription.RequiresSession)),
            new(Settings, "Auto-delete on idle", Duration(subscription.AutoDeleteOnIdle)),
            new(Settings, "Forward to", OrDash(EntityName(subscription.ForwardTo))),
            new(Settings, "Forward dead-lettered to", OrDash(EntityName(subscription.ForwardDeadLetteredMessagesTo))),
            new(Settings, "Batched operations", YesNo(subscription.EnableBatchedOperations)),
            new(Settings, "User metadata", OrDash(subscription.UserMetadata)),
        };

        MessageCounts? counts = null;
        if (runtime is not null)
        {
            // Subscriptions don't report a scheduled count; scheduled messages live on the topic.
            counts = new MessageCounts(
                runtime.ActiveMessageCount,
                runtime.DeadLetterMessageCount,
                Scheduled: 0,
                runtime.TransferMessageCount,
                runtime.TransferDeadLetterMessageCount);
            AddCounts(properties, counts, includeScheduled: false);
            AddTimestamps(properties, runtime.CreatedAt, runtime.UpdatedAt, runtime.AccessedAt);
        }

        return new EntityInfo
        {
            Kind = EntityKind.Subscription,
            Name = subscription.SubscriptionName,
            Path = EntityInfo.SubscriptionPath(subscription.TopicName, subscription.SubscriptionName),
            Status = subscription.Status.ToString(),
            Counts = counts,
            RequiresSession = subscription.RequiresSession,
            MaxDeliveryCount = subscription.MaxDeliveryCount,
            ForwardTo = EntityName(subscription.ForwardTo),
            Properties = properties,
        };
    }

    public static EntityInfo Rule(string topic, string subscription, RuleProperties rule)
    {
        var properties = new List<EntityProperty>();
        switch (rule.Filter)
        {
            case TrueRuleFilter:
                properties.Add(new(Filter, "Type", "True (all messages)"));
                break;
            case FalseRuleFilter:
                properties.Add(new(Filter, "Type", "False (no messages)"));
                break;
            case SqlRuleFilter sql:
                properties.Add(new(Filter, "Type", "SQL"));
                properties.Add(new(Filter, "Expression", sql.SqlExpression));
                break;
            case CorrelationRuleFilter correlation:
                properties.Add(new(Filter, "Type", "Correlation"));
                AddIfSet(properties, "Correlation ID", correlation.CorrelationId);
                AddIfSet(properties, "Message ID", correlation.MessageId);
                AddIfSet(properties, "Subject", correlation.Subject);
                AddIfSet(properties, "Session ID", correlation.SessionId);
                AddIfSet(properties, "To", correlation.To);
                AddIfSet(properties, "Reply to", correlation.ReplyTo);
                AddIfSet(properties, "Reply to session ID", correlation.ReplyToSessionId);
                AddIfSet(properties, "Content type", correlation.ContentType);
                foreach (var (key, value) in correlation.ApplicationProperties)
                {
                    properties.Add(new(Filter, $"Property: {key}", value?.ToString() ?? "null"));
                }

                break;
            default:
                properties.Add(new(Filter, "Type", rule.Filter?.GetType().Name ?? "None"));
                break;
        }

        properties.Add(new("Action", "SQL action", OrDash((rule.Action as SqlRuleAction)?.SqlExpression)));

        return new EntityInfo
        {
            Kind = EntityKind.Rule,
            Name = rule.Name,
            Path = EntityInfo.RulePath(topic, subscription, rule.Name),
            Properties = properties,
        };
    }

    /// <summary>ForwardTo comes back as a full URI on real namespaces and a bare name on the emulator.</summary>
    public static string? EntityName(string? forwardTo)
    {
        if (string.IsNullOrWhiteSpace(forwardTo))
        {
            return null;
        }

        return Uri.TryCreate(forwardTo, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.AbsolutePath.Trim('/')
            : forwardTo;
    }

    private static void AddCounts(List<EntityProperty> properties, MessageCounts counts, bool includeScheduled = true)
    {
        // Names match the original explorer's "Queue Information" list.
        properties.Add(new(Runtime, "Active Message Count", Count(counts.Active)));
        properties.Add(new(Runtime, "DeadLetter Message Count", Count(counts.DeadLetter)));
        if (includeScheduled)
        {
            properties.Add(new(Runtime, "Scheduled Message Count", Count(counts.Scheduled)));
        }

        properties.Add(new(Runtime, "Transfer Message Count", Count(counts.Transfer)));
        properties.Add(new(Runtime, "Transfer DL Message Count", Count(counts.TransferDeadLetter)));
        properties.Add(new(Runtime, "Message Count", Count(counts.Total)));
    }

    private static void AddTimestamps(List<EntityProperty> properties, DateTimeOffset created, DateTimeOffset updated, DateTimeOffset accessed)
    {
        properties.Add(new(Runtime, "Created At", Timestamp(created)));
        properties.Add(new(Runtime, "Accessed At", Timestamp(accessed)));
        properties.Add(new(Runtime, "Updated At", Timestamp(updated)));
    }

    private static void AddIfSet(List<EntityProperty> properties, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            properties.Add(new(Filter, name, value));
        }
    }
}
