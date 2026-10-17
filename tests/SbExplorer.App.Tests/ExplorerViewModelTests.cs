using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using Xunit;
using static SbExplorer.App.Tests.FakeEntityService;

namespace SbExplorer.App.Tests;

public class ExplorerViewModelTests
{
    private readonly FakeEntityService _entities = new();
    private readonly ExplorerViewModel _vm;

    public ExplorerViewModelTests()
    {
        _entities.Queues.AddRange([Queue("orders", active: 16, deadLetter: 3), Queue("payments", active: 2)]);
        _entities.Topics.Add(Topic("events"));
        _entities.Subscriptions["events"] = [Subscription("events", "all-events", 4), Subscription("events", "billing")];
        _entities.Rules["events/Subscriptions/billing"] = [Rule("events", "billing", "invoices")];

        var session = new FakeSession(new ConnectionProfile { Name = "Dev" }, "dev.servicebus.windows.net", _entities);
        _vm = new ExplorerViewModel(session, new FakeDialogs());
    }

    private FolderNodeViewModel QueuesFolder => _vm.Roots[0];
    private FolderNodeViewModel TopicsFolder => _vm.Roots[1];
    private EntityNodeViewModel EventsTopic => (EntityNodeViewModel)TopicsFolder.Children.Single();

    [Fact]
    public async Task Refresh_loads_queues_and_topics_with_counts()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Queues (2)", QueuesFolder.Name);
        Assert.Equal("Topics (1)", TopicsFolder.Name);
        Assert.True(QueuesFolder.IsExpanded);
        var orders = QueuesFolder.Children[0];
        Assert.Equal(("orders", "16", "3 DLQ"), (orders.Name, orders.ActiveBadge, orders.DeadLetterBadge));
        Assert.Null(QueuesFolder.Children[1].DeadLetterBadge);
        Assert.StartsWith("2 queues, 1 topic", _vm.LoadStatus);
        Assert.DoesNotContain("emulator", _vm.LoadStatus);
    }

    [Fact]
    public async Task Empty_entities_have_no_badges()
    {
        _entities.Queues.Add(Queue("empty"));

        await _vm.RefreshCommand.ExecuteAsync(null);

        var empty = QueuesFolder.Children.Single(c => c.Name == "empty");
        Assert.Null(empty.ActiveBadge);
        Assert.Null(empty.DeadLetterBadge);
    }

    [Fact]
    public async Task Emulator_sessions_explain_missing_counts()
    {
        var session = new FakeSession(new ConnectionProfile { Name = "Emu" }, "localhost", _entities, isEmulator: true);
        var vm = new ExplorerViewModel(session, new FakeDialogs());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.EndsWith("the emulator doesn't report message counts", vm.LoadStatus);
    }

    [Fact]
    public async Task Subscriptions_and_rules_load_only_when_expanded()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(0, _entities.SubscriptionLoads);
        Assert.IsType<MessageNodeViewModel>(Assert.Single(EventsTopic.Children));

        EventsTopic.IsExpanded = true;
        await EventsTopic.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["all-events", "billing"], EventsTopic.Children.Select(c => c.Name));

        var billing = (EntityNodeViewModel)EventsTopic.Children[1];
        billing.IsExpanded = true;
        await billing.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Equal("invoices", Assert.Single(billing.Children).Name);

        EventsTopic.IsExpanded = false;
        EventsTopic.IsExpanded = true;
        Assert.Equal(1, _entities.SubscriptionLoads);
    }

    [Fact]
    public async Task Failed_subscription_load_shows_error_node()
    {
        _entities.SubscriptionFailure = new InvalidOperationException("Forbidden");
        await _vm.RefreshCommand.ExecuteAsync(null);

        await EventsTopic.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Could not load: Forbidden", Assert.Single(EventsTopic.Children).Name);
        Assert.False(EventsTopic.IsLoaded);
    }

    [Fact]
    public async Task Filter_matches_queue_topic_and_loaded_subscription_names()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        await EventsTopic.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        _vm.Filter = "ORD";
        Assert.Equal([true, false], QueuesFolder.Children.Select(c => c.IsVisible));
        Assert.False(EventsTopic.IsVisible);

        _vm.Filter = "bill";
        Assert.True(EventsTopic.IsVisible);
        Assert.Equal([false, true], EventsTopic.Children.Select(c => c.IsVisible));

        _vm.Filter = "events";
        Assert.All(EventsTopic.Children, c => Assert.True(c.IsVisible));

        _vm.Filter = "";
        Assert.All(QueuesFolder.Children, c => Assert.True(c.IsVisible));
    }

    [Fact]
    public async Task Refresh_keeps_expanded_nodes_and_selection_with_fresh_data()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        EventsTopic.IsExpanded = true;
        await EventsTopic.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        _vm.SelectedNode = EventsTopic.Children[0];

        _entities.Subscriptions["events"][0] = Subscription("events", "all-events", 99);
        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(EventsTopic.IsExpanded);
        Assert.Equal("all-events", _vm.SelectedEntity?.Name);
        Assert.Equal(99, _vm.SelectedEntity?.Counts?.Active);
    }

    [Fact]
    public async Task Selecting_an_entity_groups_its_properties()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        _vm.SelectedNode = QueuesFolder.Children[0];

        Assert.True(_vm.HasSelection);
        var group = Assert.Single(_vm.PropertyGroups);
        Assert.Equal("Runtime", group.Name);
    }

    [Fact]
    public async Task Load_failure_is_reported()
    {
        _entities.Failure = new InvalidOperationException("Unauthorized");

        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(_vm.HasError);
        Assert.Equal("Could not load entities: Unauthorized", _vm.LoadStatus);
    }

    [Fact]
    public async Task Disabled_entities_are_dimmed()
    {
        _entities.Queues.Add(Queue("old") with { Status = "Disabled" });

        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(QueuesFolder.Children.Single(c => c.Name == "old").IsDimmed);
        Assert.False(QueuesFolder.Children.Single(c => c.Name == "orders").IsDimmed);
    }
}
