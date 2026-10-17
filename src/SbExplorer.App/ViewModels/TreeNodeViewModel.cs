using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SbExplorer.Core.Management;

namespace SbExplorer.App.ViewModels;

public abstract partial class TreeNodeViewModel : ObservableObject
{
    public abstract string Name { get; }

    /// <summary>Short glyph shown before the name; keeps the tree readable without an icon font.</summary>
    public abstract string Glyph { get; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public virtual EntityInfo? Entity => null;

    /// <summary>The text shown in the tree, e.g. <c>orders (16, 3)</c>.</summary>
    public virtual string DisplayName => Name;

    /// <summary>Name of the icon in Assets/Tree: Namespace, QueuesFolder, Queue, TopicDisabled, Group, Rule, …</summary>
    public virtual string? IconKey => null;

    /// <summary>What the tree shows under this node; topics and subscriptions add a Subscriptions or Rules folder.</summary>
    public virtual System.Collections.IEnumerable DisplayChildren => Children;
    public virtual string? ActiveBadge => null;
    public virtual string? DeadLetterBadge => null;
    public bool HasDeadLetters => DeadLetterBadge is not null;
    public virtual bool IsDimmed => false;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    protected virtual void OnExpanded()
    {
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            OnExpanded();
        }
    }

    public IEnumerable<TreeNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }
}

public sealed class FolderNodeViewModel(string title, EntityKind kind, string? iconKey = null) : TreeNodeViewModel
{
    public EntityKind Kind { get; } = kind;
    public string Title { get; } = title;
    public override string Name => $"{Title} ({Children.Count})";
    public override string DisplayName => Title;
    public override string? IconKey => iconKey ?? (Kind == EntityKind.Topic ? "TopicsFolder" : "QueuesFolder");
    public override string Glyph => "▤";

    public void SetChildren(IEnumerable<TreeNodeViewModel> children)
    {
        Children.Clear();
        foreach (var child in children)
        {
            Children.Add(child);
        }

        OnPropertyChanged(nameof(Name));
    }
}

/// <summary>The "Subscriptions" or "Rules" folder under a topic or subscription; shows its parent's children.</summary>
public sealed class GroupNodeViewModel : TreeNodeViewModel
{
    public GroupNodeViewModel(EntityNodeViewModel parent, string title)
    {
        Parent = parent;
        Name = title;
        IsExpanded = true;
    }

    public EntityNodeViewModel Parent { get; }
    public override string Name { get; }
    public override string Glyph => string.Empty;
    public override string IconKey => "Group";
    public override System.Collections.IEnumerable DisplayChildren => Parent.Children;
}

/// <summary>The tree's root: the namespace URI, holding the Queues and Topics folders.</summary>
public sealed class NamespaceNodeViewModel : TreeNodeViewModel
{
    public NamespaceNodeViewModel(string host, IEnumerable<TreeNodeViewModel> folders)
    {
        Name = $"sb://{host}/";
        foreach (var folder in folders)
        {
            Children.Add(folder);
        }

        IsExpanded = true;
    }

    public override string Name { get; }
    public override string Glyph => string.Empty;
    public override string IconKey => "Namespace";
}

/// <summary>Shown under a not-yet-loaded node so the expander appears, and to report load errors.</summary>
public sealed class MessageNodeViewModel(string text) : TreeNodeViewModel
{
    public override string Name => text;
    public override string Glyph => string.Empty;
    public override bool IsDimmed => true;
}

public partial class EntityNodeViewModel : TreeNodeViewModel
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<EntityNodeViewModel>>>? _loadChildren;
    private Task? _loading;

    public EntityNodeViewModel(EntityInfo entity, Func<CancellationToken, Task<IReadOnlyList<EntityNodeViewModel>>>? loadChildren = null)
    {
        Entity = entity;
        _loadChildren = loadChildren;
        if (loadChildren is not null)
        {
            Children.Add(new MessageNodeViewModel("Loading…"));
        }
    }

    public override EntityInfo Entity { get; }
    public override string Name => Entity.Name;

    // Disabled queues, topics and subscriptions use the original's grey icons.
    public override string IconKey => Entity.IsDisabled && Entity.Kind != EntityKind.Rule ? $"{Entity.Kind}Disabled" : Entity.Kind.ToString();

    private GroupNodeViewModel? _group;

    /// <summary>Like the original, subscriptions sit in a "Subscriptions" folder and rules in a "Rules" folder.</summary>
    public override System.Collections.IEnumerable DisplayChildren => Entity.Kind switch
    {
        EntityKind.Topic => new[] { _group ??= new GroupNodeViewModel(this, "Subscriptions") },
        EntityKind.Subscription => new[] { _group ??= new GroupNodeViewModel(this, "Rules") },
        _ => Children,
    };

    /// <summary>Queues and subscriptions show (active, dead-lettered) like the original explorer.</summary>
    public override string DisplayName => Entity.Counts is { } counts
        ? $"{Entity.Name} ({DisplayFormat.Count(counts.Active)}, {DisplayFormat.Count(counts.DeadLetter)})"
        : Entity.Name;
    public bool IsLoaded { get; private set; }

    public override string Glyph => Entity.Kind switch
    {
        EntityKind.Queue => "Q",
        EntityKind.Topic => "T",
        EntityKind.Subscription => "S",
        _ => "ƒ",
    };

    public override string? ActiveBadge =>
        Entity.Counts is { Active: > 0 } counts ? DisplayFormat.Count(counts.Active) : null;

    public override string? DeadLetterBadge =>
        Entity.Counts is { DeadLetter: > 0 } counts ? $"{DisplayFormat.Count(counts.DeadLetter)} DLQ" : null;

    public override bool IsDimmed => false;

    /// <summary>Loads children once; concurrent callers share the same load.</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        _loadChildren is null || IsLoaded ? Task.CompletedTask : _loading ??= LoadAsync(cancellationToken);

    protected override void OnExpanded() => _ = EnsureLoadedAsync();

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var children = await _loadChildren!(cancellationToken);
            Children.Clear();
            foreach (var child in children)
            {
                Children.Add(child);
            }

            if (children.Count == 0)
            {
                Children.Add(new MessageNodeViewModel(Entity.Kind == EntityKind.Topic ? "No subscriptions" : "No rules"));
            }

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            Children.Clear();
            Children.Add(new MessageNodeViewModel($"Could not load: {ErrorMessages.Describe(ex)}"));
        }
        finally
        {
            _loading = null;
        }
    }
}
