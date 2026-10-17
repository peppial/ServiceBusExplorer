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
