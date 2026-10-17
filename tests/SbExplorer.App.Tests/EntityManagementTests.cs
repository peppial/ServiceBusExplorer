using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using Xunit;

namespace SbExplorer.App.Tests;

public class EntityManagementTests
{
    private readonly FakeEntityService _entities = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly ExplorerViewModel _vm;

    public EntityManagementTests()
    {
        _entities.Queues.Add(FakeEntityService.Queue("orders"));
        _entities.Definitions["orders"] = new QueueDefinition { Name = "orders" };
        _entities.Topics.Add(FakeEntityService.Topic("events"));
        _entities.Definitions["events"] = new TopicDefinition { Name = "events" };
        _entities.Subscriptions["events"] = [FakeEntityService.Subscription("events", "all")];
        _entities.Definitions["events/Subscriptions/all"] = new SubscriptionDefinition { Topic = "events", Name = "all" };
        _entities.Rules["events/Subscriptions/all"] = [FakeEntityService.Rule("events", "all", "$Default")];
        _vm = new ExplorerViewModel(new FakeSession(new ConnectionProfile { Name = "x" }, "contoso.servicebus.windows.net", _entities), _dialogs);
    }

    private TreeNodeViewModel Node(string name) => _vm.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    [Fact]
    public async Task Creating_a_subscription_refreshes_expands_its_topic_and_selects_it()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _vm.SelectedNode = Node("events");
        Assert.True(_vm.NewSubscriptionCommand.CanExecute(null));
        Assert.False(_vm.NewRuleCommand.CanExecute(null));
        _vm.NewSubscriptionCommand.Execute(null);
        Assert.Equal("Create Subscription", _vm.DetailsTitle);
        Assert.False(_vm.ShowsDetails);
        _vm.Creating!.Name = "eu";
        await _vm.CommitCreateCommand.ExecuteAsync(null);

        Assert.Null(_vm.Creating);
        Assert.Equal("events/Subscriptions/eu", _vm.SelectedEntity?.Path);
        Assert.True(Node("events").IsExpanded);
        Assert.Equal("The subscription events/Subscriptions/eu has been successfully created.", _vm.LoadStatus);
    }

    [Fact]
    public async Task Delete_requires_typing_the_name()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _vm.SelectedNode = Node("orders");

        _dialogs.PromptResult = "order";
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(_entities.Calls);
        Assert.True(_vm.HasError);

        _dialogs.PromptResult = "orders";
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("delete orders", _entities.Calls.Single());
        Assert.DoesNotContain(_vm.Roots[0].Children, c => c.Name == "orders");
        Assert.Null(_vm.SelectedEntity);
    }

    [Fact]
    public async Task Rules_are_deleted_with_a_plain_confirmation_and_have_no_status()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        await ((EntityNodeViewModel)Node("events")).EnsureLoadedAsync(TestContext.Current.CancellationToken);
        var all = (EntityNodeViewModel)Node("all");
        await all.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        _vm.SelectedNode = Node("$Default");

        Assert.False(_vm.SetStatusCommand.CanExecute(EntityStatusValue.Disabled));
        Assert.True(_vm.NewRuleCommand.CanExecute(null));

        _dialogs.ConfirmResult = true;
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("delete events/Subscriptions/all/Rules/$Default", _entities.Calls.Single());
    }

    [Fact]
    public async Task Status_changes_reload_and_keep_the_selection()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _vm.SelectedNode = Node("orders");

        await _vm.SetStatusCommand.ExecuteAsync(EntityStatusValue.Disabled);

        Assert.Equal("status orders Disabled", _entities.Calls.Single());
        Assert.Equal("orders", _vm.SelectedEntity?.Name);
        Assert.Equal("QueueDisabled", _vm.SelectedNode!.IconKey);
    }

    [Fact]
    public async Task Export_of_the_selection_saves_a_json_file()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _vm.SelectedNode = Node("orders");
        _dialogs.ChooseResult = 0;

        await _vm.ExportCommand.ExecuteAsync(null);

        var file = _dialogs.SavedFiles.Single();
        Assert.Equal("orders-entities.json", file.Name);
        var definition = NamespaceTransfer.Deserialize(System.Text.Encoding.UTF8.GetString(file.Content));
        Assert.Equal("orders", Assert.Single(definition.Queues).Name);
        Assert.Empty(definition.Topics);
    }

    [Fact]
    public async Task Import_reports_created_and_skipped_entities()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _dialogs.OpenFileText = NamespaceTransfer.Serialize(new NamespaceDefinition
        {
            Queues = [new QueueDefinition { Name = "orders" }, new QueueDefinition { Name = "returns" }],
        });
        _dialogs.ChooseResult = 0;

        await _vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["create queue returns"], _entities.Calls);
        Assert.Equal("Import finished: 1 skipped, 1 created.", _vm.LoadStatus);
        Assert.Contains(_vm.Roots[0].Children, c => c.Name == "returns");
    }

    [Fact]
    public async Task Import_rejects_files_that_are_not_exports()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _dialogs.OpenFileText = "not json";

        await _vm.ImportCommand.ExecuteAsync(null);

        Assert.True(_vm.HasError);
        Assert.StartsWith("That file isn't an entity export", _vm.LoadStatus);
    }

    [Fact]
    public void Management_commands_are_off_without_the_manage_claim()
    {
        var limited = new ExplorerViewModel(
            new FakeSession(new ConnectionProfile { Name = "x" }, "x") { CanManage = false }, _dialogs);

        Assert.False(limited.NewQueueCommand.CanExecute(null));
        Assert.False(limited.ImportCommand.CanExecute(null));
        Assert.False(limited.CanManage);
    }
}
