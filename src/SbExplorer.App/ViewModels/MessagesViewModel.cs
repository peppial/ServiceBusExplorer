using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

/// <summary>The Messages tab for one queue or subscription.</summary>
public partial class MessagesViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(5);

    private readonly IMessageService _service;
    private readonly IDialogService _dialogs;
    private IReceivedBatch? _lockedBatch;

    public MessagesViewModel(IMessageService service, IDialogService dialogs, EntityInfo entity)
    {
        _service = service;
        _dialogs = dialogs;
        Entity = entity;
        Source = entity.Kind == EntityKind.Subscription
            ? MessageSource.ForSubscription(entity.Path.Split('/')[0], entity.Name)
            : MessageSource.ForQueue(entity.Name);
    }

    public EntityInfo Entity { get; }
    public MessageSource Source { get; }
    public ObservableCollection<MessageRowViewModel> Messages { get; } = [];

    /// <summary>Service Bus refuses to browse or receive from an entity that auto-forwards.</summary>
    public bool CanBrowse => Entity.ForwardTo is null;

    /// <summary>Receiving from a session entity needs a session receiver, which the Sessions tab provides.</summary>
    public bool CanReceive => CanBrowse && !Entity.RequiresSession;

    public string? Notice => !CanBrowse
        ? $"This {KindName} forwards every message to '{Entity.ForwardTo}', so Service Bus doesn't allow browsing or receiving here. Open '{Entity.ForwardTo}' instead."
        : Entity.RequiresSession
            ? $"This {KindName} requires sessions. Peek shows messages from all sessions; receiving happens per session."
            : null;

    public string SendLabel => Source.IsSubscription ? $"Send to topic '{Source.SendTarget}'…" : "Send…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekCommand), nameof(PeekNextCommand), nameof(ReceiveAndDeleteCommand), nameof(ReceiveLockedCommand))]
    private int _maxMessages = 50;

    [ObservableProperty]
    private string _fromSequence = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand), nameof(AbandonCommand), nameof(DeadLetterCommand), nameof(DeferCommand), nameof(ResendCommand))]
    private MessageRowViewModel? _selectedMessage;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedMessages))]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand), nameof(PeekCommand), nameof(PeekNextCommand), nameof(ReceiveAndDeleteCommand), nameof(ReceiveLockedCommand))]
    private int _lockedCount;

    [ObservableProperty]
    private string? _lockedUntil;

    public bool HasLockedMessages => LockedCount > 0;
    public bool HasMoreToPeek { get; private set; }

    public MessageDetailViewModel? Detail => SelectedMessage is null ? null : new MessageDetailViewModel(SelectedMessage.Message, _dialogs);

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
            $"Permanently remove up to {Plural(MaxMessages)} from {Source.EntityPath}? They can't be put back.",
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
            if (batch.Messages.Count == 0)
            {
                await batch.DisposeAsync();
                ShowMessages([], append: false);
                return "No messages to receive.";
            }

            _lockedBatch = batch;
            ShowMessages(batch.Messages, append: false);
            UpdateLockState();
            return $"Locked {Plural(batch.Messages.Count)}. Complete, abandon, dead-letter or defer each one, or release them all.";
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
        foreach (var row in Messages.Where(r => r.Message.LockToken is not null).ToList())
        {
            Messages.Remove(row);
        }

        SetStatus($"Released {Plural(count)} back to {Source.EntityPath}.");
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
        if (await _dialogs.ShowSendMessageAsync(sender))
        {
            SetStatus($"Sent {Plural(sender.SentCount)} to {Source.SendTarget}.");
        }
    }

    private async Task SettleAsync(string verb, Func<IReceivedBatch, MessageInfo, Task> settle)
    {
        var row = SelectedMessage!;
        try
        {
            await settle(_lockedBatch!, row.Message);
            var index = Messages.IndexOf(row);
            Messages.Remove(row);
            SelectedMessage = Messages.Count == 0 ? null : Messages[Math.Min(index, Messages.Count - 1)];
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
        }

        foreach (var message in messages)
        {
            Messages.Add(new MessageRowViewModel(message));
        }

        if (!append)
        {
            SelectedMessage = Messages.FirstOrDefault();
        }
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

    private void SetStatus(string message, bool isError = false)
    {
        Status = message;
        HasError = isError;
    }

    // Holding locks while peeking or receiving again would confuse which rows can be settled.
    private bool CanStartRead() => CanBrowse && !HasLockedMessages && MaxMessages > 0;
    private bool CanStartReceive() => CanReceive && !HasLockedMessages && MaxMessages > 0;
    private bool CanPeekNext() => CanStartRead() && HasMoreToPeek && Messages.Count > 0;
    private bool HasSelectedMessage() => SelectedMessage is not null;
    private bool IsSelectedLocked() => _lockedBatch is not null && SelectedMessage?.Message.LockToken is not null;

    private string KindName => Entity.Kind == EntityKind.Subscription ? "subscription" : "queue";

    private static string Plural(int count) => count == 1 ? "1 message" : $"{count:N0} messages";

    private static string SequenceRange(IReadOnlyList<MessageInfo> messages) =>
        messages.Count == 0 ? string.Empty : $" (sequence {messages[0].SequenceNumber}–{messages[^1].SequenceNumber})";
}
