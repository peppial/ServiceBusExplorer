using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;

namespace SbExplorer.App.ViewModels;

public sealed record PropertyGroupViewModel(string Name, IReadOnlyList<EntityProperty> Items);

/// <summary>The entity tree and details panel for one connected namespace.</summary>
public partial class ExplorerViewModel(
    INamespaceSession session,
    IDialogService dialogs,
    TimeProvider? clock = null,
    Func<IReadOnlyList<string>, Task>? savePinnedEntities = null)
    : ObservableObject, IAsyncDisposable
{
    private readonly List<string> _pinned = [.. session.Profile.PinnedEntities];
    private readonly FolderNodeViewModel _pinnedFolder = new("Entities", EntityKind.Queue);

    // Kept per entity so peeked or locked messages survive clicking elsewhere in the tree.
    private readonly Dictionary<string, MessagesViewModel> _messageTabs = [];
    private readonly Dictionary<string, SessionsViewModel> _sessionTabs = [];

    private readonly IEntityService _entities = session.Entities;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly FolderNodeViewModel _queues = new("Queues", EntityKind.Queue);
    private readonly FolderNodeViewModel _topics = new("Topics", EntityKind.Topic);

    public string Title => $"{session.Profile.Name} — {session.FullyQualifiedNamespace}";

    public IReadOnlyList<FolderNodeViewModel> Roots => session.CanManage ? [_queues, _topics] : [_pinnedFolder];

    /// <summary>Without the Manage claim, entities can't be listed and are added by name instead.</summary>
    public bool IsLimited => !session.CanManage;

    public string? LimitedNotice => IsLimited
        ? "Connected without management access, so queues and topics can't be listed. Add the queues or topic/subscriptions you want by name; messages work as far as the key's Listen and Send claims allow."
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntity), nameof(PropertyGroups), nameof(HasSelection), nameof(Messages), nameof(HasMessages),
        nameof(Sessions), nameof(HasSessions))]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private string? _loadStatus;

    [ObservableProperty]
    private bool _hasError;

    public EntityInfo? SelectedEntity => SelectedNode?.Entity;
    public bool HasSelection => SelectedEntity is not null;

    public bool HasMessages => Messages is not null;

    /// <summary>The Messages tab for the selected queue or subscription.</summary>
    public MessagesViewModel? Messages
    {
        get
        {
            if (SelectedEntity is not { Kind: EntityKind.Queue or EntityKind.Subscription } entity)
            {
                return null;
            }

            if (!_messageTabs.TryGetValue(entity.Path, out var tab) || tab.Entity != entity && !tab.HasLockedMessages)
            {
                // Rebuild on fresh entity data, unless the old tab still holds locks the user must settle.
                tab = new MessagesViewModel(session.Messages, dialogs, entity);
                _messageTabs[entity.Path] = tab;
            }

            return tab;
        }
    }

    public bool HasSessions => Sessions is not null;

    /// <summary>The Sessions tab, for session-enabled queues and subscriptions.</summary>
    public SessionsViewModel? Sessions
    {
        get
        {
            if (SelectedEntity is not { Kind: EntityKind.Queue or EntityKind.Subscription, RequiresSession: true } entity)
            {
                return null;
            }

            if (!_sessionTabs.TryGetValue(entity.Path, out var tab))
            {
                tab = new SessionsViewModel(session.Messages, dialogs, entity);
                _sessionTabs[entity.Path] = tab;
            }

            return tab;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in _messageTabs.Values)
        {
            await tab.DisposeAsync();
        }

        foreach (var tab in _sessionTabs.Values)
        {
            await tab.DisposeAsync();
        }

        _messageTabs.Clear();
        _sessionTabs.Clear();
    }

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
        if (IsLimited)
        {
            ShowPinnedEntities(selectedPath);
            return;
        }

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

    [RelayCommand]
    private async Task AddEntityAsync()
    {
        var text = await dialogs.PromptAsync("Add entity", "Queue name, or topic/subscription:", string.Empty);
        if (text is null)
        {
            return;
        }

        if (!EntityReference.TryParse(text, out var entity, out var error))
        {
            LoadStatus = error;
            HasError = true;
            return;
        }

        if (!_pinned.Contains(entity!.Path, StringComparer.OrdinalIgnoreCase) && !string.Equals(entity.Path, session.ScopedEntity, StringComparison.OrdinalIgnoreCase))
        {
            _pinned.Add(entity.Path);
            await SavePinnedAsync();
        }

        ShowPinnedEntities(entity.Path);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveEntity))]
    private async Task RemoveEntityAsync()
    {
        var path = SelectedEntity!.Path;
        _pinned.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        await SavePinnedAsync();
        ShowPinnedEntities(selectedPath: null);
    }

    private bool CanRemoveEntity() =>
        IsLimited && SelectedEntity is { } entity && !string.Equals(entity.Path, session.ScopedEntity, StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedNodeChanged(TreeNodeViewModel? value) => RemoveEntityCommand.NotifyCanExecuteChanged();

    private void ShowPinnedEntities(string? selectedPath)
    {
        var paths = new List<string>();
        if (session.ScopedEntity is { } scoped)
        {
            paths.Add(scoped);
        }

        paths.AddRange(_pinned.Where(p => !paths.Contains(p, StringComparer.OrdinalIgnoreCase)));
        var nodes = paths
            .Select(p => EntityReference.TryParse(p, out var entity, out _) ? new EntityNodeViewModel(entity!) : null)
            .OfType<EntityNodeViewModel>()
            .ToList();

        _pinnedFolder.SetChildren(nodes);
        _pinnedFolder.IsExpanded = true;
        ApplyFilterTo(nodes, parentMatches: false);
        SelectedNode = nodes.FirstOrDefault(n => string.Equals(n.Entity.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        LoadStatus = nodes.Count == 0
            ? "No management access: add a queue or topic/subscription by name."
            : $"No management access · {Plural(nodes.Count, "entity added by name", "entities added by name")}";
        HasError = false;
    }

    private Task SavePinnedAsync() => savePinnedEntities?.Invoke(_pinned.ToList()) ?? Task.CompletedTask;

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
        ApplyFilterTo(_pinnedFolder.Children.OfType<EntityNodeViewModel>(), parentMatches: false);

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

    private static string Plural(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";
}
