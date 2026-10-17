using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

/// <summary>
/// The right panel for one entity, like the original's "View Queue" control: a Description tab to view and
/// update settings, message tabs opened from the button bar, and the button bar itself.
/// </summary>
public partial class EntityPanelViewModel : ObservableObject, IAsyncDisposable
{
    public const int DescriptionTab = 0;
    public const int MessagesTab = 1;
    public const int DeadletterTab = 2;
    public const int TransferDeadletterTab = 3;
    public const int SessionsTab = 4;

    private readonly ExplorerViewModel _explorer;
    private readonly INamespaceSession _session;
    private readonly IDialogService _dialogs;
    private readonly ILogService? _log;
    private MessagesViewModel? _messages;
    private MessagesViewModel? _deadLetters;
    private MessagesViewModel? _transferDeadLetters;
    private SessionsViewModel? _sessions;

    public EntityPanelViewModel(ExplorerViewModel explorer, INamespaceSession session, IDialogService dialogs, ILogService? log, EntityInfo entity)
    {
        _explorer = explorer;
        _session = session;
        _dialogs = dialogs;
        _log = log;
        _entity = entity;
        EditorLoad = LoadEditorAsync();
    }

    /// <summary>The initial editor load, so tests and callers can wait for it.</summary>
    public Task EditorLoad { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Information), nameof(StatusButtonLabel), nameof(SupportsSessions))]
    private EntityInfo _entity;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private EntityEditorViewModel? _editor;

    [ObservableProperty]
    private string? _editorError;

    [ObservableProperty]
    private bool _showMessagesTab;

    [ObservableProperty]
    private bool _showDeadletterTab;

    [ObservableProperty]
    private bool _showTransferDeadletterTab;

    [ObservableProperty]
    private bool _showSessionsTab;

    [ObservableProperty]
    private int _selectedTab;

    public bool CanManage => _session.CanManage;
    public bool IsQueue => Entity.Kind == EntityKind.Queue;
    public bool IsTopic => Entity.Kind == EntityKind.Topic;
    public bool IsSubscription => Entity.Kind == EntityKind.Subscription;
    public bool IsRule => Entity.Kind == EntityKind.Rule;
    public bool SupportsMessages => IsQueue || IsSubscription;
    public bool SupportsSessions => SupportsMessages && Entity.RequiresSession;
    public bool HasStatus => !IsRule && CanManage;
    public string StatusButtonLabel => Entity.IsDisabled ? "Enable" : "Disable";
    public string InformationTitle => $"{Entity.Kind} Information";

    /// <summary>The "Queue Information" table: status and runtime values, or a rule's filter and action.</summary>
    public IReadOnlyList<EntityProperty> Information =>
        Entity.Properties.Where(p => p.Group is "Runtime" or "Filter" or "Action" or "Access" || p.Name == "Status").ToList();

    public MessagesViewModel? Messages => SupportsMessages ? _messages ??= CreateMessages(SubQueueKind.None) : null;
    public MessagesViewModel? DeadLetters => SupportsMessages ? _deadLetters ??= CreateMessages(SubQueueKind.DeadLetter) : null;

    public MessagesViewModel? TransferDeadLetters =>
        SupportsMessages ? _transferDeadLetters ??= CreateMessages(SubQueueKind.TransferDeadLetter) : null;

    public SessionsViewModel? Sessions =>
        SupportsSessions ? _sessions ??= new SessionsViewModel(_session.Messages, _dialogs, Entity, _log) : null;

    /// <summary>After a refresh: show fresh runtime values and reload the settings.</summary>
    public Task UpdateAsync(EntityInfo entity)
    {
        if (entity == Entity)
        {
            return Task.CompletedTask;
        }

        Entity = entity;
        return EditorLoad = LoadEditorAsync();
    }

    [RelayCommand]
    private Task MessagesAsync() => RetrieveAsync(SubQueueKind.None);

    [RelayCommand]
    private Task DeadletterAsync() => RetrieveAsync(SubQueueKind.DeadLetter);

    [RelayCommand]
    private Task TransferDeadletterAsync() => RetrieveAsync(SubQueueKind.TransferDeadLetter);

    [RelayCommand]
    private async Task SessionsAsync()
    {
        if (Sessions is not { } sessions)
        {
            return;
        }

        ShowSessionsTab = true;
        SelectedTab = SessionsTab;
        await sessions.FindSessionsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task PurgeAsync()
    {
        if (Messages is { } messages && messages.PurgeCommand.CanExecute(null))
        {
            ShowMessagesTab = true;
            SelectedTab = MessagesTab;
            await messages.PurgeCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task PurgeDeadletterAsync()
    {
        if (DeadLetters is { } deadLetters && deadLetters.PurgeCommand.CanExecute(null))
        {
            ShowDeadletterTab = true;
            SelectedTab = DeadletterTab;
            await deadLetters.PurgeCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (Messages is { } messages)
        {
            await messages.SendCommand.ExecuteAsync(null);
            return;
        }

        if (IsTopic)
        {
            var sender = new SendMessageViewModel(_session.Messages, Entity.Name, requiresSession: false);
            if (await _dialogs.ShowSendMessageAsync(sender))
            {
                _log?.Info($"{Entity.Name}: Sent {(sender.SentCount == 1 ? "1 message" : $"{sender.SentCount:N0} messages")}.");
            }
        }
    }

    /// <summary>"Close Tabs": hides the message tabs and releases any locks they hold.</summary>
    [RelayCommand]
    private async Task CloseTabsAsync()
    {
        foreach (var tab in new[] { _messages, _deadLetters, _transferDeadLetters })
        {
            if (tab is { HasLockedMessages: true })
            {
                await tab.ReleaseCommand.ExecuteAsync(null);
            }
        }

        ShowMessagesTab = ShowDeadletterTab = ShowTransferDeadletterTab = ShowSessionsTab = false;
        SelectedTab = DescriptionTab;
    }

    [RelayCommand]
    private Task ToggleStatusAsync() =>
        _explorer.SetStatusCommand.ExecuteAsync(Entity.IsDisabled ? EntityStatusValue.Active : EntityStatusValue.Disabled);

    [RelayCommand]
    private Task DeleteAsync() => _explorer.DeleteCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task RefreshAsync() => _explorer.RefreshCommand.ExecuteAsync(null);

    [RelayCommand]
    private void CreateSubscription() => _explorer.NewSubscriptionCommand.Execute(null);

    [RelayCommand]
    private void AddRule() => _explorer.NewRuleCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        var editor = Editor!;
        await editor.SaveCommand.ExecuteAsync(null);
        if (editor.SaveError is null)
        {
            await _explorer.CompleteSaveAsync(editor);
        }
        else
        {
            _log?.Error($"Could not update {Entity.Path}: {editor.SaveError}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in new[] { _messages, _deadLetters, _transferDeadLetters })
        {
            if (tab is not null)
            {
                await tab.DisposeAsync();
            }
        }

        if (_sessions is not null)
        {
            await _sessions.DisposeAsync();
        }
    }

    private async Task RetrieveAsync(SubQueueKind subQueue)
    {
        var target = subQueue switch
        {
            SubQueueKind.DeadLetter => DeadLetters,
            SubQueueKind.TransferDeadLetter => TransferDeadLetters,
            _ => Messages,
        };
        if (target is null)
        {
            return;
        }

        var where = subQueue switch
        {
            SubQueueKind.DeadLetter => $"the dead-letter queue of {Entity.Name}",
            SubQueueKind.TransferDeadLetter => $"the transfer dead-letter queue of {Entity.Name}",
            _ => Entity.Name,
        };
        var count = subQueue switch
        {
            SubQueueKind.DeadLetter => Entity.Counts?.DeadLetter,
            SubQueueKind.TransferDeadLetter => Entity.Counts?.TransferDeadLetter,
            _ => Entity.Counts?.Active,
        };
        var options = new ReceiveOptionsViewModel($"Retrieve messages from {where}", target.CanReceive, count,
            subQueue == SubQueueKind.None ? Entity.MaxDeliveryCount : null);
        if (!await _dialogs.ShowReceiveOptionsAsync(options))
        {
            return;
        }

        switch (subQueue)
        {
            case SubQueueKind.DeadLetter:
                ShowDeadletterTab = true;
                SelectedTab = DeadletterTab;
                break;
            case SubQueueKind.TransferDeadLetter:
                ShowTransferDeadletterTab = true;
                SelectedTab = TransferDeadletterTab;
                break;
            default:
                ShowMessagesTab = true;
                SelectedTab = MessagesTab;
                break;
        }

        await target.RetrieveAsync(options.Mode, options.EffectiveCount, options.FromSequence);
    }

    private MessagesViewModel CreateMessages(SubQueueKind subQueue) =>
        new(_session.Messages, _dialogs, Entity, log: _log, fixedSubQueue: subQueue);

    private async Task LoadEditorAsync()
    {
        if (!CanManage)
        {
            Editor = null;
            return;
        }

        try
        {
            var editor = await EntityEditorViewModel.EditAsync(_session.Entities, Entity);
            editor.PropertyChanged += OnEditorChanged;
            if (Editor is { } old)
            {
                old.PropertyChanged -= OnEditorChanged;
            }

            Editor = editor;
            EditorError = null;
        }
        catch (Exception ex)
        {
            Editor = null;
            EditorError = $"Could not read the settings of {Entity.Path}: {ErrorMessages.Describe(ex)}";
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntityEditorViewModel.IsValid))
        {
            UpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanUpdate() => CanManage && Editor is { IsValid: true };
}
