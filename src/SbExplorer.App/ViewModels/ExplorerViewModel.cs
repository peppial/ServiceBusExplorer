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
    private readonly NamespaceTransfer _transfer = new(session.Entities);
    private readonly HashSet<string> _expandAfterRefresh = [];
    private string? _selectAfterRefresh;
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
        expanded.UnionWith(_expandAfterRefresh);
        _expandAfterRefresh.Clear();
        var expandFolders = (Queues: _queues.IsExpanded || _queues.Children.Count == 0, Topics: _topics.IsExpanded || _topics.Children.Count == 0);
        var selectedPath = _selectAfterRefresh ?? SelectedNode?.Entity?.Path;
        TreeNodeViewModel? selectedFolder = _selectAfterRefresh is null ? SelectedNode as FolderNodeViewModel : null;
        _selectAfterRefresh = null;

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

    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        foreach (var command in new IRelayCommand[]
                 {
                     RemoveEntityCommand, NewSubscriptionCommand, NewRuleCommand, EditCommand, DeleteCommand, SetStatusCommand,
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    public bool CanManage => session.CanManage;

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task NewQueueAsync() => EditAsync(EntityEditorViewModel.NewQueue(_entities));

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task NewTopicAsync() => EditAsync(EntityEditorViewModel.NewTopic(_entities));

    [RelayCommand(CanExecute = nameof(HasTopicContext))]
    private Task NewSubscriptionAsync() => EditAsync(EntityEditorViewModel.NewSubscription(_entities, SelectedTopic!));

    [RelayCommand(CanExecute = nameof(HasSubscriptionContext))]
    private Task NewRuleAsync()
    {
        var (topic, subscription) = SelectedSubscription!.Value;
        return EditAsync(EntityEditorViewModel.NewRule(_entities, topic, subscription));
    }

    [RelayCommand(CanExecute = nameof(HasManageableSelection))]
    private async Task EditAsync()
    {
        try
        {
            await EditAsync(await EntityEditorViewModel.EditAsync(_entities, SelectedEntity!));
        }
        catch (Exception ex)
        {
            ReportError($"Could not open {SelectedEntity?.Name}: {ErrorMessages.Describe(ex)}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasManageableSelection))]
    private async Task DeleteAsync()
    {
        var entity = SelectedEntity!;
        var kind = entity.Kind.ToString().ToLowerInvariant();
        if (entity.Kind == EntityKind.Rule)
        {
            if (!await dialogs.ConfirmAsync("Delete rule", $"Delete the rule '{entity.Name}'? Messages it would have matched won't reach this subscription.", "Delete"))
            {
                return;
            }
        }
        else
        {
            var consequence = entity.Kind == EntityKind.Topic
                ? "the topic, all of its subscriptions and every message in them"
                : $"the {kind} and every message in it";
            var typed = await dialogs.PromptAsync($"Delete {kind}", $"This permanently deletes {consequence}. Type '{entity.Name}' to confirm:", string.Empty);
            if (typed is null)
            {
                return;
            }

            if (!string.Equals(typed.Trim(), entity.Name, StringComparison.Ordinal))
            {
                ReportError($"Not deleted: '{typed.Trim()}' doesn't match '{entity.Name}'.");
                return;
            }
        }

        try
        {
            await _entities.DeleteAsync(entity);
            _messageTabs.Remove(entity.Path);
            _sessionTabs.Remove(entity.Path);
            SelectedNode = null;
            await RefreshCommand.ExecuteAsync(null);
            LoadStatus = $"Deleted {kind} {entity.Path}.";
        }
        catch (Exception ex)
        {
            ReportError($"Could not delete {entity.Path}: {ErrorMessages.Describe(ex)}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeStatus))]
    private async Task SetStatusAsync(EntityStatusValue status)
    {
        var entity = SelectedEntity!;
        try
        {
            await _entities.SetStatusAsync(entity, status);
            await ReloadAndSelectAsync(entity.Path);
            LoadStatus = $"{entity.Path} is now {status}.";
        }
        catch (Exception ex)
        {
            ReportError($"Could not change the status of {entity.Path}: {ErrorMessages.Describe(ex)}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task ExportAsync()
    {
        IReadOnlyCollection<EntityInfo>? only = null;
        if (SelectedEntity is { } selected)
        {
            var choice = await dialogs.ChooseAsync("Export", $"Export the definition of {selected.Path}, or of every entity in the namespace?",
                [$"Only {selected.Name}", "Whole namespace"]);
            if (choice is null)
            {
                return;
            }

            only = choice == 0 ? [selected] : null;
        }

        try
        {
            LoadStatus = "Exporting…";
            HasError = false;
            var definition = await _transfer.ExportAsync(session.FullyQualifiedNamespace, only);
            var name = only is null ? session.FullyQualifiedNamespace.Split('.')[0] : only.First().Name;
            var json = System.Text.Encoding.UTF8.GetBytes(NamespaceTransfer.Serialize(definition));
            LoadStatus = await dialogs.SaveFileAsync($"{name}-entities.json", json)
                ? $"Exported {Plural(definition.EntityCount, "entity definition", "entity definitions")}."
                : "Export cancelled.";
        }
        catch (Exception ex)
        {
            ReportError($"Export failed: {ErrorMessages.Describe(ex)}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task ImportAsync()
    {
        var json = await dialogs.OpenTextFileAsync("Import entity definitions");
        if (json is null)
        {
            return;
        }

        NamespaceDefinition definition;
        try
        {
            definition = NamespaceTransfer.Deserialize(json);
        }
        catch (Exception ex)
        {
            ReportError($"That file isn't an entity export: {ex.Message.Split('\n')[0]}");
            return;
        }

        var choice = await dialogs.ChooseAsync("Import",
            $"Import {Plural(definition.EntityCount, "entity", "entities")} into {session.FullyQualifiedNamespace}. What should happen to entities that already exist?",
            ["Skip existing", "Overwrite existing"]);
        if (choice is null)
        {
            return;
        }

        var progress = new Progress<ImportItemResult>(r => LoadStatus = $"Importing… {r.Path}: {r.Outcome}");
        var results = await _transfer.ImportAsync(definition, overwrite: choice == 1, progress);
        await RefreshCommand.ExecuteAsync(null);

        var summary = string.Join(", ", results.GroupBy(r => r.Outcome).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
        var failures = results.Where(r => r.Outcome == ImportOutcome.Failed).ToList();
        LoadStatus = $"Import finished: {summary}.";
        HasError = failures.Count > 0;
        if (failures.Count > 0)
        {
            await dialogs.ChooseAsync("Import finished with errors",
                string.Join("\n", failures.Select(f => $"{f.Path}: {f.Error}")), ["OK"]);
        }
    }

    private async Task EditAsync(EntityEditorViewModel editor)
    {
        if (await dialogs.ShowEntityEditorAsync(editor) && editor.SavedPath is { } path)
        {
            await ReloadAndSelectAsync(path);
            LoadStatus = $"{(editor.IsNew ? "Created" : "Saved")} {path}.";
        }
    }

    /// <summary>Refreshes the tree, expanding the parents of <paramref name="path"/> and selecting it.</summary>
    private async Task ReloadAndSelectAsync(string path)
    {
        var parts = path.Split('/');
        if (parts.Length >= 3)
        {
            _expandAfterRefresh.Add(parts[0]);
        }

        if (parts.Length >= 5)
        {
            _expandAfterRefresh.Add(EntityInfo.SubscriptionPath(parts[0], parts[2]));
        }

        _selectAfterRefresh = path;
        await RefreshCommand.ExecuteAsync(null);
    }

    private void ReportError(string message)
    {
        LoadStatus = message;
        HasError = true;
    }

    private string? SelectedTopic => SelectedEntity switch
    {
        { Kind: EntityKind.Topic } topic => topic.Name,
        { Kind: EntityKind.Subscription or EntityKind.Rule } child => child.Path.Split('/')[0],
        _ => null,
    };

    private (string Topic, string Subscription)? SelectedSubscription => SelectedEntity switch
    {
        { Kind: EntityKind.Subscription } s => (s.Path.Split('/')[0], s.Name),
        { Kind: EntityKind.Rule } r => (r.Path.Split('/')[0], r.Path.Split('/')[2]),
        _ => null,
    };

    private bool HasTopicContext() => CanManage && SelectedTopic is not null;
    private bool HasSubscriptionContext() => CanManage && SelectedSubscription is not null;
    private bool HasManageableSelection() => CanManage && SelectedEntity is not null;
    private bool CanChangeStatus(EntityStatusValue status) => HasManageableSelection() && SelectedEntity!.Kind != EntityKind.Rule;

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
