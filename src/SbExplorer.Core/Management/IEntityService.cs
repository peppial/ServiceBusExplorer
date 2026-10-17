namespace SbExplorer.Core.Management;

public interface IEntityService
{
    Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settings and runtime counts of one queue, topic or subscription. Works with a key scoped to that entity,
    /// which can't list the namespace.
    /// </summary>
    Task<EntityInfo> GetEntityAsync(EntityInfo reference, CancellationToken cancellationToken = default);

    Task<QueueDefinition> GetQueueAsync(string name, CancellationToken cancellationToken = default);
    Task<TopicDefinition> GetTopicAsync(string name, CancellationToken cancellationToken = default);
    Task<SubscriptionDefinition> GetSubscriptionAsync(string topic, string name, CancellationToken cancellationToken = default);
    Task<RuleDefinition> GetRuleAsync(string topic, string subscription, string name, CancellationToken cancellationToken = default);

    Task CreateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default);
    Task CreateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default);

    /// <summary>Creates the subscription; its first rule (if any) replaces the default match-all rule.</summary>
    Task CreateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default);

    Task CreateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default);

    Task UpdateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default);
    Task UpdateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default);
    Task UpdateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default);
    Task UpdateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default);

    /// <summary>Deletes the entity at <paramref name="entity"/>'s path, including a topic's subscriptions.</summary>
    Task DeleteAsync(EntityInfo entity, CancellationToken cancellationToken = default);

    Task SetStatusAsync(EntityInfo entity, EntityStatusValue status, CancellationToken cancellationToken = default);
}
