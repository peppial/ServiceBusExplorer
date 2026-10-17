using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Management;

namespace SbExplorer.Infrastructure.Management;

/// <summary>Converts between SDK administration models and the editable definitions.</summary>
public static class DefinitionMapper
{
    public static EntityStatus ToStatus(EntityStatusValue status) => status switch
    {
        EntityStatusValue.Disabled => EntityStatus.Disabled,
        EntityStatusValue.SendDisabled => EntityStatus.SendDisabled,
        EntityStatusValue.ReceiveDisabled => EntityStatus.ReceiveDisabled,
        _ => EntityStatus.Active,
    };

    public static EntityStatusValue FromStatus(EntityStatus status) =>
        status == EntityStatus.Disabled ? EntityStatusValue.Disabled
        : status == EntityStatus.SendDisabled ? EntityStatusValue.SendDisabled
        : status == EntityStatus.ReceiveDisabled ? EntityStatusValue.ReceiveDisabled
        : EntityStatusValue.Active;

    public static QueueDefinition ToDefinition(QueueProperties q) => new()
    {
        Name = q.Name,
        LockDuration = q.LockDuration,
        MaxDeliveryCount = q.MaxDeliveryCount,
        DefaultMessageTimeToLive = q.DefaultMessageTimeToLive,
        DeadLetteringOnMessageExpiration = q.DeadLetteringOnMessageExpiration,
        RequiresSession = q.RequiresSession,
        RequiresDuplicateDetection = q.RequiresDuplicateDetection,
        DuplicateDetectionHistoryTimeWindow = q.DuplicateDetectionHistoryTimeWindow,
        EnablePartitioning = q.EnablePartitioning,
        MaxSizeInMegabytes = q.MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = q.MaxMessageSizeInKilobytes,
        AutoDeleteOnIdle = q.AutoDeleteOnIdle,
        ForwardTo = EntityMapper.EntityName(q.ForwardTo),
        ForwardDeadLetteredMessagesTo = EntityMapper.EntityName(q.ForwardDeadLetteredMessagesTo),
        EnableBatchedOperations = q.EnableBatchedOperations,
        UserMetadata = NullIfEmpty(q.UserMetadata),
        Status = FromStatus(q.Status),
    };

    public static CreateQueueOptions ToCreateOptions(QueueDefinition d)
    {
        var options = new CreateQueueOptions(d.Name)
        {
            RequiresSession = d.RequiresSession,
            RequiresDuplicateDetection = d.RequiresDuplicateDetection,
            EnablePartitioning = d.EnablePartitioning,
        };
        if (d.RequiresDuplicateDetection)
        {
            options.DuplicateDetectionHistoryTimeWindow = d.DuplicateDetectionHistoryTimeWindow;
        }

        options.LockDuration = d.LockDuration;
        options.MaxDeliveryCount = d.MaxDeliveryCount;
        options.DefaultMessageTimeToLive = d.DefaultMessageTimeToLive;
        options.DeadLetteringOnMessageExpiration = d.DeadLetteringOnMessageExpiration;
        options.MaxSizeInMegabytes = d.MaxSizeInMegabytes;
        options.AutoDeleteOnIdle = d.AutoDeleteOnIdle;
        options.ForwardTo = NullIfEmpty(d.ForwardTo);
        options.ForwardDeadLetteredMessagesTo = NullIfEmpty(d.ForwardDeadLetteredMessagesTo);
        options.EnableBatchedOperations = d.EnableBatchedOperations;
        SetMetadata(d.UserMetadata, value => options.UserMetadata = value);
        options.Status = ToStatus(d.Status);
        if (d.MaxMessageSizeInKilobytes is { } maxKb)
        {
            options.MaxMessageSizeInKilobytes = maxKb;
        }

        return options;
    }

    /// <summary>Copies the settings Service Bus allows to change after creation.</summary>
    public static void ApplyUpdate(QueueProperties q, QueueDefinition d)
    {
        q.LockDuration = d.LockDuration;
        q.MaxDeliveryCount = d.MaxDeliveryCount;
        q.DefaultMessageTimeToLive = d.DefaultMessageTimeToLive;
        q.DeadLetteringOnMessageExpiration = d.DeadLetteringOnMessageExpiration;
        if (q.RequiresDuplicateDetection)
        {
            q.DuplicateDetectionHistoryTimeWindow = d.DuplicateDetectionHistoryTimeWindow;
        }

        q.MaxSizeInMegabytes = d.MaxSizeInMegabytes;
        q.AutoDeleteOnIdle = d.AutoDeleteOnIdle;
        q.ForwardTo = NullIfEmpty(d.ForwardTo);
        q.ForwardDeadLetteredMessagesTo = NullIfEmpty(d.ForwardDeadLetteredMessagesTo);
        q.EnableBatchedOperations = d.EnableBatchedOperations;
        q.UserMetadata = NullIfEmpty(d.UserMetadata) ?? string.Empty;
        q.Status = ToStatus(d.Status);
        if (d.MaxMessageSizeInKilobytes is { } maxKb)
        {
            q.MaxMessageSizeInKilobytes = maxKb;
        }
    }

    public static TopicDefinition ToDefinition(TopicProperties t) => new()
    {
        Name = t.Name,
        DefaultMessageTimeToLive = t.DefaultMessageTimeToLive,
        RequiresDuplicateDetection = t.RequiresDuplicateDetection,
        DuplicateDetectionHistoryTimeWindow = t.DuplicateDetectionHistoryTimeWindow,
        EnablePartitioning = t.EnablePartitioning,
        SupportOrdering = t.SupportOrdering,
        MaxSizeInMegabytes = t.MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = t.MaxMessageSizeInKilobytes,
        AutoDeleteOnIdle = t.AutoDeleteOnIdle,
        EnableBatchedOperations = t.EnableBatchedOperations,
        UserMetadata = NullIfEmpty(t.UserMetadata),
        Status = FromStatus(t.Status),
    };

    public static CreateTopicOptions ToCreateOptions(TopicDefinition d)
    {
        var options = new CreateTopicOptions(d.Name)
        {
            RequiresDuplicateDetection = d.RequiresDuplicateDetection,
            EnablePartitioning = d.EnablePartitioning,
            SupportOrdering = d.SupportOrdering,
            DefaultMessageTimeToLive = d.DefaultMessageTimeToLive,
            MaxSizeInMegabytes = d.MaxSizeInMegabytes,
            AutoDeleteOnIdle = d.AutoDeleteOnIdle,
            EnableBatchedOperations = d.EnableBatchedOperations,
            Status = ToStatus(d.Status),
        };
        SetMetadata(d.UserMetadata, value => options.UserMetadata = value);
        if (d.RequiresDuplicateDetection)
        {
            options.DuplicateDetectionHistoryTimeWindow = d.DuplicateDetectionHistoryTimeWindow;
        }

        if (d.MaxMessageSizeInKilobytes is { } maxKb)
        {
            options.MaxMessageSizeInKilobytes = maxKb;
        }

        return options;
    }

    public static void ApplyUpdate(TopicProperties t, TopicDefinition d)
    {
        t.DefaultMessageTimeToLive = d.DefaultMessageTimeToLive;
        if (t.RequiresDuplicateDetection)
        {
            t.DuplicateDetectionHistoryTimeWindow = d.DuplicateDetectionHistoryTimeWindow;
        }

        t.SupportOrdering = d.SupportOrdering;
        t.MaxSizeInMegabytes = d.MaxSizeInMegabytes;
        t.AutoDeleteOnIdle = d.AutoDeleteOnIdle;
        t.EnableBatchedOperations = d.EnableBatchedOperations;
        t.UserMetadata = NullIfEmpty(d.UserMetadata) ?? string.Empty;
        t.Status = ToStatus(d.Status);
        if (d.MaxMessageSizeInKilobytes is { } maxKb)
        {
            t.MaxMessageSizeInKilobytes = maxKb;
        }
    }

    public static SubscriptionDefinition ToDefinition(SubscriptionProperties s) => new()
    {
        Topic = s.TopicName,
        Name = s.SubscriptionName,
        LockDuration = s.LockDuration,
        MaxDeliveryCount = s.MaxDeliveryCount,
        DefaultMessageTimeToLive = s.DefaultMessageTimeToLive,
        DeadLetteringOnMessageExpiration = s.DeadLetteringOnMessageExpiration,
        DeadLetteringOnFilterEvaluationExceptions = s.EnableDeadLetteringOnFilterEvaluationExceptions,
        RequiresSession = s.RequiresSession,
        AutoDeleteOnIdle = s.AutoDeleteOnIdle,
        ForwardTo = EntityMapper.EntityName(s.ForwardTo),
        ForwardDeadLetteredMessagesTo = EntityMapper.EntityName(s.ForwardDeadLetteredMessagesTo),
        EnableBatchedOperations = s.EnableBatchedOperations,
        UserMetadata = NullIfEmpty(s.UserMetadata),
        Status = FromStatus(s.Status),
    };

    public static CreateSubscriptionOptions ToCreateOptions(SubscriptionDefinition d)
    {
        var options = new CreateSubscriptionOptions(d.Topic, d.Name)
        {
        LockDuration = d.LockDuration,
        MaxDeliveryCount = d.MaxDeliveryCount,
        DefaultMessageTimeToLive = d.DefaultMessageTimeToLive,
        DeadLetteringOnMessageExpiration = d.DeadLetteringOnMessageExpiration,
        EnableDeadLetteringOnFilterEvaluationExceptions = d.DeadLetteringOnFilterEvaluationExceptions,
        RequiresSession = d.RequiresSession,
        AutoDeleteOnIdle = d.AutoDeleteOnIdle,
        ForwardTo = NullIfEmpty(d.ForwardTo),
        ForwardDeadLetteredMessagesTo = NullIfEmpty(d.ForwardDeadLetteredMessagesTo),
        EnableBatchedOperations = d.EnableBatchedOperations,
        Status = ToStatus(d.Status),
        };
        SetMetadata(d.UserMetadata, value => options.UserMetadata = value);
        return options;
    }

    public static void ApplyUpdate(SubscriptionProperties s, SubscriptionDefinition d)
    {
        s.LockDuration = d.LockDuration;
        s.MaxDeliveryCount = d.MaxDeliveryCount;
        s.DefaultMessageTimeToLive = d.DefaultMessageTimeToLive;
        s.DeadLetteringOnMessageExpiration = d.DeadLetteringOnMessageExpiration;
        s.EnableDeadLetteringOnFilterEvaluationExceptions = d.DeadLetteringOnFilterEvaluationExceptions;
        s.AutoDeleteOnIdle = d.AutoDeleteOnIdle;
        s.ForwardTo = NullIfEmpty(d.ForwardTo);
        s.ForwardDeadLetteredMessagesTo = NullIfEmpty(d.ForwardDeadLetteredMessagesTo);
        s.EnableBatchedOperations = d.EnableBatchedOperations;
        s.UserMetadata = NullIfEmpty(d.UserMetadata) ?? string.Empty;
        s.Status = ToStatus(d.Status);
    }

    public static RuleDefinition ToDefinition(RuleProperties rule)
    {
        var definition = new RuleDefinition
        {
            Name = rule.Name,
            ActionSqlExpression = NullIfEmpty((rule.Action as SqlRuleAction)?.SqlExpression),
        };

        // TrueRuleFilter and FalseRuleFilter derive from SqlRuleFilter, so they are matched first.
        return rule.Filter switch
        {
            TrueRuleFilter => definition with { FilterKind = RuleFilterKind.True },
            FalseRuleFilter => definition with { FilterKind = RuleFilterKind.False },
            SqlRuleFilter sql => definition with { FilterKind = RuleFilterKind.Sql, SqlExpression = sql.SqlExpression },
            CorrelationRuleFilter c => definition with
            {
                FilterKind = RuleFilterKind.Correlation,
                CorrelationId = NullIfEmpty(c.CorrelationId),
                MessageId = NullIfEmpty(c.MessageId),
                Subject = NullIfEmpty(c.Subject),
                SessionId = NullIfEmpty(c.SessionId),
                To = NullIfEmpty(c.To),
                ReplyTo = NullIfEmpty(c.ReplyTo),
                ReplyToSessionId = NullIfEmpty(c.ReplyToSessionId),
                ContentType = NullIfEmpty(c.ContentType),
                Properties = c.ApplicationProperties.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? string.Empty),
            },
            _ => definition with { FilterKind = RuleFilterKind.True },
        };
    }

    public static RuleFilter ToFilter(RuleDefinition d)
    {
        switch (d.FilterKind)
        {
            case RuleFilterKind.True:
                return new TrueRuleFilter();
            case RuleFilterKind.False:
                return new FalseRuleFilter();
            case RuleFilterKind.Correlation:
                var filter = new CorrelationRuleFilter
                {
                    CorrelationId = NullIfEmpty(d.CorrelationId),
                    MessageId = NullIfEmpty(d.MessageId),
                    Subject = NullIfEmpty(d.Subject),
                    SessionId = NullIfEmpty(d.SessionId),
                    To = NullIfEmpty(d.To),
                    ReplyTo = NullIfEmpty(d.ReplyTo),
                    ReplyToSessionId = NullIfEmpty(d.ReplyToSessionId),
                    ContentType = NullIfEmpty(d.ContentType),
                };
                foreach (var (key, value) in d.Properties)
                {
                    filter.ApplicationProperties[key] = value;
                }

                return filter;
            default:
                return new SqlRuleFilter(d.SqlExpression ?? "1=1");
        }
    }

    public static RuleAction? ToAction(RuleDefinition d) =>
        string.IsNullOrWhiteSpace(d.ActionSqlExpression) ? null : new SqlRuleAction(d.ActionSqlExpression);

    public static CreateRuleOptions ToCreateOptions(RuleDefinition d) => new(d.Name, ToFilter(d)) { Action = ToAction(d) };

    // The SDK rejects a null UserMetadata, so it's only set when there is a value.
    private static void SetMetadata(string? value, Action<string> set)
    {
        if (NullIfEmpty(value) is { } metadata)
        {
            set(metadata);
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
