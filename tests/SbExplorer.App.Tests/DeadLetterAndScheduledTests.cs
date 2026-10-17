using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class DeadLetterAndScheduledTests
{
    private const string OrdersDlq = "orders/$DeadLetterQueue";

    private readonly FakeMessageService _service = new();
    private readonly FakeDialogs _dialogs = new();

    private static readonly EntityInfo Orders = new() { Kind = EntityKind.Queue, Name = "orders", Path = "orders" };

    private static readonly EntityInfo Billing = new()
    {
        Kind = EntityKind.Subscription,
        Name = "billing",
        Path = "events/Subscriptions/billing",
        ForwardTo = "audit",
    };

    private async Task<MessagesViewModel> DeadLetterViewAsync(EntityInfo? entity = null)
    {
        var vm = new MessagesViewModel(_service, _dialogs, entity ?? Orders);
        vm.SelectedSubQueue = vm.SubQueues[1];
        await vm.PeekCommand.ExecuteAsync(null);
        return vm;
    }

    private void SeedDeadLetters(int count) =>
        _service.Seed(OrdersDlq, count, i => new MessageInfo
        {
            MessageId = $"dead-{i}",
            DeadLetterReason = "ValidationFailed",
            Body = "x"u8.ToArray(),
        });

    [Fact]
    public async Task Switching_to_dead_letter_peeks_it_and_changes_available_actions()
    {
        SeedDeadLetters(3);
        var vm = await DeadLetterViewAsync();

        Assert.Equal(SubQueueKind.DeadLetter, vm.Source.SubQueue);
        Assert.Equal(3, vm.Messages.Count);
        Assert.True(vm.IsDeadLetterView);
        Assert.True(vm.ResubmitCommand.CanExecute(null));
        Assert.False(vm.CancelScheduledCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dead_letter_queue_of_a_forwarding_subscription_is_browsable()
    {
        var vm = new MessagesViewModel(_service, _dialogs, Billing);
        Assert.False(vm.CanBrowse);

        vm.SelectedSubQueue = vm.SubQueues[1];

        Assert.True(vm.CanBrowse);
        Assert.True(vm.PeekCommand.CanExecute(null));
        Assert.Contains("every subscription whose filter matches", vm.Notice);
    }

    [Fact]
    public async Task Resubmit_sends_selected_to_the_chosen_target_and_removes_them()
    {
        SeedDeadLetters(3);
        var vm = await DeadLetterViewAsync();
        vm.SelectedRows = [vm.Messages[0], vm.Messages[2]];
        _dialogs.PromptResult = " orders-retry ";

        await vm.ResubmitCommand.ExecuteAsync(null);

        var resubmitted = _service.Resubmitted.Single();
        Assert.Equal("orders-retry", resubmitted.Target);
        Assert.Equal([1L, 3L], resubmitted.Sequences);
        Assert.Equal([2L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Equal("Resubmitted 2 messages to orders-retry.", vm.Status);
    }

    [Fact]
    public async Task Resubmit_defaults_to_the_original_entity_and_cancels_on_empty_target()
    {
        SeedDeadLetters(1);
        var vm = await DeadLetterViewAsync();

        _dialogs.PromptResult = null;
        await vm.ResubmitCommand.ExecuteAsync(null);
        Assert.Empty(_service.Resubmitted);
        Assert.Single(vm.Messages);
    }

    [Fact]
    public async Task Partial_results_explain_missing_messages()
    {
        SeedDeadLetters(2);
        _service.Missing.Add(2);
        var vm = await DeadLetterViewAsync();
        vm.SelectedRows = vm.Messages.ToList();
        _dialogs.ConfirmResult = true;

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([2L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Equal("Deleted 1 message. 1 could not be found; they may have been received elsewhere.", vm.Status);
    }

    [Fact]
    public async Task Edit_and_resubmit_sends_the_edited_copy_then_deletes_the_original()
    {
        SeedDeadLetters(2);
        var vm = await DeadLetterViewAsync();
        _dialogs.OnSend = async sender =>
        {
            Assert.Equal("Edit and resubmit message 1", sender.Title);
            Assert.Equal("orders", sender.Target);
            sender.Body = "fixed";
            await sender.SendCommand.ExecuteAsync(null);
            return true;
        };

        await vm.EditAndResubmitCommand.ExecuteAsync(null);

        var sent = _service.Sent.Single();
        Assert.Equal("fixed", System.Text.Encoding.UTF8.GetString(sent.Message.Body.Span));
        Assert.Equal([2L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Equal([2L], _service.Messages(OrdersDlq).Select(m => m.SequenceNumber));
    }

    [Fact]
    public async Task Purge_needs_confirmation_and_clears_everything()
    {
        SeedDeadLetters(4);
        var vm = await DeadLetterViewAsync();

        await vm.PurgeCommand.ExecuteAsync(null);
        Assert.Equal(4, _service.Messages(OrdersDlq).Count);

        _dialogs.ConfirmResult = true;
        await vm.PurgeCommand.ExecuteAsync(null);
        Assert.Empty(_service.Messages(OrdersDlq));
        Assert.Empty(vm.Messages);
        Assert.Equal("Purged 4 messages from the dead-letter queue of orders.", vm.Status);
    }

    [Fact]
    public void Active_queue_of_a_session_entity_cannot_be_purged_here()
    {
        var vm = new MessagesViewModel(_service, _dialogs, Orders with { RequiresSession = true });

        Assert.False(vm.PurgeCommand.CanExecute(null));
        vm.SelectedSubQueue = vm.SubQueues[1];
        Assert.True(vm.PurgeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Deferred_messages_are_received_by_sequence_number_and_settled()
    {
        _service.Seed("orders", 3, i => new MessageInfo { State = i == 1 ? MessageState.Deferred : MessageState.Active });
        var vm = new MessagesViewModel(_service, _dialogs, Orders);
        await vm.PeekCommand.ExecuteAsync(null);

        vm.SelectedRows = [vm.Messages[0]];
        Assert.False(vm.ReceiveDeferredCommand.CanExecute(null));

        vm.SelectedRows = [vm.Messages[1]];
        await vm.ReceiveDeferredCommand.ExecuteAsync(null);
        Assert.Equal([2L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Equal(1, vm.LockedCount);

        await vm.CompleteCommand.ExecuteAsync(null);
        Assert.Equal(("Complete", 2L, (string?)null), _service.Batches.Single().Settled.Single());
    }

    [Fact]
    public async Task Scheduled_messages_can_be_cancelled()
    {
        _service.Seed("orders", 3, i => new MessageInfo { State = i == 0 ? MessageState.Active : MessageState.Scheduled });
        var vm = new MessagesViewModel(_service, _dialogs, Orders);
        await vm.PeekCommand.ExecuteAsync(null);

        vm.SelectedRows = vm.Messages.ToList();
        Assert.False(vm.CancelScheduledCommand.CanExecute(null));

        vm.SelectedRows = [vm.Messages[1], vm.Messages[2]];
        _dialogs.ConfirmResult = true;
        await vm.CancelScheduledCommand.ExecuteAsync(null);

        var cancelled = _service.Cancelled.Single();
        Assert.Equal("orders", cancelled.Entity);
        Assert.Equal([2L, 3L], cancelled.Sequences);
        Assert.Equal([1L], vm.Messages.Select(m => m.SequenceNumber));
    }

    [Fact]
    public async Task Bulk_actions_are_blocked_while_holding_locks()
    {
        SeedDeadLetters(2);
        var vm = await DeadLetterViewAsync();

        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        Assert.False(vm.ResubmitCommand.CanExecute(null));
        Assert.False(vm.PurgeCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Switching_views_releases_locks()
    {
        _service.Seed("orders", 2);
        var vm = new MessagesViewModel(_service, _dialogs, Orders);
        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        vm.SelectedSubQueue = vm.SubQueues[1];

        Assert.True(_service.Batches.Single().Disposed);
        Assert.False(vm.HasLockedMessages);
    }
}
