using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

/// <summary>Runs against the entities defined in docker/emulator-config.json.</summary>
public class EntityServiceTests
{
    private static async Task<INamespaceSession> ConnectAsync()
    {
        Emulator.SkipIfNotRunning();
        return await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "Emulator" }, Emulator.ConnectionString, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Lists_queues_with_settings()
    {
        await using var session = await ConnectAsync();

        var queues = await session.Entities.GetQueuesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(queues, q => q.Name == "orders" && !q.RequiresSession && q.Counts is not null);
        Assert.Contains(queues, q => q.Name == "orders-sessions" && q.RequiresSession);
        Assert.Contains(queues.Single(q => q.Name == "payments").Properties, p => p is { Name: "Duplicate detection", Value: "5 min" });
    }

    [Fact]
    public async Task Lists_subscriptions_and_rules()
    {
        await using var session = await ConnectAsync();
        var ct = TestContext.Current.CancellationToken;

        var topics = await session.Entities.GetTopicsAsync(ct);
        var subscriptions = await session.Entities.GetSubscriptionsAsync("events", ct);
        var rules = await session.Entities.GetRulesAsync("events", "high-priority", ct);

        Assert.Contains(topics, t => t.Name == "events");
        Assert.Equal(["all-events", "billing", "high-priority"], subscriptions.Select(s => s.Name));
        Assert.Equal("audit", subscriptions.Single(s => s.Name == "billing").ForwardTo);
        Assert.Contains(rules.Single(r => r.Name == "priority-high").Properties, p => p is { Name: "Expression", Value: "priority = 'high'" });
    }
}
