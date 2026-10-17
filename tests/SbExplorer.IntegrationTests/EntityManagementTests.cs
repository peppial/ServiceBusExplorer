using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

/// <summary>Creates real entities with unique names on the emulator and always removes them again.</summary>
public sealed class EntityManagementTests : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<EntityInfo> _cleanup = [];
    private INamespaceSession? _session;

    private IEntityService Entities => _session!.Entities;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Emulator.SkipIfNotRunning();
        _session = await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "Emulator" }, Emulator.ConnectionString, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is null)
        {
            return;
        }

        foreach (var entity in _cleanup.AsEnumerable().Reverse())
        {
            try
            {
                await Entities.DeleteAsync(entity);
            }
            catch (Exception)
            {
                // Already deleted by the test.
            }
        }

        await _session.DisposeAsync();
    }

    private EntityInfo Track(EntityKind kind, string name, string? path = null)
    {
        var entity = new EntityInfo { Kind = kind, Name = name, Path = path ?? name };
        _cleanup.Add(entity);
        return entity;
    }

    [Fact]
    public async Task Queue_create_read_update_disable_delete()
    {
        var name = $"crud-q-{_suffix}";
        var entity = Track(EntityKind.Queue, name);

        await Entities.CreateQueueAsync(new QueueDefinition
        {
            Name = name,
            LockDuration = TimeSpan.FromSeconds(45),
            MaxDeliveryCount = 4,
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(30),
            RequiresSession = true,
        }, Ct);

        var created = await Entities.GetQueueAsync(name, Ct);
        Assert.Equal((TimeSpan.FromSeconds(45), 4, TimeSpan.FromMinutes(30), true),
            (created.LockDuration, created.MaxDeliveryCount, created.DefaultMessageTimeToLive, created.RequiresSession));

        // UserMetadata is sent too, but the emulator doesn't store it, so only the delivery count is checked.
        await Entities.UpdateQueueAsync(created with { MaxDeliveryCount = 9, UserMetadata = "owner: team-a" }, Ct);
        Assert.Equal(9, (await Entities.GetQueueAsync(name, Ct)).MaxDeliveryCount);

        await Entities.SetStatusAsync(entity, EntityStatusValue.Disabled, Ct);
        Assert.Equal(EntityStatusValue.Disabled, (await Entities.GetQueueAsync(name, Ct)).Status);

        await Entities.DeleteAsync(entity, Ct);
        Assert.DoesNotContain(await Entities.GetQueuesAsync(Ct), q => q.Name == name);
    }

    [Fact]
    public async Task Subscription_with_filter_and_extra_rules()
    {
        var topic = $"crud-t-{_suffix}";
        Track(EntityKind.Topic, topic);
        await Entities.CreateTopicAsync(new TopicDefinition { Name = topic, DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);

        await Entities.CreateSubscriptionAsync(new SubscriptionDefinition
        {
            Topic = topic,
            Name = "eu",
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(30),
            Rules = [new RuleDefinition { Name = "eu-only", SqlExpression = "region = 'eu'", ActionSqlExpression = "SET routed = 'true'" }],
        }, Ct);
        await Entities.CreateRuleAsync(topic, "eu", new RuleDefinition
        {
            Name = "invoices",
            FilterKind = RuleFilterKind.Correlation,
            Subject = "invoice",
            Properties = new Dictionary<string, string> { ["tier"] = "gold" },
        }, Ct);

        var rules = await Entities.GetRulesAsync(topic, "eu", Ct);
        Assert.Equal(["eu-only", "invoices"], rules.Select(r => r.Name));

        var sql = await Entities.GetRuleAsync(topic, "eu", "eu-only", Ct);
        Assert.Equal(("region = 'eu'", "SET routed = 'true'"), (sql.SqlExpression, sql.ActionSqlExpression));
        var correlation = await Entities.GetRuleAsync(topic, "eu", "invoices", Ct);
        Assert.Equal(("invoice", "gold"), (correlation.Subject, correlation.Properties["tier"]));

        await Entities.UpdateRuleAsync(topic, "eu", sql with { SqlExpression = "region IN ('eu', 'uk')" }, Ct);
        Assert.Equal("region IN ('eu', 'uk')", (await Entities.GetRuleAsync(topic, "eu", "eu-only", Ct)).SqlExpression);

        await Entities.DeleteAsync(new EntityInfo { Kind = EntityKind.Rule, Name = "invoices", Path = EntityInfo.RulePath(topic, "eu", "invoices") }, Ct);
        Assert.Single(await Entities.GetRulesAsync(topic, "eu", Ct));
    }

    [Fact]
    public async Task Export_then_import_recreates_deleted_entities()
    {
        var topic = $"crud-x-{_suffix}";
        var queue = $"crud-xq-{_suffix}";
        Track(EntityKind.Queue, queue);
        var topicEntity = Track(EntityKind.Topic, topic);
        await Entities.CreateQueueAsync(new QueueDefinition { Name = queue, MaxDeliveryCount = 6, DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        await Entities.CreateTopicAsync(new TopicDefinition { Name = topic, DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        await Entities.CreateSubscriptionAsync(new SubscriptionDefinition
        {
            Topic = topic,
            Name = "s",
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(30),
            Rules = [new RuleDefinition { Name = "high", SqlExpression = "priority = 'high'" }],
        }, Ct);

        var transfer = new NamespaceTransfer(Entities);
        var selection = new[] { new EntityInfo { Kind = EntityKind.Queue, Name = queue, Path = queue }, topicEntity };
        var json = NamespaceTransfer.Serialize(await transfer.ExportAsync("emulator", selection, Ct));

        await Entities.DeleteAsync(topicEntity, Ct);
        var results = await transfer.ImportAsync(NamespaceTransfer.Deserialize(json), overwrite: false, cancellationToken: Ct);

        Assert.Equal(ImportOutcome.Skipped, results.Single(r => r.Path == queue).Outcome);
        Assert.Equal(ImportOutcome.Created, results.Single(r => r.Path == topic).Outcome);
        Assert.Equal(ImportOutcome.Created, results.Single(r => r.Path == EntityInfo.SubscriptionPath(topic, "s")).Outcome);
        Assert.Equal("priority = 'high'", (await Entities.GetRuleAsync(topic, "s", "high", Ct)).SqlExpression);
    }
}
