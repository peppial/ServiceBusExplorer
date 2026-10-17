using Azure;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Management;

namespace SbExplorer.Infrastructure.Management;

public sealed class AzureEntityService(ServiceBusAdministrationClient administration) : IEntityService
{
    public async Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = ToListAsync(administration.GetQueuesAsync(cancellationToken), cancellationToken);
        var runtime = ToListAsync(administration.GetQueuesRuntimePropertiesAsync(cancellationToken), cancellationToken);
        await Task.WhenAll(queues, runtime);

        var runtimeByName = (await runtime).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        return (await queues)
            .Select(q => EntityMapper.Queue(q, runtimeByName.GetValueOrDefault(q.Name)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        var topics = ToListAsync(administration.GetTopicsAsync(cancellationToken), cancellationToken);
        var runtime = ToListAsync(administration.GetTopicsRuntimePropertiesAsync(cancellationToken), cancellationToken);
        await Task.WhenAll(topics, runtime);

        var runtimeByName = (await runtime).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        return (await topics)
            .Select(t => EntityMapper.Topic(t, runtimeByName.GetValueOrDefault(t.Name)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default)
    {
        var subscriptions = ToListAsync(administration.GetSubscriptionsAsync(topic, cancellationToken), cancellationToken);
        var runtime = ToListAsync(administration.GetSubscriptionsRuntimePropertiesAsync(topic, cancellationToken), cancellationToken);
        await Task.WhenAll(subscriptions, runtime);

        var runtimeByName = (await runtime).ToDictionary(r => r.SubscriptionName, StringComparer.OrdinalIgnoreCase);
        return (await subscriptions)
            .Select(s => EntityMapper.Subscription(s, runtimeByName.GetValueOrDefault(s.SubscriptionName)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default)
    {
        var rules = await ToListAsync(administration.GetRulesAsync(topic, subscription, cancellationToken), cancellationToken);
        return rules
            .Select(r => EntityMapper.Rule(topic, subscription, r))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<QueueDefinition> GetQueueAsync(string name, CancellationToken cancellationToken = default) =>
        DefinitionMapper.ToDefinition((await administration.GetQueueAsync(name, cancellationToken)).Value);

    public async Task<TopicDefinition> GetTopicAsync(string name, CancellationToken cancellationToken = default) =>
        DefinitionMapper.ToDefinition((await administration.GetTopicAsync(name, cancellationToken)).Value);

    public async Task<SubscriptionDefinition> GetSubscriptionAsync(string topic, string name, CancellationToken cancellationToken = default) =>
        DefinitionMapper.ToDefinition((await administration.GetSubscriptionAsync(topic, name, cancellationToken)).Value);

    public async Task<RuleDefinition> GetRuleAsync(string topic, string subscription, string name, CancellationToken cancellationToken = default) =>
        DefinitionMapper.ToDefinition((await administration.GetRuleAsync(topic, subscription, name, cancellationToken)).Value);

    public Task CreateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) =>
        administration.CreateQueueAsync(DefinitionMapper.ToCreateOptions(queue), cancellationToken);

    public Task CreateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) =>
        administration.CreateTopicAsync(DefinitionMapper.ToCreateOptions(topic), cancellationToken);

    public Task CreateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        subscription.Rules.Count == 0
            ? administration.CreateSubscriptionAsync(DefinitionMapper.ToCreateOptions(subscription), cancellationToken)
            : administration.CreateSubscriptionAsync(
                DefinitionMapper.ToCreateOptions(subscription), DefinitionMapper.ToCreateOptions(subscription.Rules[0]), cancellationToken);

    public Task CreateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) =>
        administration.CreateRuleAsync(topic, subscription, DefinitionMapper.ToCreateOptions(rule), cancellationToken);

    public async Task UpdateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default)
    {
        var existing = (await administration.GetQueueAsync(queue.Name, cancellationToken)).Value;
        DefinitionMapper.ApplyUpdate(existing, queue);
        await administration.UpdateQueueAsync(existing, cancellationToken);
    }

    public async Task UpdateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default)
    {
        var existing = (await administration.GetTopicAsync(topic.Name, cancellationToken)).Value;
        DefinitionMapper.ApplyUpdate(existing, topic);
        await administration.UpdateTopicAsync(existing, cancellationToken);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        var existing = (await administration.GetSubscriptionAsync(subscription.Topic, subscription.Name, cancellationToken)).Value;
        DefinitionMapper.ApplyUpdate(existing, subscription);
        await administration.UpdateSubscriptionAsync(existing, cancellationToken);
    }

    public async Task UpdateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default)
    {
        var existing = (await administration.GetRuleAsync(topic, subscription, rule.Name, cancellationToken)).Value;
        existing.Filter = DefinitionMapper.ToFilter(rule);
        existing.Action = DefinitionMapper.ToAction(rule);
        await administration.UpdateRuleAsync(topic, subscription, existing, cancellationToken);
    }

    public Task DeleteAsync(EntityInfo entity, CancellationToken cancellationToken = default)
    {
        var parts = entity.Path.Split('/');
        return entity.Kind switch
        {
            EntityKind.Queue => administration.DeleteQueueAsync(entity.Name, cancellationToken),
            EntityKind.Topic => administration.DeleteTopicAsync(entity.Name, cancellationToken),
            EntityKind.Subscription => administration.DeleteSubscriptionAsync(parts[0], entity.Name, cancellationToken),
            EntityKind.Rule => administration.DeleteRuleAsync(parts[0], parts[2], entity.Name, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(entity), entity.Kind, null),
        };
    }

    public async Task SetStatusAsync(EntityInfo entity, EntityStatusValue status, CancellationToken cancellationToken = default)
    {
        var parts = entity.Path.Split('/');
        var value = DefinitionMapper.ToStatus(status);
        switch (entity.Kind)
        {
            case EntityKind.Queue:
                var queue = (await administration.GetQueueAsync(entity.Name, cancellationToken)).Value;
                queue.Status = value;
                await administration.UpdateQueueAsync(queue, cancellationToken);
                break;
            case EntityKind.Topic:
                var topic = (await administration.GetTopicAsync(entity.Name, cancellationToken)).Value;
                topic.Status = value;
                await administration.UpdateTopicAsync(topic, cancellationToken);
                break;
            case EntityKind.Subscription:
                var subscription = (await administration.GetSubscriptionAsync(parts[0], entity.Name, cancellationToken)).Value;
                subscription.Status = value;
                await administration.UpdateSubscriptionAsync(subscription, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("Rules don't have a status.");
        }
    }

    private static async Task<List<T>> ToListAsync<T>(AsyncPageable<T> pageable, CancellationToken cancellationToken) where T : notnull
    {
        var items = new List<T>();
        await foreach (var item in pageable.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }
}
