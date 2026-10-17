using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using Xunit;

namespace SbExplorer.App.Tests;

/// <summary>Credentials without the Manage claim: entities are added by name instead of listed.</summary>
public class LimitedAccessTests
{
    private readonly FakeDialogs _dialogs = new();
    private readonly List<IReadOnlyList<string>> _saved = [];

    private ExplorerViewModel Create(string? scoped = null, params string[] pinned)
    {
        var profile = new ConnectionProfile { Name = "Listen only", PinnedEntities = pinned };
        var session = new FakeSession(profile, "contoso.servicebus.windows.net") { CanManage = false, ScopedEntity = scoped };
        session.FakeEntities.Failure = new InvalidOperationException("must not call the management API");
        return new ExplorerViewModel(session, _dialogs, savePinnedEntities: list =>
        {
            _saved.Add(list);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Shows_scoped_and_pinned_entities_without_calling_management()
    {
        var vm = Create("orders", "events/Subscriptions/billing");

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.IsLimited);
        var folder = Assert.Single(vm.Roots);
        Assert.Equal(["orders", "billing"], folder.Children.Select(c => c.Name));
        Assert.Equal(EntityKind.Subscription, folder.Children[1].Entity!.Kind);
        Assert.False(vm.HasError);
        Assert.Equal("No namespace management access · 2 entities added by name", vm.LoadStatus);
    }

    [Fact]
    public async Task Added_entities_are_saved_and_selected_with_a_messages_tab()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        _dialogs.PromptResult = "events/all-events";

        await vm.AddEntityCommand.ExecuteAsync(null);

        Assert.Equal(["events/Subscriptions/all-events"], _saved.Single());
        Assert.Equal("all-events", vm.SelectedEntity?.Name);
        Assert.NotNull(vm.Messages);
        Assert.Equal("events", vm.Messages!.Source.Topic);
    }

    [Fact]
    public async Task Invalid_names_are_reported_and_duplicates_ignored()
    {
        var vm = Create("orders");
        await vm.RefreshCommand.ExecuteAsync(null);

        _dialogs.PromptResult = "a/b/c";
        await vm.AddEntityCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);

        _dialogs.PromptResult = "ORDERS";
        await vm.AddEntityCommand.ExecuteAsync(null);
        Assert.Empty(_saved);
        Assert.Single(vm.Roots[0].Children);
    }

    [Fact]
    public async Task Pinned_entities_can_be_removed_but_the_scoped_one_cannot()
    {
        var vm = Create("orders", "payments");
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.SelectedNode = vm.Roots[0].Children[0];
        Assert.False(vm.RemoveEntityCommand.CanExecute(null));

        vm.SelectedNode = vm.Roots[0].Children[1];
        await vm.RemoveEntityCommand.ExecuteAsync(null);

        Assert.Empty(_saved.Single());
        Assert.Equal(["orders"], vm.Roots[0].Children.Select(c => c.Name));
    }

    [Fact]
    public async Task Managed_sessions_hide_the_add_entity_controls()
    {
        var session = new FakeSession(new ConnectionProfile { Name = "Full" }, "x");
        var vm = new ExplorerViewModel(session, _dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsLimited);
        Assert.Null(vm.LimitedNotice);
        Assert.Equal(2, vm.Roots.Count);
    }

    [Fact]
    public async Task Main_window_persists_pinned_entities_on_the_connection()
    {
        var secrets = new FakeSecretStore();
        var store = new FakeConnectionStore(secrets);
        var profile = new ConnectionProfile { Name = "Listen only" };
        await store.SaveAsync(profile, "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=listen;SharedAccessKey=v", TestContext.Current.CancellationToken);
        var dialogs = new FakeDialogs { PromptResult = "orders" };
        var log = new SbExplorer.App.Services.LogService();
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector { CanManage = false }, dialogs, log);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ConnectSavedCommand.ExecuteAsync(profile);
        await vm.Explorer!.AddEntityCommand.ExecuteAsync(null);

        Assert.Contains(log.Entries, e => e.Message.Contains("the namespace can't be listed with this credential"));
        Assert.Equal(["orders"], store.Profiles.Single().PinnedEntities);
        Assert.Equal(["orders"], vm.SavedConnections.Single().PinnedEntities);
    }

    [Fact]
    public void Editing_a_connection_keeps_its_pinned_entities()
    {
        var existing = new ConnectionProfile { Name = "x", PinnedEntities = ["orders"] };

        var editor = new ConnectionEditorViewModel(new FakeConnector(), existing, "Endpoint=sb://x/;SharedAccessKeyName=k;SharedAccessKey=v")
        {
            Name = "renamed",
        };

        Assert.Equal(["orders"], editor.ToProfile().PinnedEntities);
    }

    [Fact]
    public async Task Test_connection_explains_limited_access()
    {
        var editor = new ConnectionEditorViewModel(new FakeConnector { CanManage = false })
        {
            Name = "x",
            ConnectionString = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=listen;SharedAccessKey=v",
        };

        await editor.TestCommand.ExecuteAsync(null);

        Assert.True(editor.TestSucceeded);
        Assert.Contains("without management access", editor.TestResult);
    }

    [Fact]
    public async Task A_key_scoped_to_one_queue_still_shows_and_edits_that_queue()
    {
        var entities = new FakeEntityService { Failure = new UnauthorizedAccessException("must not list") };
        entities.Readable["test"] = FakeEntityService.Queue("test", active: 7, deadLetter: 1) with { MaxDeliveryCount = 4 };
        entities.Definitions["test"] = new QueueDefinition { Name = "test", MaxDeliveryCount = 4 };
        var session = new FakeSession(new ConnectionProfile { Name = "scoped" }, "contoso.servicebus.windows.net", entities)
        {
            CanManage = false,
            ScopedEntity = "test",
            LimitedReason = "Unauthorized access. 'Manage' claim(s) are required for this operation.",
        };
        var vm = new ExplorerViewModel(session, _dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("is for 'test' only (it has EntityPath=test)", vm.LimitedNotice);
        Assert.Contains("Service Bus said: Unauthorized access.", vm.LimitedNotice);
        var node = vm.Roots[0].Children.Single();
        Assert.Equal("test (7, 1)", node.DisplayName);

        vm.SelectedNode = node;
        var panel = vm.Details!;
        await panel.EditorLoad;
        Assert.True(panel.EntityManageable);
        Assert.True(panel.CanModify);
        Assert.False(panel.HasStatus);
        panel.Editor!.MaxDeliveryCount = 6;
        Assert.True(panel.UpdateCommand.CanExecute(null));
        await panel.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(6, ((QueueDefinition)entities.Definitions["test"]).MaxDeliveryCount);
    }

    [Fact]
    public async Task Entities_the_key_cannot_read_stay_as_plain_references()
    {
        var vm = Create("orders");

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedNode = vm.Roots[0].Children.Single();
        await vm.Details!.EditorLoad;

        Assert.Equal("orders", vm.Roots[0].Children.Single().DisplayName);
        Assert.False(vm.Details.CanModify);
        Assert.False(vm.Details.CanEditSettings);
        Assert.Null(vm.Details.EditorError);
    }
}
