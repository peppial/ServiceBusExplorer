using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.Core.Management;

namespace SbExplorer.App.ViewModels;

public partial class KeyValueRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;
}

public sealed record FilterKindOption(RuleFilterKind Kind, string Label);

/// <summary>Create or edit a queue, topic, subscription or rule.</summary>
public partial class EntityEditorViewModel : ObservableObject
{
    private readonly IEntityService _entities;

    private EntityEditorViewModel(IEntityService entities, EntityKind kind, bool isNew, string? topic, string? subscription)
    {
        _entities = entities;
        Kind = kind;
        IsNew = isNew;
        Topic = topic;
        Subscription = subscription;
        RuleProperties.CollectionChanged += OnRulePropertiesChanged;
        _selectedFilterKind = FilterKinds[0];
        _selectedStatus = EntityStatusValue.Active;
        LockDurationParts = new DurationPartsViewModel(_lockDuration, text => LockDuration = text);
        DefaultMessageTimeToLiveParts = new DurationPartsViewModel(_defaultMessageTimeToLive, text => DefaultMessageTimeToLive = text);
        AutoDeleteOnIdleParts = new DurationPartsViewModel(_autoDeleteOnIdle, text => AutoDeleteOnIdle = text);
        DuplicateDetectionWindowParts = new DurationPartsViewModel(_duplicateDetectionWindow, text => DuplicateDetectionWindow = text);
    }

    public DurationPartsViewModel LockDurationParts { get; }
    public DurationPartsViewModel DefaultMessageTimeToLiveParts { get; }
    public DurationPartsViewModel AutoDeleteOnIdleParts { get; }
    public DurationPartsViewModel DuplicateDetectionWindowParts { get; }

    /// <summary>The original labels: "Queue Properties", "Subscription Settings", …</summary>
    public string KindTitle => Kind.ToString();

    partial void OnLockDurationChanged(string value) => LockDurationParts?.Load(value);
    partial void OnDefaultMessageTimeToLiveChanged(string value) => DefaultMessageTimeToLiveParts?.Load(value);
    partial void OnAutoDeleteOnIdleChanged(string value) => AutoDeleteOnIdleParts?.Load(value);
    partial void OnDuplicateDetectionWindowChanged(string value) => DuplicateDetectionWindowParts?.Load(value);

    public EntityKind Kind { get; }
    public bool IsNew { get; }
    public bool IsEdit => !IsNew;
    public string? Topic { get; }
    public string? Subscription { get; }

    public bool IsQueue => Kind == EntityKind.Queue;
    public bool IsTopic => Kind == EntityKind.Topic;
    public bool IsSubscription => Kind == EntityKind.Subscription;
    public bool IsRule => Kind == EntityKind.Rule;
    public bool HasEntitySettings => !IsRule;
    public bool HasLockSettings => IsQueue || IsSubscription;
    public bool HasSizeSettings => IsQueue || IsTopic;

    /// <summary>A new subscription can start with a filter instead of the match-all $Default rule.</summary>
    public bool ShowsRuleEditor => IsRule || (IsSubscription && IsNew && UseInitialRule);

    /// <summary>Sessions, duplicate detection and partitioning are fixed once the entity exists.</summary>
    public bool CanChangeCreationOnlySettings => IsNew;

    public string Title => (IsNew ? "New " : "Edit ") + Kind.ToString().ToLowerInvariant() + (Kind switch
    {
        EntityKind.Subscription => $" on {Topic}",
        EntityKind.Rule => $" on {Topic}/{Subscription}",
        _ => string.Empty,
    });

    public IReadOnlyList<EntityStatusValue> Statuses { get; } = Enum.GetValues<EntityStatusValue>();

    public IReadOnlyList<FilterKindOption> FilterKinds { get; } =
    [
        new(RuleFilterKind.Sql, "SQL filter"),
        new(RuleFilterKind.Correlation, "Correlation filter"),
        new(RuleFilterKind.True, "All messages (true)"),
        new(RuleFilterKind.False, "No messages (false)"),
    ];

    public ObservableCollection<KeyValueRowViewModel> RuleProperties { get; } = [];

    // ---- entity settings ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _lockDuration = "1m";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _maxDeliveryCount = 10;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _defaultMessageTimeToLive = DurationInput.NeverText;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _autoDeleteOnIdle = DurationInput.NeverText;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _duplicateDetectionWindow = "10m";

    [ObservableProperty]
    private bool _deadLetteringOnMessageExpiration;

    [ObservableProperty]
    private bool _deadLetteringOnFilterEvaluationExceptions = true;

    [ObservableProperty]
    private bool _requiresSession;

    [ObservableProperty]
    private bool _requiresDuplicateDetection;

    [ObservableProperty]
    private bool _enablePartitioning;

    [ObservableProperty]
    private bool _supportOrdering;

    [ObservableProperty]
    private bool _enableBatchedOperations = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private long _maxSizeInMegabytes = 1024;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxMessageSizeInKilobytes = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _forwardTo = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _forwardDeadLetteredMessagesTo = string.Empty;

    [ObservableProperty]
    private string _userMetadata = string.Empty;

    [ObservableProperty]
    private EntityStatusValue _selectedStatus;

    // ---- rule settings ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsRuleEditor), nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _useInitialRule;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSqlFilter), nameof(IsCorrelationFilter), nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private FilterKindOption _selectedFilterKind;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _ruleName = RuleDefinition.DefaultName;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _sqlExpression = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationId = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationMessageId = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationSubject = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationSessionId = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationTo = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationReplyTo = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _correlationContentType = string.Empty;

    [ObservableProperty]
    private string _actionSqlExpression = string.Empty;

    [ObservableProperty]
    private string? _saveError;

    public bool IsSqlFilter => SelectedFilterKind.Kind == RuleFilterKind.Sql;
    public bool IsCorrelationFilter => SelectedFilterKind.Kind == RuleFilterKind.Correlation;

    /// <summary>The saved entity's path, for selecting it afterwards.</summary>
    public string? SavedPath { get; private set; }

    public event EventHandler? Saved;

    public static EntityEditorViewModel NewQueue(IEntityService entities) => new(entities, EntityKind.Queue, true, null, null);

    public static EntityEditorViewModel NewTopic(IEntityService entities) => new(entities, EntityKind.Topic, true, null, null);

    public static EntityEditorViewModel NewSubscription(IEntityService entities, string topic) =>
        new(entities, EntityKind.Subscription, true, topic, null);

    public static EntityEditorViewModel NewRule(IEntityService entities, string topic, string subscription) =>
        new(entities, EntityKind.Rule, true, topic, subscription) { RuleName = string.Empty, SqlExpression = "1=1" };

    public static async Task<EntityEditorViewModel> EditAsync(IEntityService entities, EntityInfo entity, CancellationToken cancellationToken = default)
    {
        var parts = entity.Path.Split('/');
        switch (entity.Kind)
        {
            case EntityKind.Queue:
            {
                var editor = new EntityEditorViewModel(entities, EntityKind.Queue, false, null, null);
                editor.Load(await entities.GetQueueAsync(entity.Name, cancellationToken));
                return editor;
            }
            case EntityKind.Topic:
            {
                var editor = new EntityEditorViewModel(entities, EntityKind.Topic, false, null, null);
                editor.Load(await entities.GetTopicAsync(entity.Name, cancellationToken));
                return editor;
            }
            case EntityKind.Subscription:
            {
                var editor = new EntityEditorViewModel(entities, EntityKind.Subscription, false, parts[0], null);
                editor.Load(await entities.GetSubscriptionAsync(parts[0], entity.Name, cancellationToken));
                return editor;
            }
            default:
            {
                var editor = new EntityEditorViewModel(entities, EntityKind.Rule, false, parts[0], parts[2]);
                editor.Load(await entities.GetRuleAsync(parts[0], parts[2], entity.Name, cancellationToken));
                return editor;
            }
        }
    }

    public string? ValidationError
    {
        get
        {
            if (!IsRule && NameError(Name, Kind) is { } nameError)
            {
                return nameError;
            }

            if (HasEntitySettings)
            {
                if (HasLockSettings)
                {
                    if (!DurationInput.TryParse(LockDuration, out var lockDuration))
                    {
                        return "Lock duration must look like 30s, 1m or 00:01:00.";
                    }

                    if (lockDuration > TimeSpan.FromMinutes(5))
                    {
                        return "Lock duration can be at most 5 minutes.";
                    }

                    if (MaxDeliveryCount < 1)
                    {
                        return "Max delivery count must be at least 1.";
                    }
                }

                if (!DurationInput.TryParse(DefaultMessageTimeToLive, out _))
                {
                    return "Default message time to live must look like 1h, 14d or Never.";
                }

                if (!DurationInput.TryParse(AutoDeleteOnIdle, out var autoDelete))
                {
                    return "Auto-delete on idle must look like 1h, 7d or Never.";
                }

                if (autoDelete < TimeSpan.FromMinutes(5))
                {
                    return "Auto-delete on idle must be at least 5 minutes.";
                }

                if (!IsSubscription && RequiresDuplicateDetection && !DurationInput.TryParse(DuplicateDetectionWindow, out _))
                {
                    return "Duplicate detection window must look like 10m or 1d.";
                }

                if (HasSizeSettings && MaxSizeInMegabytes < 1)
                {
                    return "Max size must be at least 1 MB.";
                }

                if (HasSizeSettings && !string.IsNullOrWhiteSpace(MaxMessageSizeInKilobytes) &&
                    !(long.TryParse(MaxMessageSizeInKilobytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) && kb > 0))
                {
                    return "Max message size must be a number of kilobytes, or empty for the default.";
                }

                if (!IsSubscription && string.Equals(ForwardTo.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return "An entity can't forward to itself.";
                }
            }

            if (ShowsRuleEditor)
            {
                if (NameError(RuleName, EntityKind.Rule) is { } ruleNameError)
                {
                    return ruleNameError;
                }

                if (IsSqlFilter && string.IsNullOrWhiteSpace(SqlExpression))
                {
                    return "Enter a SQL filter expression, e.g. priority = 'high'.";
                }

                if (IsCorrelationFilter && RuleProperties.Any(p => string.IsNullOrWhiteSpace(p.Key)))
                {
                    return "Every correlation property needs a name.";
                }

                if (IsCorrelationFilter && !BuildRule().HasCorrelationCriteria)
                {
                    return "A correlation filter needs at least one field or property to match.";
                }
            }

            return null;
        }
    }

    public bool IsValid => ValidationError is null;

    [RelayCommand]
    private void AddRuleProperty() => RuleProperties.Add(new KeyValueRowViewModel());

    [RelayCommand]
    private void RemoveRuleProperty(KeyValueRowViewModel row) => RuleProperties.Remove(row);

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveError = null;
        try
        {
            switch (Kind)
            {
                case EntityKind.Queue:
                    var queue = BuildQueue();
                    await (IsNew ? _entities.CreateQueueAsync(queue, cancellationToken) : _entities.UpdateQueueAsync(queue, cancellationToken));
                    SavedPath = queue.Name;
                    break;
                case EntityKind.Topic:
                    var topic = BuildTopic();
                    await (IsNew ? _entities.CreateTopicAsync(topic, cancellationToken) : _entities.UpdateTopicAsync(topic, cancellationToken));
                    SavedPath = topic.Name;
                    break;
                case EntityKind.Subscription:
                    var subscription = BuildSubscription();
                    await (IsNew ? _entities.CreateSubscriptionAsync(subscription, cancellationToken) : _entities.UpdateSubscriptionAsync(subscription, cancellationToken));
                    SavedPath = EntityInfo.SubscriptionPath(subscription.Topic, subscription.Name);
                    break;
                default:
                    var rule = BuildRule();
                    await (IsNew
                        ? _entities.CreateRuleAsync(Topic!, Subscription!, rule, cancellationToken)
                        : _entities.UpdateRuleAsync(Topic!, Subscription!, rule, cancellationToken));
                    SavedPath = EntityInfo.RulePath(Topic!, Subscription!, rule.Name);
                    break;
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveError = ErrorMessages.Describe(ex);
        }
    }

    public QueueDefinition BuildQueue() => new()
    {
        Name = Name.Trim(),
        LockDuration = Duration(LockDuration),
        MaxDeliveryCount = MaxDeliveryCount,
        DefaultMessageTimeToLive = Duration(DefaultMessageTimeToLive),
        DeadLetteringOnMessageExpiration = DeadLetteringOnMessageExpiration,
        RequiresSession = RequiresSession,
        RequiresDuplicateDetection = RequiresDuplicateDetection,
        DuplicateDetectionHistoryTimeWindow = Duration(DuplicateDetectionWindow, TimeSpan.FromMinutes(10)),
        EnablePartitioning = EnablePartitioning,
        MaxSizeInMegabytes = MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = MaxMessageKb(),
        AutoDeleteOnIdle = Duration(AutoDeleteOnIdle),
        ForwardTo = Blank(ForwardTo),
        ForwardDeadLetteredMessagesTo = Blank(ForwardDeadLetteredMessagesTo),
        EnableBatchedOperations = EnableBatchedOperations,
        UserMetadata = Blank(UserMetadata),
        Status = SelectedStatus,
    };

    public TopicDefinition BuildTopic() => new()
    {
        Name = Name.Trim(),
        DefaultMessageTimeToLive = Duration(DefaultMessageTimeToLive),
        RequiresDuplicateDetection = RequiresDuplicateDetection,
        DuplicateDetectionHistoryTimeWindow = Duration(DuplicateDetectionWindow, TimeSpan.FromMinutes(10)),
        EnablePartitioning = EnablePartitioning,
        SupportOrdering = SupportOrdering,
        MaxSizeInMegabytes = MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = MaxMessageKb(),
        AutoDeleteOnIdle = Duration(AutoDeleteOnIdle),
        EnableBatchedOperations = EnableBatchedOperations,
        UserMetadata = Blank(UserMetadata),
        Status = SelectedStatus,
    };

    public SubscriptionDefinition BuildSubscription() => new()
    {
        Topic = Topic!,
        Name = Name.Trim(),
        LockDuration = Duration(LockDuration),
        MaxDeliveryCount = MaxDeliveryCount,
        DefaultMessageTimeToLive = Duration(DefaultMessageTimeToLive),
        DeadLetteringOnMessageExpiration = DeadLetteringOnMessageExpiration,
        DeadLetteringOnFilterEvaluationExceptions = DeadLetteringOnFilterEvaluationExceptions,
        RequiresSession = RequiresSession,
        AutoDeleteOnIdle = Duration(AutoDeleteOnIdle),
        ForwardTo = Blank(ForwardTo),
        ForwardDeadLetteredMessagesTo = Blank(ForwardDeadLetteredMessagesTo),
        EnableBatchedOperations = EnableBatchedOperations,
        UserMetadata = Blank(UserMetadata),
        Status = SelectedStatus,
        Rules = IsNew && UseInitialRule ? [BuildRule()] : [],
    };

    public RuleDefinition BuildRule() => new()
    {
        Name = RuleName.Trim(),
        FilterKind = SelectedFilterKind.Kind,
        SqlExpression = IsSqlFilter ? SqlExpression.Trim() : null,
        CorrelationId = IsCorrelationFilter ? Blank(CorrelationId) : null,
        MessageId = IsCorrelationFilter ? Blank(CorrelationMessageId) : null,
        Subject = IsCorrelationFilter ? Blank(CorrelationSubject) : null,
        SessionId = IsCorrelationFilter ? Blank(CorrelationSessionId) : null,
        To = IsCorrelationFilter ? Blank(CorrelationTo) : null,
        ReplyTo = IsCorrelationFilter ? Blank(CorrelationReplyTo) : null,
        ContentType = IsCorrelationFilter ? Blank(CorrelationContentType) : null,
        Properties = IsCorrelationFilter
            ? RuleProperties.Where(p => !string.IsNullOrWhiteSpace(p.Key)).GroupBy(p => p.Key.Trim()).ToDictionary(g => g.Key, g => g.Last().Value)
            : new Dictionary<string, string>(),
        ActionSqlExpression = Blank(ActionSqlExpression),
    };

    /// <summary>Service Bus naming rules; queue and topic names may contain '/' to form a path.</summary>
    public static string? NameError(string name, EntityKind kind)
    {
        var trimmed = name.Trim();
        var label = kind.ToString().ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            return $"Enter a {label} name.";
        }

        var maxLength = kind is EntityKind.Queue or EntityKind.Topic ? 260 : 50;
        if (trimmed.Length > maxLength)
        {
            return $"A {label} name can be at most {maxLength} characters.";
        }

        var allowsSlash = kind is EntityKind.Queue or EntityKind.Topic;
        var allowsDollar = kind == EntityKind.Rule;
        if (trimmed.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' || (allowsSlash && c == '/') || (allowsDollar && c == '$'))))
        {
            return $"A {label} name can only contain letters, digits, '.', '-', '_'" + (allowsSlash ? " and '/'." : ".");
        }

        if (allowsSlash && (trimmed.StartsWith('/') || trimmed.EndsWith('/') || trimmed.Contains("//")))
        {
            return $"A {label} name can't start or end with '/' or contain '//'.";
        }

        return null;
    }

    private void Load(QueueDefinition q)
    {
        Name = q.Name;
        LockDuration = DurationInput.Format(q.LockDuration);
        MaxDeliveryCount = q.MaxDeliveryCount;
        DefaultMessageTimeToLive = DurationInput.Format(q.DefaultMessageTimeToLive);
        DeadLetteringOnMessageExpiration = q.DeadLetteringOnMessageExpiration;
        RequiresSession = q.RequiresSession;
        RequiresDuplicateDetection = q.RequiresDuplicateDetection;
        DuplicateDetectionWindow = DurationInput.Format(q.DuplicateDetectionHistoryTimeWindow);
        EnablePartitioning = q.EnablePartitioning;
        MaxSizeInMegabytes = q.MaxSizeInMegabytes;
        MaxMessageSizeInKilobytes = q.MaxMessageSizeInKilobytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        AutoDeleteOnIdle = DurationInput.Format(q.AutoDeleteOnIdle);
        ForwardTo = q.ForwardTo ?? string.Empty;
        ForwardDeadLetteredMessagesTo = q.ForwardDeadLetteredMessagesTo ?? string.Empty;
        EnableBatchedOperations = q.EnableBatchedOperations;
        UserMetadata = q.UserMetadata ?? string.Empty;
        SelectedStatus = q.Status;
    }

    private void Load(TopicDefinition t)
    {
        Name = t.Name;
        DefaultMessageTimeToLive = DurationInput.Format(t.DefaultMessageTimeToLive);
        RequiresDuplicateDetection = t.RequiresDuplicateDetection;
        DuplicateDetectionWindow = DurationInput.Format(t.DuplicateDetectionHistoryTimeWindow);
        EnablePartitioning = t.EnablePartitioning;
        SupportOrdering = t.SupportOrdering;
        MaxSizeInMegabytes = t.MaxSizeInMegabytes;
        MaxMessageSizeInKilobytes = t.MaxMessageSizeInKilobytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        AutoDeleteOnIdle = DurationInput.Format(t.AutoDeleteOnIdle);
        EnableBatchedOperations = t.EnableBatchedOperations;
        UserMetadata = t.UserMetadata ?? string.Empty;
        SelectedStatus = t.Status;
    }

    private void Load(SubscriptionDefinition s)
    {
        Name = s.Name;
        LockDuration = DurationInput.Format(s.LockDuration);
        MaxDeliveryCount = s.MaxDeliveryCount;
        DefaultMessageTimeToLive = DurationInput.Format(s.DefaultMessageTimeToLive);
        DeadLetteringOnMessageExpiration = s.DeadLetteringOnMessageExpiration;
        DeadLetteringOnFilterEvaluationExceptions = s.DeadLetteringOnFilterEvaluationExceptions;
        RequiresSession = s.RequiresSession;
        AutoDeleteOnIdle = DurationInput.Format(s.AutoDeleteOnIdle);
        ForwardTo = s.ForwardTo ?? string.Empty;
        ForwardDeadLetteredMessagesTo = s.ForwardDeadLetteredMessagesTo ?? string.Empty;
        EnableBatchedOperations = s.EnableBatchedOperations;
        UserMetadata = s.UserMetadata ?? string.Empty;
        SelectedStatus = s.Status;
    }

    private void Load(RuleDefinition r)
    {
        RuleName = r.Name;
        SelectedFilterKind = FilterKinds.First(k => k.Kind == r.FilterKind);
        SqlExpression = r.SqlExpression ?? string.Empty;
        CorrelationId = r.CorrelationId ?? string.Empty;
        CorrelationMessageId = r.MessageId ?? string.Empty;
        CorrelationSubject = r.Subject ?? string.Empty;
        CorrelationSessionId = r.SessionId ?? string.Empty;
        CorrelationTo = r.To ?? string.Empty;
        CorrelationReplyTo = r.ReplyTo ?? string.Empty;
        CorrelationContentType = r.ContentType ?? string.Empty;
        foreach (var (key, value) in r.Properties)
        {
            RuleProperties.Add(new KeyValueRowViewModel { Key = key, Value = value });
        }

        ActionSqlExpression = r.ActionSqlExpression ?? string.Empty;
    }

    private long? MaxMessageKb() =>
        long.TryParse(MaxMessageSizeInKilobytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb : null;

    private static TimeSpan Duration(string text, TimeSpan fallback = default) =>
        DurationInput.TryParse(text, out var value) ? value : fallback;

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Property rows are edited in place, so validation watches each row as well as the collection.
    private void OnRulePropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.NewItems?.OfType<KeyValueRowViewModel>() ?? [])
        {
            row.PropertyChanged += OnRulePropertyChanged;
        }

        foreach (var row in e.OldItems?.OfType<KeyValueRowViewModel>() ?? [])
        {
            row.PropertyChanged -= OnRulePropertyChanged;
        }

        OnRulePropertyChanged(sender, new PropertyChangedEventArgs(null));
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(IsValid));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
