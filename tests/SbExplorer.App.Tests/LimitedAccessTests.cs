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
        Assert.Equal("No management access · 2 entities added by name", vm.LoadStatus);
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
        await store.SaveAsync(new ConnectionProfile { Name = "Listen only" },
            "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=listen;SharedAccessKey=v", TestContext.Current.CancellationToken);
        var dialogs = new FakeDialogs { PromptResult = "orders" };
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector { CanManage = false }, dialogs);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedConnection = vm.Connections[0];

        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.Explorer!.AddEntityCommand.ExecuteAsync(null);

        Assert.Contains("without management access", vm.Status);
        Assert.Equal(["orders"], store.Profiles.Single().PinnedEntities);
        Assert.Equal(["orders"], vm.Connections[0].Profile.PinnedEntities);
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
}
