using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;

namespace SbExplorer.App.ViewModels;

public sealed record PropertyGroupViewModel(string Name, IReadOnlyList<EntityProperty> Items);

/// <summary>The entity tree and details panel for one connected namespace.</summary>
public partial class ExplorerViewModel(INamespaceSession session, TimeProvider? clock = null) : ObservableObject
{
    private readonly IEntityService _entities = session.Entities;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly FolderNodeViewModel _queues = new("Queues", EntityKind.Queue);
    private readonly FolderNodeViewModel _topics = new("Topics", EntityKind.Topic);

    public string Title => $"{session.Profile.Name} — {session.FullyQualifiedNamespace}";

    public IReadOnlyList<FolderNodeViewModel> Roots => [_queues, _topics];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntity), nameof(PropertyGroups), nameof(HasSelection))]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private string? _loadStatus;

    [ObservableProperty]
    private bool _hasError;

    public EntityInfo? SelectedEntity => SelectedNode?.Entity;
    public bool HasSelection => SelectedEntity is not null;

    public IReadOnlyList<PropertyGroupViewModel> PropertyGroups =>
        SelectedEntity?.Properties
            .GroupBy(p => p.Group)
            .Select(g => new PropertyGroupViewModel(g.Key, g.ToList()))
            .ToList() ?? [];

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Remember what the user had open so a refresh doesn't collapse the tree.
        var expanded = AllEntityNodes().Where(n => n.IsExpanded).Select(n => n.Entity.Path).ToHashSet();
        var expandFolders = (Queues: _queues.IsExpanded || _queues.Children.Count == 0, Topics: _topics.IsExpanded || _topics.Children.Count == 0);
        var selectedPath = SelectedNode?.Entity?.Path;
        TreeNodeViewModel? selectedFolder = SelectedNode as FolderNodeViewModel;

        LoadStatus = "Loading…";
        HasError = false;
        try
        {
            var queues = _entities.GetQueuesAsync(cancellationToken);
            var topics = _entities.GetTopicsAsync(cancellationToken);
            await Task.WhenAll(queues, topics);

            _queues.SetChildren((await queues).Select(q => new EntityNodeViewModel(q)));
            _topics.SetChildren((await topics).Select(TopicNode));
            _queues.IsExpanded = expandFolders.Queues;
            _topics.IsExpanded = expandFolders.Topics;

            await RestoreExpansionAsync(expanded, cancellationToken);
            ApplyFilter();

            SelectedNode = selectedFolder ?? (selectedPath is null
                ? null
                : AllEntityNodes().FirstOrDefault(n => n.Entity.Path == selectedPath));

            var now = _clock.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            LoadStatus = $"{Plural(_queues.Children.Count, "queue")}, {Plural(_topics.Children.Count, "topic")} · refreshed {now}"
                + (session.IsEmulator ? " · the emulator doesn't report message counts" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            LoadStatus = "Refresh cancelled";
        }
        catch (Exception ex)
        {
            LoadStatus = $"Could not load entities: {ErrorMessages.Describe(ex)}";
            HasError = true;
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private EntityNodeViewModel TopicNode(EntityInfo topic) =>
        new(topic, async ct =>
        {
            var subscriptions = await _entities.GetSubscriptionsAsync(topic.Name, ct);
            var nodes = subscriptions.Select(s => SubscriptionNode(topic.Name, s)).ToList();
            ApplyFilterTo(nodes, parentMatches: Matches(topic.Name));
            return nodes;
        });

    private EntityNodeViewModel SubscriptionNode(string topic, EntityInfo subscription) =>
        new(subscription, async ct =>
        {
            var rules = await _entities.GetRulesAsync(topic, subscription.Name, ct);
            return rules.Select(r => new EntityNodeViewModel(r)).ToList();
        });

    private async Task RestoreExpansionAsync(HashSet<string> expanded, CancellationToken cancellationToken)
    {
        foreach (var topic in _topics.Children.OfType<EntityNodeViewModel>().Where(t => expanded.Contains(t.Entity.Path)))
        {
            await topic.EnsureLoadedAsync(cancellationToken);
            topic.IsExpanded = true;
            foreach (var subscription in topic.Children.OfType<EntityNodeViewModel>().Where(s => expanded.Contains(s.Entity.Path)))
            {
                await subscription.EnsureLoadedAsync(cancellationToken);
                subscription.IsExpanded = true;
            }
        }
    }

    /// <summary>
    /// Shows queues and topics whose name contains the filter, plus topics with a matching subscription
    /// among those already loaded. Children of a matching topic stay visible.
    /// </summary>
    private void ApplyFilter()
    {
        ApplyFilterTo(_queues.Children.OfType<EntityNodeViewModel>(), parentMatches: false);

        foreach (var topic in _topics.Children.OfType<EntityNodeViewModel>())
        {
            var topicMatches = Matches(topic.Entity.Name);
            var subscriptions = topic.Children.OfType<EntityNodeViewModel>().ToList();
            ApplyFilterTo(subscriptions, topicMatches);
            topic.IsVisible = topicMatches || subscriptions.Any(s => s.IsVisible);
        }
    }

    private void ApplyFilterTo(IEnumerable<EntityNodeViewModel> nodes, bool parentMatches)
    {
        foreach (var node in nodes)
        {
            node.IsVisible = parentMatches || Matches(node.Entity.Name);
        }
    }

    private bool Matches(string name) =>
        string.IsNullOrWhiteSpace(Filter) || name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private IEnumerable<EntityNodeViewModel> AllEntityNodes() =>
        Roots.SelectMany(r => r.SelfAndDescendants()).OfType<EntityNodeViewModel>();

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
