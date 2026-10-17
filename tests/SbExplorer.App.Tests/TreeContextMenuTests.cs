using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using Xunit;

namespace SbExplorer.App.Tests;

public class TreeContextMenuTests
{
    private readonly FakeEntityService _entities = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly ExplorerViewModel _explorer;

    public TreeContextMenuTests()
    {
        _entities.Queues.Add(FakeEntityService.Queue("orders"));
        _entities.Queues.Add(FakeEntityService.Queue("orders-sessions") with { RequiresSession = true });
        _entities.Topics.Add(FakeEntityService.Topic("events"));
        _entities.Subscriptions["events"] = [FakeEntityService.Subscription("events", "all")];
        _entities.Rules["events/Subscriptions/all"] = [FakeEntityService.Rule("events", "all", "$Default")];
        _explorer = new ExplorerViewModel(new FakeSession(new ConnectionProfile { Name = "x" }, "contoso.servicebus.windows.net", _entities), _dialogs);
    }

    private TreeNodeViewModel Node(string name) =>
        _explorer.TreeRoots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    private List<string> Headers(TreeNodeViewModel node)
    {
        _explorer.SelectedNode = node;
        return EntityTreeView.MenuFor(_explorer, node).OfType<MenuItem>().Select(m => (string)m.Header!).ToList();
    }

    [AvaloniaFact]
    public async Task Each_kind_of_node_gets_the_original_menu()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["Refresh Entities", "Export Entities", "Import Entities", "Copy Namespace URL", "Expand Subtree", "Collapse Subtree"],
            Headers(Node("sb://contoso.servicebus.windows.net/")));
        Assert.StartsWith("Create Queue", Headers(_explorer.Roots[0])[0]);
        Assert.StartsWith("Create Topic", Headers(_explorer.Roots[1])[0]);

        var queue = Headers(Node("orders"));
        Assert.Contains("Copy Deadletter Queue URL", queue);
        Assert.Contains("Receive Dead-letter Queue Messages", queue);
        Assert.Contains("Purge Messages", queue);
        Assert.DoesNotContain("Get Message Sessions", queue);
        Assert.Contains("Get Message Sessions", Headers(Node("orders-sessions")));

        Assert.Contains("Create Subscription", Headers(Node("events")));
        await ((EntityNodeViewModel)Node("events")).EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Add Rule", Headers(Node("all")));
        await ((EntityNodeViewModel)Node("all")).EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Remove Rule"], Headers(Node("$Default")));
    }

    [AvaloniaFact]
    public async Task Copy_url_uses_the_namespace_entity_or_dead_letter_queue()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        _explorer.SelectedNode = Node("orders");

        await _explorer.CopyUrlCommand.ExecuteAsync("deadletter");
        Assert.Equal("sb://contoso.servicebus.windows.net/orders/$DeadLetterQueue", _dialogs.CopiedText);

        await _explorer.CopyUrlCommand.ExecuteAsync("namespace");
        Assert.Equal("sb://contoso.servicebus.windows.net/", _dialogs.CopiedText);
    }

    [AvaloniaFact]
    public async Task Expand_subtree_loads_and_opens_everything_below()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        _explorer.SelectedNode = _explorer.Roots[1];

        await _explorer.ExpandSubtreeCommand.ExecuteAsync(null);

        Assert.True(Node("events").IsExpanded);
        Assert.True(Node("all").IsExpanded);
        Assert.Equal("$Default", Assert.Single(Node("all").Children).Name);

        _explorer.CollapseSubtreeCommand.Execute(null);
        Assert.False(Node("events").IsExpanded);
    }

    [AvaloniaFact]
    public async Task Export_queues_exports_only_that_folder()
    {
        _entities.Definitions["orders"] = new QueueDefinition { Name = "orders" };
        _entities.Definitions["orders-sessions"] = new QueueDefinition { Name = "orders-sessions", RequiresSession = true };
        await _explorer.RefreshCommand.ExecuteAsync(null);
        _explorer.SelectedNode = _explorer.Roots[0];

        await _explorer.ExportSelectedCommand.ExecuteAsync(null);

        var file = _dialogs.SavedFiles.Single();
        Assert.Equal("contoso-queues-entities.json", file.Name);
        var definition = NamespaceTransfer.Deserialize(System.Text.Encoding.UTF8.GetString(file.Content));
        Assert.Equal(["orders", "orders-sessions"], definition.Queues.Select(q => q.Name));
        Assert.Empty(definition.Topics);
    }

    [AvaloniaFact]
    public async Task Right_clicking_a_node_selects_it_and_opens_its_menu()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        var tree = new EntityTreeView { DataContext = _explorer };
        var window = new Window { Content = tree, Width = 400, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().First(i => i.DataContext is EntityNodeViewModel { Name: "orders" });
        var point = item.TranslatePoint(new Point(40, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("orders", _explorer.SelectedEntity?.Name);
        var menu = Assert.Single(window.GetVisualDescendants().OfType<ContextMenu>()
            .Concat(TopLevel.GetTopLevel(item)!.GetVisualDescendants().OfType<ContextMenu>()).Distinct());
        Assert.True(menu.IsOpen);
    }

    [AvaloniaFact]
    public async Task Topics_and_subscriptions_show_subscriptions_and_rules_folders()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        var events = (EntityNodeViewModel)Node("events");
        await events.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        var subscriptions = Assert.IsType<GroupNodeViewModel>(Assert.Single(events.DisplayChildren.Cast<TreeNodeViewModel>()));
        Assert.Equal(("Subscriptions", "Group", true), (subscriptions.Name, subscriptions.IconKey, subscriptions.IsExpanded));
        Assert.Equal(["all"], subscriptions.DisplayChildren.Cast<TreeNodeViewModel>().Select(n => n.Name));

        var all = (EntityNodeViewModel)Node("all");
        var rules = Assert.IsType<GroupNodeViewModel>(Assert.Single(all.DisplayChildren.Cast<TreeNodeViewModel>()));
        Assert.Equal("Rules", rules.Name);

        Assert.Equal(["Create Subscription", "Refresh Subscriptions", "Expand Subtree", "Collapse Subtree"], Headers(subscriptions));
        Assert.True(_explorer.NewSubscriptionCommand.CanExecute(null));
        Assert.Equal(["Add Rule", "Refresh Rules"], Headers(rules));
        Assert.True(_explorer.NewRuleCommand.CanExecute(null));
        Assert.Equal("Rules: events/Subscriptions/all", _explorer.DetailsTitle);
    }

    [AvaloniaFact]
    public void Every_icon_key_has_an_original_icon()
    {
        foreach (var key in new[] { "Namespace", "QueuesFolder", "TopicsFolder", "Queue", "QueueDisabled", "Topic", "TopicDisabled",
                     "Subscription", "SubscriptionDisabled", "Rule", "Group", "Header", "Log" })
        {
            Assert.NotNull(IconConverter.Load(key));
        }
    }
}
