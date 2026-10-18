using SbExplorer.Core.Management;

namespace SbExplorer.Core.Tests;

public class NamespaceTransferTests
{
    private readonly InMemoryEntityService _entities = new();

    public NamespaceTransferTests()
    {
        _entities.Add(new QueueDefinition { Name = "orders", MaxDeliveryCount = 5, DefaultMessageTimeToLive = TimeSpan.FromDays(7) });
        _entities.Add(new QueueDefinition { Name = "payments", RequiresDuplicateDetection = true });
        _entities.Add(new TopicDefinition { Name = "events" });
        _entities.Add(new SubscriptionDefinition { Topic = "events", Name = "all" },
            new RuleDefinition { Name = "$Default", FilterKind = RuleFilterKind.True });
        _entities.Add(new SubscriptionDefinition { Topic = "events", Name = "high" },
            new RuleDefinition { Name = "p", SqlExpression = "priority = 'high'", ActionSqlExpression = "SET x = 1" },
            new RuleDefinition { Name = "c", FilterKind = RuleFilterKind.Correlation, Subject = "invoice", Properties = new Dictionary<string, string> { ["r"] = "eu" } });
    }

    [Fact]
    public async Task Exports_the_whole_namespace_with_rules()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("contoso", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["orders", "payments"], export.Queues.Select(q => q.Name));
        var topic = Assert.Single(export.Topics);
        Assert.Equal(["all", "high"], topic.Subscriptions.Select(s => s.Name));
        Assert.Equal(["p", "c"], topic.Subscriptions[1].Rules.Select(r => r.Name));
        Assert.Equal(5, export.EntityCount);
    }

    [Fact]
    public async Task Exporting_a_subscription_takes_its_topic_with_only_that_subscription()
    {
        var high = new EntityInfo { Kind = EntityKind.Subscription, Name = "high", Path = "events/Subscriptions/high" };

        var export = await new NamespaceTransfer(_entities).ExportAsync("contoso", [high], TestContext.Current.CancellationToken);

        Assert.Empty(export.Queues);
        Assert.Equal(["high"], Assert.Single(export.Topics).Subscriptions.Select(s => s.Name));
    }

    [Fact]
    public async Task Serialization_round_trips_including_never_and_enums()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("contoso", cancellationToken: TestContext.Current.CancellationToken);

        var json = NamespaceTransfer.Serialize(export);
        var back = NamespaceTransfer.Deserialize(json);

        Assert.Contains("\"FilterKind\": \"Correlation\"", json);
        Assert.Equal(TimeSpan.MaxValue, back.Queues[1].DefaultMessageTimeToLive);
        Assert.Equal(export.Queues, back.Queues);
        Assert.Equal("eu", back.Topics[0].Subscriptions[1].Rules[1].Properties["r"]);
    }

    [Fact]
    public void Export_files_keep_rule_value_types_and_parameters()
    {
        var definition = new NamespaceDefinition
        {
            Topics =
            [
                new TopicDefinition
                {
                    Name = "t",
                    Subscriptions =
                    [
                        new SubscriptionDefinition
                        {
                            Topic = "t",
                            Name = "s",
                            Rules =
                            [
                                new RuleDefinition
                                {
                                    Name = "typed",
                                    FilterKind = RuleFilterKind.Correlation,
                                    Properties = new Dictionary<string, string> { ["tier"] = "5" },
                                    PropertyTypes = new Dictionary<string, SbExplorer.Core.Messaging.PropertyValueType> { ["tier"] = SbExplorer.Core.Messaging.PropertyValueType.Int32 },
                                },
                                new RuleDefinition
                                {
                                    Name = "params",
                                    SqlExpression = "amount > @min",
                                    SqlParameters = new Dictionary<string, TypedValue> { ["@min"] = new("100", SbExplorer.Core.Messaging.PropertyValueType.Int64) },
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var back = NamespaceTransfer.Deserialize(NamespaceTransfer.Serialize(definition));

        var rules = back.Topics[0].Subscriptions[0].Rules;
        Assert.Equal(SbExplorer.Core.Messaging.PropertyValueType.Int32, rules[0].PropertyTypes["tier"]);
        Assert.Equal(new TypedValue("100", SbExplorer.Core.Messaging.PropertyValueType.Int64), rules[1].SqlParameters["@min"]);
        Assert.Equal(100L, rules[1].SqlParameters["@min"].ToObject());
    }

    [Fact]
    public async Task Import_creates_missing_entities_and_skips_existing_ones()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("source", cancellationToken: TestContext.Current.CancellationToken);
        var target = new InMemoryEntityService();
        target.Add(new QueueDefinition { Name = "orders", MaxDeliveryCount = 99 });

        var results = await new NamespaceTransfer(target).ImportAsync(export, overwrite: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ImportOutcome.Skipped, results.Single(r => r.Path == "orders").Outcome);
        Assert.Equal(99, target.Queues["orders"].MaxDeliveryCount);
        Assert.All(results.Where(r => r.Path != "orders"), r => Assert.Equal(ImportOutcome.Created, r.Outcome));
        Assert.Equal(["p", "c"], target.RulesOf("events", "high").Select(r => r.Name));
    }

    [Fact]
    public async Task Import_with_overwrite_updates_existing_entities_and_rules()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("source", cancellationToken: TestContext.Current.CancellationToken);
        var target = new InMemoryEntityService();
        target.Add(new QueueDefinition { Name = "orders", MaxDeliveryCount = 99 });
        target.Add(new TopicDefinition { Name = "events" });
        target.Add(new SubscriptionDefinition { Topic = "events", Name = "high" }, new RuleDefinition { Name = "p", SqlExpression = "old" });

        var results = await new NamespaceTransfer(target).ImportAsync(export, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ImportOutcome.Updated, results.Single(r => r.Path == "orders").Outcome);
        Assert.Equal(5, target.Queues["orders"].MaxDeliveryCount);
        Assert.Equal("priority = 'high'", target.RulesOf("events", "high").Single(r => r.Name == "p").SqlExpression);
        Assert.Contains(target.RulesOf("events", "high"), r => r.Name == "c");
    }

    [Fact]
    public async Task Overwriting_a_subscription_deletes_rules_the_file_does_not_have()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("source", cancellationToken: TestContext.Current.CancellationToken);
        var target = new InMemoryEntityService();
        target.Add(new TopicDefinition { Name = "events" });
        target.Add(new SubscriptionDefinition { Topic = "events", Name = "high" },
            new RuleDefinition { Name = RuleDefinition.DefaultName, FilterKind = RuleFilterKind.True });

        await new NamespaceTransfer(target).ImportAsync(export, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        // A leftover match-all $Default would make the filtered subscription receive every message.
        Assert.Equal(["p", "c"], target.RulesOf("events", "high").Select(r => r.Name));
    }

    [Fact]
    public async Task A_file_without_rules_leaves_the_subscription_rules_alone()
    {
        var target = new InMemoryEntityService();
        target.Add(new TopicDefinition { Name = "events" });
        target.Add(new SubscriptionDefinition { Topic = "events", Name = "s" }, new RuleDefinition { Name = "keep", SqlExpression = "a = 1" });
        var file = new NamespaceDefinition
        {
            Topics = [new TopicDefinition { Name = "events", Subscriptions = [new SubscriptionDefinition { Topic = "events", Name = "s", MaxDeliveryCount = 3 }] }],
        };

        await new NamespaceTransfer(target).ImportAsync(file, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["keep"], target.RulesOf("events", "s").Select(r => r.Name));
        Assert.Equal(3, target.Subscriptions["events/Subscriptions/s"].MaxDeliveryCount);
    }

    [Fact]
    public async Task Forwarding_to_an_entity_later_in_the_file_is_set_once_it_exists()
    {
        var file = new NamespaceDefinition
        {
            Queues =
            [
                new QueueDefinition { Name = "a-intake", ForwardTo = "events", ForwardDeadLetteredMessagesTo = "z-errors" },
                new QueueDefinition { Name = "z-errors" },
            ],
            Topics =
            [
                new TopicDefinition
                {
                    Name = "events",
                    Subscriptions = [new SubscriptionDefinition { Topic = "events", Name = "copy", ForwardTo = "z-errors" }],
                },
            ],
        };
        var target = new InMemoryEntityService();

        var results = await new NamespaceTransfer(target).ImportAsync(file, overwrite: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(ImportOutcome.Created, r.Outcome));
        Assert.Equal(4, results.Count);
        Assert.Equal(("events", "z-errors"), (target.Queues["a-intake"].ForwardTo, target.Queues["a-intake"].ForwardDeadLetteredMessagesTo));
        Assert.Equal("z-errors", target.Subscriptions["events/Subscriptions/copy"].ForwardTo);
    }

    [Fact]
    public async Task A_missing_forwarding_target_fails_that_entity_but_keeps_it_created()
    {
        var file = new NamespaceDefinition { Queues = [new QueueDefinition { Name = "orders", ForwardTo = "nowhere" }] };
        var target = new InMemoryEntityService();

        var result = Assert.Single(await new NamespaceTransfer(target).ImportAsync(file, overwrite: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ImportOutcome.Failed, result.Outcome);
        Assert.StartsWith("Created, but forwarding could not be set", result.Error);
        Assert.Null(target.Queues["orders"].ForwardTo);
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_import()
    {
        var export = await new NamespaceTransfer(_entities).ExportAsync("source", cancellationToken: TestContext.Current.CancellationToken);
        var target = new InMemoryEntityService { FailOn = "payments" };

        var progress = new List<ImportItemResult>();
        var results = await new NamespaceTransfer(target).ImportAsync(export, false, new SyncProgress(progress.Add), TestContext.Current.CancellationToken);

        var failed = results.Single(r => r.Outcome == ImportOutcome.Failed);
        Assert.Equal(("payments", "quota exceeded"), (failed.Path, failed.Error));
        Assert.True(target.Topics.ContainsKey("events"));
        Assert.Equal(results.Count, progress.Count);
    }

    private sealed class SyncProgress(Action<ImportItemResult> report) : IProgress<ImportItemResult>
    {
        public void Report(ImportItemResult value) => report(value);
    }
}

/// <summary>A tiny namespace that stores definitions; listing returns EntityInfo built from them.</summary>
internal sealed class InMemoryEntityService : IEntityService
{
    public Dictionary<string, QueueDefinition> Queues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TopicDefinition> Topics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SubscriptionDefinition> Subscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<RuleDefinition>> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? FailOn { get; init; }

    public void Add(QueueDefinition queue) => Queues[queue.Name] = queue;
    public void Add(TopicDefinition topic) => Topics[topic.Name] = topic with { Subscriptions = [] };

    public void Add(SubscriptionDefinition subscription, params RuleDefinition[] rules)
    {
        var key = EntityInfo.SubscriptionPath(subscription.Topic, subscription.Name);
        Subscriptions[key] = subscription with { Rules = [] };
        Rules[key] = [.. rules];
    }

    public IReadOnlyList<RuleDefinition> RulesOf(string topic, string subscription) => Rules[EntityInfo.SubscriptionPath(topic, subscription)];

    public Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        List(Queues.Keys.Select(n => Info(EntityKind.Queue, n, n)));

    public Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default) =>
        List(Topics.Keys.Select(n => Info(EntityKind.Topic, n, n)));

    public Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default) =>
        List(Subscriptions.Values.Where(s => s.Topic == topic).Select(s => Info(EntityKind.Subscription, s.Name, EntityInfo.SubscriptionPath(topic, s.Name))));

    public Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default) =>
        List(RulesOf(topic, subscription).Select(r => Info(EntityKind.Rule, r.Name, EntityInfo.RulePath(topic, subscription, r.Name))));

    public Task<EntityInfo> GetEntityAsync(EntityInfo reference, CancellationToken cancellationToken = default) => Task.FromResult(reference);

    public Task<QueueDefinition> GetQueueAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Queues[name]);
    public Task<TopicDefinition> GetTopicAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Topics[name]);

    public Task<SubscriptionDefinition> GetSubscriptionAsync(string topic, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscriptions[EntityInfo.SubscriptionPath(topic, name)]);

    public Task<RuleDefinition> GetRuleAsync(string topic, string subscription, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(RulesOf(topic, subscription).Single(r => r.Name == name));

    public Task CreateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) =>
        Do(queue.Name, () => Add(RequireTargets(queue, queue.ForwardTo, queue.ForwardDeadLetteredMessagesTo)));
    public Task CreateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) => Do(topic.Name, () => Add(topic));

    public Task CreateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        Do(subscription.Name, () => Add(RequireTargets(subscription, subscription.ForwardTo, subscription.ForwardDeadLetteredMessagesTo), subscription.Rules.Count > 0
            ? [.. subscription.Rules]
            : [new RuleDefinition { Name = RuleDefinition.DefaultName, FilterKind = RuleFilterKind.True }]));

    public Task CreateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) =>
        Do(rule.Name, () => Rules[EntityInfo.SubscriptionPath(topic, subscription)].Add(rule));

    public Task UpdateQueueAsync(QueueDefinition queue, CancellationToken cancellationToken = default) =>
        Do(queue.Name, () => Add(RequireTargets(queue, queue.ForwardTo, queue.ForwardDeadLetteredMessagesTo)));
    public Task UpdateTopicAsync(TopicDefinition topic, CancellationToken cancellationToken = default) => Do(topic.Name, () => Add(topic));

    public Task UpdateSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        Do(subscription.Name, () => Subscriptions[EntityInfo.SubscriptionPath(subscription.Topic, subscription.Name)] =
            RequireTargets(subscription, subscription.ForwardTo, subscription.ForwardDeadLetteredMessagesTo) with { Rules = [] });

    public Task UpdateRuleAsync(string topic, string subscription, RuleDefinition rule, CancellationToken cancellationToken = default) =>
        Do(rule.Name, () =>
        {
            var rules = Rules[EntityInfo.SubscriptionPath(topic, subscription)];
            rules[rules.FindIndex(r => r.Name == rule.Name)] = rule;
        });

    public Task DeleteAsync(EntityInfo entity, CancellationToken cancellationToken = default)
    {
        Assert.Equal(EntityKind.Rule, entity.Kind);
        var parts = entity.Path.Split('/');
        Rules[EntityInfo.SubscriptionPath(parts[0], parts[2])].RemoveAll(r => r.Name == entity.Name);
        return Task.CompletedTask;
    }
    public Task SetStatusAsync(EntityInfo entity, EntityStatusValue status, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    // Like Service Bus, forwarding to an entity that doesn't exist is refused.
    private T RequireTargets<T>(T entity, params string?[] targets)
    {
        foreach (var target in targets.Where(t => t is not null))
        {
            if (!Queues.ContainsKey(target!) && !Topics.ContainsKey(target!))
            {
                throw new InvalidOperationException($"The forwarding target {target} doesn't exist.");
            }
        }

        return entity;
    }

    private Task Do(string name, Action action)
    {
        if (string.Equals(name, FailOn, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromException(new InvalidOperationException("quota exceeded\nTrackingId: 123"));
        }

        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromException(ex);
        }
    }

    private static EntityInfo Info(EntityKind kind, string name, string path) => new() { Kind = kind, Name = name, Path = path };

    private static Task<IReadOnlyList<EntityInfo>> List(IEnumerable<EntityInfo> items) => Task.FromResult<IReadOnlyList<EntityInfo>>(items.ToList());
}
