using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbExplorer.Core.Management;

/// <summary>An export file: queues, and topics with their subscriptions and rules.</summary>
public sealed record NamespaceDefinition
{
    public int Version { get; init; } = 1;
    public string? ExportedFrom { get; init; }
    public DateTimeOffset ExportedAt { get; init; }
    public IReadOnlyList<QueueDefinition> Queues { get; init; } = [];
    public IReadOnlyList<TopicDefinition> Topics { get; init; } = [];

    public int EntityCount => Queues.Count + Topics.Count + Topics.Sum(t => t.Subscriptions.Count);
}

public enum ImportOutcome
{
    Created,
    Updated,
    Skipped,
    Failed,
}

public sealed record ImportItemResult(string Path, ImportOutcome Outcome, string? Error = null);

/// <summary>Exports entity definitions to JSON and imports them into a namespace.</summary>
public sealed class NamespaceTransfer(IEntityService entities)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(NamespaceDefinition definition) => JsonSerializer.Serialize(definition, Json);

    public static NamespaceDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<NamespaceDefinition>(json, Json)
        ?? throw new JsonException("The file is empty.");

    /// <summary>
    /// Exports the given entities, or the whole namespace when <paramref name="only"/> is null. A selected
    /// subscription or rule exports its topic with just that subscription.
    /// </summary>
    public async Task<NamespaceDefinition> ExportAsync(
        string? namespaceName, IReadOnlyCollection<EntityInfo>? only = null, CancellationToken cancellationToken = default)
    {
        var queues = new List<QueueDefinition>();
        var topics = new List<TopicDefinition>();

        var queueNames = only is null
            ? (await entities.GetQueuesAsync(cancellationToken)).Select(q => q.Name)
            : only.Where(e => e.Kind == EntityKind.Queue).Select(e => e.Name);
        foreach (var name in queueNames)
        {
            queues.Add(await entities.GetQueueAsync(name, cancellationToken));
        }

        // topic -> null means "all subscriptions"
        var topicSelection = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        if (only is null)
        {
            foreach (var topic in await entities.GetTopicsAsync(cancellationToken))
            {
                topicSelection[topic.Name] = null;
            }
        }
        else
        {
            foreach (var entity in only)
            {
                var parts = entity.Path.Split('/');
                switch (entity.Kind)
                {
                    case EntityKind.Topic:
                        topicSelection[entity.Name] = null;
                        break;
                    case EntityKind.Subscription or EntityKind.Rule:
                        if (!topicSelection.TryGetValue(parts[0], out var set))
                        {
                            topicSelection[parts[0]] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }

                        set?.Add(parts[2]);
                        break;
                }
            }
        }

        foreach (var (topicName, subscriptionFilter) in topicSelection)
        {
            var topic = await entities.GetTopicAsync(topicName, cancellationToken);
            var subscriptionNames = subscriptionFilter?.ToList()
                ?? (await entities.GetSubscriptionsAsync(topicName, cancellationToken)).Select(s => s.Name).ToList();
            var subscriptions = new List<SubscriptionDefinition>();
            foreach (var subscriptionName in subscriptionNames)
            {
                var subscription = await entities.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
                var rules = new List<RuleDefinition>();
                foreach (var rule in await entities.GetRulesAsync(topicName, subscriptionName, cancellationToken))
                {
                    rules.Add(await entities.GetRuleAsync(topicName, subscriptionName, rule.Name, cancellationToken));
                }

                subscriptions.Add(subscription with { Rules = rules });
            }

            topics.Add(topic with { Subscriptions = subscriptions });
        }

        return new NamespaceDefinition
        {
            ExportedFrom = namespaceName,
            ExportedAt = DateTimeOffset.UtcNow,
            Queues = queues,
            Topics = topics,
        };
    }

    /// <summary>
    /// Creates every entity in <paramref name="definition"/> that doesn't exist yet. Existing ones are updated
    /// when <paramref name="overwrite"/> is set and skipped otherwise. One failure doesn't stop the rest.
    /// </summary>
    public async Task<IReadOnlyList<ImportItemResult>> ImportAsync(
        NamespaceDefinition definition,
        bool overwrite,
        IProgress<ImportItemResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ImportItemResult>();

        void Report(ImportItemResult result)
        {
            results.Add(result);
            progress?.Report(result);
        }

        var existingQueues = Names(await entities.GetQueuesAsync(cancellationToken));
        var existingTopics = Names(await entities.GetTopicsAsync(cancellationToken));

        foreach (var queue in definition.Queues)
        {
            Report(await ApplyAsync(queue.Name, existingQueues.Contains(queue.Name), overwrite,
                () => entities.CreateQueueAsync(queue, cancellationToken),
                () => entities.UpdateQueueAsync(queue, cancellationToken)));
        }

        foreach (var topic in definition.Topics)
        {
            var topicResult = await ApplyAsync(topic.Name, existingTopics.Contains(topic.Name), overwrite,
                () => entities.CreateTopicAsync(topic with { Subscriptions = [] }, cancellationToken),
                () => entities.UpdateTopicAsync(topic, cancellationToken));
            Report(topicResult);
            if (topicResult.Outcome == ImportOutcome.Failed)
            {
                continue;
            }

            var existingSubscriptions = existingTopics.Contains(topic.Name)
                ? Names(await entities.GetSubscriptionsAsync(topic.Name, cancellationToken))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subscription in topic.Subscriptions)
            {
                var path = EntityInfo.SubscriptionPath(topic.Name, subscription.Name);
                var item = subscription with { Topic = topic.Name };
                var exists = existingSubscriptions.Contains(subscription.Name);
                Report(await ApplyAsync(path, exists, overwrite,
                    () => CreateSubscriptionWithRulesAsync(item, cancellationToken),
                    () => UpdateSubscriptionWithRulesAsync(item, cancellationToken)));
            }
        }

        return results;
    }

    private async Task CreateSubscriptionWithRulesAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken)
    {
        // The first rule replaces $Default on create; the rest are added after.
        await entities.CreateSubscriptionAsync(subscription with { Rules = subscription.Rules.Take(1).ToList() }, cancellationToken);
        foreach (var rule in subscription.Rules.Skip(1))
        {
            await entities.CreateRuleAsync(subscription.Topic, subscription.Name, rule, cancellationToken);
        }
    }

    private async Task UpdateSubscriptionWithRulesAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken)
    {
        await entities.UpdateSubscriptionAsync(subscription, cancellationToken);
        var existingRules = Names(await entities.GetRulesAsync(subscription.Topic, subscription.Name, cancellationToken));
        foreach (var rule in subscription.Rules)
        {
            if (existingRules.Contains(rule.Name))
            {
                await entities.UpdateRuleAsync(subscription.Topic, subscription.Name, rule, cancellationToken);
            }
            else
            {
                await entities.CreateRuleAsync(subscription.Topic, subscription.Name, rule, cancellationToken);
            }
        }
    }

    private static async Task<ImportItemResult> ApplyAsync(string path, bool exists, bool overwrite, Func<Task> create, Func<Task> update)
    {
        if (exists && !overwrite)
        {
            return new ImportItemResult(path, ImportOutcome.Skipped);
        }

        try
        {
            await (exists ? update() : create());
            return new ImportItemResult(path, exists ? ImportOutcome.Updated : ImportOutcome.Created);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ImportItemResult(path, ImportOutcome.Failed, ex.Message.Split('\n', 2)[0].Trim());
        }
    }

    private static HashSet<string> Names(IEnumerable<EntityInfo> items) =>
        items.Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
