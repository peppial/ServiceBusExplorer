using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;

namespace SbExplorer.App.Views;

public partial class EntityTreeView : UserControl
{
    public EntityTreeView()
    {
        InitializeComponent();
        EntityTree.ContextRequested += OnContextRequested;
    }

    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    // Right-click selects the node, then shows the original explorer's menu for that kind of node. The menu is
    // built here because its items depend on the node and bind to commands on the explorer and entity panel.
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ExplorerViewModel explorer
            || (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { DataContext: TreeNodeViewModel node } item)
        {
            return;
        }

        explorer.SelectedNode = node;
        var items = MenuFor(explorer, node);
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(item);
        e.Handled = true;
    }

    internal static List<Control> MenuFor(ExplorerViewModel explorer, TreeNodeViewModel node)
    {
        var manage = explorer.CanManage;
        var panel = explorer.Details;
        var expand = new Control[] { new Separator(), Item("Expand Subtree", explorer.ExpandSubtreeCommand), Item("Collapse Subtree", explorer.CollapseSubtreeCommand) };

        switch (node)
        {
            case NamespaceNodeViewModel:
                return
                [
                    Item("Refresh Entities", explorer.RefreshCommand),
                    Item("Export Entities", explorer.ExportSelectedCommand, enabled: manage),
                    Item("Import Entities", explorer.ImportCommand, enabled: manage),
                    Item("Copy Namespace URL", explorer.CopyUrlCommand, "namespace"),
                    .. expand,
                ];

            case FolderNodeViewModel { Kind: EntityKind.Queue } when explorer.IsLimited:
                return [Item("Add Entity…", explorer.AddEntityCommand), .. expand];

            case FolderNodeViewModel { Kind: EntityKind.Queue }:
                return
                [
                    Item("Create Queue", explorer.NewQueueCommand),
                    Item("Refresh Queues", explorer.RefreshCommand),
                    Item("Export Queues", explorer.ExportSelectedCommand),
                    .. expand,
                ];

            case FolderNodeViewModel { Kind: EntityKind.Topic }:
                return
                [
                    Item("Create Topic", explorer.NewTopicCommand),
                    Item("Refresh Topics", explorer.RefreshCommand),
                    Item("Export Topics", explorer.ExportSelectedCommand),
                    .. expand,
                ];

            case EntityNodeViewModel { Entity.Kind: EntityKind.Queue } queue when panel is not null:
                return
                [
                    StatusMenu(explorer, "Set Status", manage),
                    Item("Delete Queue", explorer.DeleteCommand, enabled: manage),
                    Item("Refresh Queue", explorer.RefreshCommand),
                    Item("Export Queue", explorer.ExportSelectedCommand, enabled: manage),
                    Item("Copy Queue URL", explorer.CopyUrlCommand, "entity"),
                    Item("Copy Deadletter Queue URL", explorer.CopyUrlCommand, "deadletter"),
                    .. MessageItems(panel, queue.Entity.RequiresSession),
                ];

            case EntityNodeViewModel { Entity.Kind: EntityKind.Topic } when panel is not null:
                return
                [
                    StatusMenu(explorer, "Change Status Topic", manage),
                    Item("Delete Topic", explorer.DeleteCommand, enabled: manage),
                    Item("Refresh Topic", explorer.RefreshCommand),
                    Item("Export Topic", explorer.ExportSelectedCommand, enabled: manage),
                    Item("Create Subscription", explorer.NewSubscriptionCommand),
                    Item("Copy Topic URL", explorer.CopyUrlCommand, "entity"),
                    new Separator(),
                    Item("Send Messages", panel.SendCommand),
                    .. expand,
                ];

            case EntityNodeViewModel { Entity.Kind: EntityKind.Subscription } subscription when panel is not null:
                return
                [
                    Item("Delete Subscription", explorer.DeleteCommand, enabled: manage),
                    StatusMenu(explorer, "Change Status Subscription", manage),
                    Item("Refresh Subscription", explorer.RefreshCommand),
                    Item("Add Rule", explorer.NewRuleCommand),
                    Item("Copy Subscription URL", explorer.CopyUrlCommand, "entity"),
                    Item("Copy Deadletter Queue URL", explorer.CopyUrlCommand, "deadletter"),
                    .. MessageItems(panel, subscription.Entity.RequiresSession),
                    .. expand,
                ];

            case GroupNodeViewModel { Parent.Entity.Kind: EntityKind.Topic }:
                return
                [
                    Item("Create Subscription", explorer.NewSubscriptionCommand),
                    Item("Refresh Subscriptions", explorer.RefreshCommand),
                    .. expand,
                ];

            case GroupNodeViewModel:
                return [Item("Add Rule", explorer.NewRuleCommand), Item("Refresh Rules", explorer.RefreshCommand)];

            case EntityNodeViewModel { Entity.Kind: EntityKind.Rule }:
                return [Item("Remove Rule", explorer.DeleteCommand, enabled: manage)];

            default:
                return [];
        }
    }

    private static IEnumerable<Control> MessageItems(EntityPanelViewModel panel, bool requiresSession)
    {
        yield return new Separator();
        yield return Item("Send Messages", panel.SendCommand);
        yield return Item("Receive Messages", panel.MessagesCommand);
        yield return Item("Receive Dead-letter Queue Messages", panel.DeadletterCommand);
        yield return Item("Receive Transfer Dead-letter Queue Messages", panel.TransferDeadletterCommand);
        yield return new Separator();
        yield return Item("Purge Messages", panel.PurgeCommand);
        yield return Item("Purge Dead-letter Queue Messages", panel.PurgeDeadletterCommand);
        if (requiresSession)
        {
            yield return new Separator();
            yield return Item("Get Message Sessions", panel.SessionsCommand);
        }
    }

    private static MenuItem StatusMenu(ExplorerViewModel explorer, string header, bool enabled)
    {
        var menu = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var (label, status) in new[]
                 {
                     ("Active", EntityStatusValue.Active),
                     ("Disabled", EntityStatusValue.Disabled),
                     ("Send Disabled", EntityStatusValue.SendDisabled),
                     ("Receive Disabled", EntityStatusValue.ReceiveDisabled),
                 })
        {
            menu.Items.Add(Item(label, explorer.SetStatusCommand, status));
        }

        return menu;
    }

    private static MenuItem Item(string header, ICommand command, object? parameter = null, bool enabled = true) => new()
    {
        Header = header,
        Command = command,
        CommandParameter = parameter,
        IsEnabled = enabled,
    };
}
