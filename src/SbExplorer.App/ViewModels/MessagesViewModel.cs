using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

public sealed record SubQueueOption(SubQueueKind Kind, string Label);

/// <summary>The Messages tab for one queue or subscription, including its dead-letter sub-queues.</summary>
public partial class MessagesViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(5);

    private readonly IMessageService _service;
    private readonly IDialogService _dialogs;
    private IReceivedBatch? _lockedBatch;

    public MessagesViewModel(IMessageService service, IDialogService dialogs, EntityInfo entity, string? sessionId = null)
    {
        _service = service;
        _dialogs = dialogs;
        Entity = entity;
        SessionId = sessionId;
        _selectedSubQueue = SubQueues[0];
    }

    public EntityInfo Entity { get; }

    /// <summary>Set when this tab shows one session of a session-enabled entity.</summary>
    public string? SessionId { get; }

    public bool IsSessionView => SessionId is not null;
    public bool ShowSubQueues => !IsSessionView;
    public ObservableCollection<MessageRowViewModel> Messages { get; } = [];

    public IReadOnlyList<SubQueueOption> SubQueues { get; } =
    [
        new(SubQueueKind.None, "Active"),
        new(SubQueueKind.DeadLetter, "Dead-letter"),
        new(SubQueueKind.TransferDeadLetter, "Transfer dead-letter"),
    ];

    public MessageSource Source
    {
        get
        {
            var source = Entity.Kind == EntityKind.Subscription
                ? MessageSource.ForSubscription(Entity.Path.Split('/')[0], Entity.Name, SelectedSubQueue.Kind)
                : MessageSource.ForQueue(Entity.Name, SelectedSubQueue.Kind);
            return SessionId is null ? source : source.ForSession(SessionId);
        }
    }

    public bool IsDeadLetterView => SelectedSubQueue.Kind != SubQueueKind.None;
    public bool IsActiveView => !IsDeadLetterView;

    /// <summary>
    /// Service Bus refuses to browse an entity that auto-forwards. Its dead-letter queue doesn't forward
    /// (unless ForwardDeadLetteredMessagesTo is set), so that stays browsable.
    /// </summary>
    public bool CanBrowse => IsDeadLetterView || Entity.ForwardTo is null;

    /// <summary>The active queue of a session entity needs a session receiver; its dead-letter queue doesn't.</summary>
    public bool CanReceive => CanBrowse && (IsDeadLetterView || !Entity.RequiresSession || IsSessionView);

    /// <summary>Purging a whole session entity drains it session by session.</summary>
    private bool PurgesAllSessions => IsActiveView && Entity.RequiresSession && !IsSessionView;

    public string? Notice => IsSessionView
        ? null
        : !CanBrowse
        ? $"This {KindName} forwards every message to '{Entity.ForwardTo}', so Service Bus doesn't allow browsing or receiving here. Open '{Entity.ForwardTo}' instead, or check this {KindName}'s dead-letter queue."
        : IsActiveView && Entity.RequiresSession
            ? $"This {KindName} requires sessions. Peek shows messages from all sessions; to receive, open a session on the Sessions tab."
            : IsDeadLetterView && Source.IsSubscription
                ? $"Resubmitted messages go to the topic '{Source.SendTarget}', so every subscription whose filter matches receives a copy."
                : null;

    public string SendLabel => Source.IsSubscription ? $"Send to topic '{Source.SendTarget}'…" : "Send…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Source), nameof(IsDeadLetterView), nameof(IsActiveView), nameof(CanBrowse), nameof(CanReceive), nameof(Notice))]
    private SubQueueOption _selectedSubQueue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekCommand), nameof(PeekNextCommand), nameof(ReceiveAndDeleteCommand), nameof(ReceiveLockedCommand))]
    private int _maxMessages = 50;

    [ObservableProperty]
    private string _fromSequence = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand), nameof(AbandonCommand), nameof(DeadLetterCommand), nameof(DeferCommand),
        nameof(ResendCommand), nameof(EditAndResubmitCommand))]
    private MessageRowViewModel? _selectedMessage;

    /// <summary>All rows selected in the table; bulk actions work on these.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyCanExecuteChangedFor(nameof(ResubmitCommand), nameof(DeleteCommand), nameof(ReceiveDeferredCommand), nameof(CancelScheduledCommand))]
    private IReadOnlyList<MessageRowViewModel> _selectedRows = [];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedMessages))]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand), nameof(PeekCommand), nameof(PeekNextCommand), nameof(ReceiveAndDeleteCommand),
        nameof(ReceiveLockedCommand), nameof(ReceiveDeferredCommand), nameof(ResubmitCommand), nameof(DeleteCommand), nameof(PurgeCommand))]
    private int _lockedCount;

    [ObservableProperty]
    private string? _lockedUntil;

    public bool HasLockedMessages => LockedCount > 0;
    public bool HasMoreToPeek { get; private set; }
    public string? SelectionSummary => SelectedRows.Count > 1 ? $"{SelectedRows.Count} selected" : null;

    public MessageDetailViewModel? Detail => SelectedMessage is null ? null : new MessageDetailViewModel(SelectedMessage.Message, _dialogs);

    async partial void OnSelectedSubQueueChanged(SubQueueOption value)
    {
        await ReleaseLocksAsync();
        Messages.Clear();
        SelectedRows = [];
        HasMoreToPeek = false;
        SetStatus(string.Empty);
        NotifyAllCommands();
        if (CanBrowse)
        {
            await PeekCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRead))]
    private async Task PeekAsync(CancellationToken cancellationToken)
    {
        long? from = long.TryParse(FromSequence, out var parsed) ? parsed : null;
        await RunAsync(async () =>
        {
            var messages = await _service.PeekAsync(Source, MaxMessages, from, cancellationToken);
            ShowMessages(messages, append: false);
            HasMoreToPeek = messages.Count == MaxMessages;
            PeekNextCommand.NotifyCanExecuteChanged();
            return messages.Count == 0 ? "No messages." : $"Peeked {Plural(messages.Count)}{SequenceRange(messages)}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanPeekNext))]
    private async Task PeekNextAsync(CancellationToken cancellationToken)
    {
        var from = Messages[^1].SequenceNumber + 1;
        await RunAsync(async () =>
        {
            var messages = await _service.PeekAsync(Source, MaxMessages, from, cancellationToken);
            ShowMessages(messages, append: true);
            HasMoreToPeek = messages.Count == MaxMessages;
            PeekNextCommand.NotifyCanExecuteChanged();
            return messages.Count == 0 ? "No more messages." : $"Peeked {Plural(messages.Count)} more ({Messages.Count} shown).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartReceive))]
    private async Task ReceiveAndDeleteAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Receive and delete",
            $"Permanently remove up to {Plural(MaxMessages)} from {SourceLabel}? They can't be put back.",
            "Receive and delete");
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var messages = await _service.ReceiveAndDeleteAsync(Source, MaxMessages, ReceiveWait, cancellationToken);
            ShowMessages(messages, append: false);
            return messages.Count == 0 ? "No messages to receive." : $"Received and deleted {Plural(messages.Count)}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartReceive))]
    private async Task ReceiveLockedAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var batch = await _service.ReceivePeekLockAsync(Source, MaxMessages, ReceiveWait, cancellationToken);
            return await HoldLocksAsync(batch, "receive");
        });
    }

    [RelayCommand(CanExecute = nameof(CanReceiveDeferred))]
    private async Task ReceiveDeferredAsync(CancellationToken cancellationToken)
    {
        var sequenceNumbers = SelectedRows.Select(r => r.SequenceNumber).ToList();
        await RunAsync(async () =>
        {
            var batch = await _service.ReceiveDeferredAsync(Source, sequenceNumbers, cancellationToken);
            return await HoldLocksAsync(batch, "receive as deferred");
        });
    }

    [RelayCommand(CanExecute = nameof(IsSelectedLocked))]
    private Task CompleteAsync() => SettleAsync("Completed", (batch, m) => batch.CompleteAsync(m));

    [RelayCommand(CanExecute = nameof(IsSelectedLocked))]
    private Task AbandonAsync() => SettleAsync("Abandoned", (batch, m) => batch.AbandonAsync(m));

    [RelayCommand(CanExecute = nameof(IsSelectedLocked))]
    private Task DeferAsync() => SettleAsync("Deferred", (batch, m) => batch.DeferAsync(m));

    [RelayCommand(CanExecute = nameof(IsSelectedLocked))]
    private async Task DeadLetterAsync()
    {
        var reason = await _dialogs.PromptAsync("Dead-letter message", "Reason", "ManualDeadLetter");
        if (reason is null)
        {
            return;
        }

        await SettleAsync("Dead-lettered", (batch, m) => batch.DeadLetterAsync(m, reason, "Dead-lettered from Service Bus Explorer"));
    }

    [RelayCommand(CanExecute = nameof(HasLockedMessages))]
    private async Task ReleaseAsync()
    {
        var count = LockedCount;
        await ReleaseLocksAsync();
        RemoveRows(Messages.Where(r => r.Message.LockToken is not null).ToList());
        SetStatus($"Released {Plural(count)} back to {SourceLabel}.");
    }

    [RelayCommand(CanExecute = nameof(CanCancelScheduled))]
    private async Task CancelScheduledAsync(CancellationToken cancellationToken)
    {
        var rows = SelectedRows.ToList();
        if (!await _dialogs.ConfirmAsync("Cancel scheduled messages", $"Cancel {Plural(rows.Count)}? They will never be delivered.", "Cancel messages"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _service.CancelScheduledAsync(Source.SendTarget, rows.Select(r => r.SequenceNumber).ToList(), cancellationToken);
            RemoveRows(rows);
            return $"Cancelled {Plural(rows.Count)}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanResubmit))]
    private async Task ResubmitAsync(CancellationToken cancellationToken)
    {
        var rows = SelectedRows.ToList();
        var target = await _dialogs.PromptAsync(
            $"Resubmit {Plural(rows.Count)}",
            "Send copies to this queue or topic, then remove them from the dead-letter queue:",
            Source.SendTarget);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var done = await _service.ResubmitAsync(Source, rows.Select(r => r.SequenceNumber).ToList(), target.Trim(), cancellationToken);
            RemoveRows(rows.Where(r => done.Contains(r.SequenceNumber)).ToList());
            return $"Resubmitted {Plural(done.Count)} to {target.Trim()}.{Missing(rows.Count, done.Count)}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanEditAndResubmit))]
    private async Task EditAndResubmitAsync(CancellationToken cancellationToken)
    {
        var row = SelectedMessage!;
        var sender = new SendMessageViewModel(_service, Source.SendTarget, requiresSession: false, row.Message)
        {
            Heading = $"Edit and resubmit message {row.SequenceNumber}",
        };
        if (!await _dialogs.ShowSendMessageAsync(sender))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var deleted = await _service.DeleteAsync(Source, [row.SequenceNumber], cancellationToken);
            if (deleted.Count == 0)
            {
                return $"Sent the edited message to {Source.SendTarget}, but the original {row.SequenceNumber} was no longer in the dead-letter queue.";
            }

            RemoveRows([row]);
            return $"Sent the edited message to {Source.SendTarget} and removed the original {row.SequenceNumber}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var rows = SelectedRows.ToList();
        if (!await _dialogs.ConfirmAsync("Delete messages", $"Permanently delete {Plural(rows.Count)} from {SourceLabel}?", "Delete"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var done = await _service.DeleteAsync(Source, rows.Select(r => r.SequenceNumber).ToList(), cancellationToken);
            RemoveRows(rows.Where(r => done.Contains(r.SequenceNumber)).ToList());
            return $"Deleted {Plural(done.Count)}.{Missing(rows.Count, done.Count)}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanPurge), IncludeCancelCommand = true)]
    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var target = PurgesAllSessions ? $"every session of {Source.EntityPath}" : SourceLabel;
        if (!await _dialogs.ConfirmAsync("Purge", $"Permanently delete every message in {target}? This can't be undone.", "Purge"))
        {
            return;
        }

        var progress = new Progress<long>(count => SetStatus($"Purging {target}… {count:N0} deleted so far"));
        await RunAsync(async () =>
        {
            var purged = PurgesAllSessions
                ? await _service.PurgeAllSessionsAsync(Source, progress, cancellationToken)
                : await _service.PurgeAsync(Source, progress, cancellationToken);
            Messages.Clear();
            SelectedRows = [];
            return $"Purged {Plural((int)Math.Min(purged, int.MaxValue))} from {target}.";
        });
    }

    [RelayCommand]
    private Task SendAsync() => OpenSendDialogAsync(template: null);

    [RelayCommand(CanExecute = nameof(HasSelectedMessage))]
    private Task ResendAsync() => OpenSendDialogAsync(SelectedMessage!.Message);

    public async ValueTask DisposeAsync() => await ReleaseLocksAsync();

    private async Task OpenSendDialogAsync(MessageInfo? template)
    {
        // Topics don't carry RequiresSession, so a session ID is only enforced for queues.
        var sender = new SendMessageViewModel(_service, Source.SendTarget, Entity.Kind == EntityKind.Queue && Entity.RequiresSession, template);
        if (SessionId is not null)
        {
            sender.SessionId = SessionId;
        }

        if (await _dialogs.ShowSendMessageAsync(sender))
        {
            SetStatus(sender.Schedule
                ? $"Scheduled {Plural(sender.SentCount)} on {Source.SendTarget}."
                : $"Sent {Plural(sender.SentCount)} to {Source.SendTarget}.");
        }
    }

    private async Task<string> HoldLocksAsync(IReceivedBatch batch, string verb)
    {
        if (batch.Messages.Count == 0)
        {
            await batch.DisposeAsync();
            ShowMessages([], append: false);
            return $"No messages to {verb}.";
        }

        _lockedBatch = batch;
        ShowMessages(batch.Messages, append: false);
        UpdateLockState();
        return $"Locked {Plural(batch.Messages.Count)}. Complete, abandon, dead-letter or defer each one, or release them all.";
    }

    private async Task SettleAsync(string verb, Func<IReceivedBatch, MessageInfo, Task> settle)
    {
        var row = SelectedMessage!;
        try
        {
            await settle(_lockedBatch!, row.Message);
            RemoveRows([row]);
            UpdateLockState();
            SetStatus($"{verb} message {row.SequenceNumber}.");
            if (LockedCount == 0)
            {
                await ReleaseLocksAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not settle message {row.SequenceNumber}: {ErrorMessages.Describe(ex)}", isError: true);
        }
    }

    private async Task RunAsync(Func<Task<string>> operation)
    {
        SetStatus("Working…");
        try
        {
            SetStatus(await operation());
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus(ErrorMessages.Describe(ex), isError: true);
        }
    }

    private void ShowMessages(IReadOnlyList<MessageInfo> messages, bool append)
    {
        if (!append)
        {
            Messages.Clear();
            SelectedRows = [];
        }

        foreach (var message in messages)
        {
            Messages.Add(new MessageRowViewModel(message));
        }

        if (!append)
        {
            SelectedMessage = Messages.FirstOrDefault();
            SelectedRows = SelectedMessage is null ? [] : [SelectedMessage];
        }
    }

    /// <summary>Removes rows and moves the selection to the next remaining row.</summary>
    private void RemoveRows(IReadOnlyCollection<MessageRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var index = SelectedMessage is null ? 0 : Math.Max(0, Messages.IndexOf(SelectedMessage));
        foreach (var row in rows)
        {
            Messages.Remove(row);
        }

        SelectedMessage = Messages.Count == 0 ? null : Messages[Math.Min(index, Messages.Count - 1)];
        SelectedRows = SelectedMessage is null ? [] : [SelectedMessage];
    }

    private async Task ReleaseLocksAsync()
    {
        if (_lockedBatch is { } batch)
        {
            _lockedBatch = null;
            await batch.DisposeAsync();
        }

        UpdateLockState();
    }

    private void UpdateLockState()
    {
        var locked = Messages.Where(r => r.Message.LockToken is not null).ToList();
        LockedCount = _lockedBatch is null ? 0 : locked.Count;
        LockedUntil = locked.Count == 0 || _lockedBatch is null
            ? null
            : MessageRowViewModel.LocalTime(locked.Min(r => r.Message.LockedUntil ?? DateTimeOffset.MaxValue));
        CompleteCommand.NotifyCanExecuteChanged();
        AbandonCommand.NotifyCanExecuteChanged();
        DeadLetterCommand.NotifyCanExecuteChanged();
        DeferCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAllCommands()
    {
        foreach (var command in new IRelayCommand[]
                 {
                     PeekCommand, PeekNextCommand, ReceiveAndDeleteCommand, ReceiveLockedCommand, ReceiveDeferredCommand,
                     CancelScheduledCommand, ResubmitCommand, EditAndResubmitCommand, DeleteCommand, PurgeCommand,
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        Status = string.IsNullOrEmpty(message) ? null : message;
        HasError = isError;
    }

    // Holding locks while peeking or receiving again would confuse which rows can be settled.
    private bool CanStartRead() => CanBrowse && !HasLockedMessages && MaxMessages > 0;
    private bool CanStartReceive() => CanReceive && !HasLockedMessages && MaxMessages > 0;
    private bool CanPeekNext() => CanStartRead() && HasMoreToPeek && Messages.Count > 0;
    private bool HasSelectedMessage() => SelectedMessage is not null;
    private bool IsSelectedLocked() => _lockedBatch is not null && SelectedMessage?.Message.LockToken is not null;
    private bool HasUnlockedSelection() => SelectedRows.Count > 0 && !HasLockedMessages;

    private bool CanReceiveDeferred() =>
        CanReceive && HasUnlockedSelection() && SelectedRows.All(r => r.Message.State == MessageState.Deferred);

    private bool CanCancelScheduled() =>
        IsActiveView && HasUnlockedSelection() && SelectedRows.All(r => r.Message.State == MessageState.Scheduled);

    private bool CanResubmit() => IsDeadLetterView && HasUnlockedSelection();
    private bool CanEditAndResubmit() => IsDeadLetterView && !HasLockedMessages && SelectedMessage is not null;
    private bool CanDelete() => CanReceive && HasUnlockedSelection();
    private bool CanPurge() => (CanReceive || PurgesAllSessions) && CanBrowse && !HasLockedMessages;

    private string KindName => Entity.Kind == EntityKind.Subscription ? "subscription" : "queue";

    private string SourceLabel => IsSessionView ? $"session '{SessionId}' of {Source.EntityPath}" : SelectedSubQueue.Kind switch
    {
        SubQueueKind.DeadLetter => $"the dead-letter queue of {Source.EntityPath}",
        SubQueueKind.TransferDeadLetter => $"the transfer dead-letter queue of {Source.EntityPath}",
        _ => Source.EntityPath,
    };

    private static string Plural(int count) => count == 1 ? "1 message" : $"{count:N0} messages";

    private static string Missing(int requested, int done) =>
        requested == done ? string.Empty : $" {requested - done} could not be found; they may have been received elsewhere.";

    private static string SequenceRange(IReadOnlyList<MessageInfo> messages) =>
        messages.Count == 0 ? string.Empty : $" (sequence {messages[0].SequenceNumber}–{messages[^1].SequenceNumber})";
}
