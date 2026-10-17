using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class MessagesViewModelTests
{
    private readonly FakeMessageService _service = new();
    private readonly FakeDialogs _dialogs = new();

    private static readonly EntityInfo Orders = new() { Kind = EntityKind.Queue, Name = "orders", Path = "orders" };

    private MessagesViewModel Create(EntityInfo? entity = null) => new(_service, _dialogs, entity ?? Orders);

    [Fact]
    public async Task Peek_shows_messages_and_selects_the_first()
    {
        _service.Seed("orders", 3);
        var vm = Create();

        await vm.PeekCommand.ExecuteAsync(null);

        Assert.Equal([1L, 2L, 3L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Same(vm.Messages[0], vm.SelectedMessage);
        Assert.Equal("Peeked 3 messages (sequence 1–3).", vm.Status);
        Assert.False(vm.PeekNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Peek_next_continues_after_the_last_sequence_number()
    {
        _service.Seed("orders", 5);
        var vm = Create();
        vm.MaxMessages = 2;

        await vm.PeekCommand.ExecuteAsync(null);
        Assert.True(vm.PeekNextCommand.CanExecute(null));
        await vm.PeekNextCommand.ExecuteAsync(null);
        await vm.PeekNextCommand.ExecuteAsync(null);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.False(vm.PeekNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Peek_from_sequence_number()
    {
        _service.Seed("orders", 5);
        var vm = Create();
        vm.FromSequence = "4";

        await vm.PeekCommand.ExecuteAsync(null);

        Assert.Equal([4L, 5L], vm.Messages.Select(m => m.SequenceNumber));
    }

    [Fact]
    public async Task Peek_errors_are_shown()
    {
        _service.Failure = new InvalidOperationException("The messaging entity could not be found.");
        var vm = Create();

        await vm.PeekCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("The messaging entity could not be found.", vm.Status);
    }

    [Fact]
    public void Forwarding_entities_cannot_be_browsed()
    {
        var vm = Create(Orders with { ForwardTo = "audit" });

        Assert.False(vm.CanBrowse);
        Assert.False(vm.PeekCommand.CanExecute(null));
        Assert.False(vm.ReceiveLockedCommand.CanExecute(null));
        Assert.Contains("forwards every message to 'audit'", vm.Notice);
    }

    [Fact]
    public void Session_entities_can_be_peeked_but_not_received_here()
    {
        var vm = Create(Orders with { RequiresSession = true });

        Assert.True(vm.PeekCommand.CanExecute(null));
        Assert.False(vm.ReceiveLockedCommand.CanExecute(null));
        Assert.False(vm.ReceiveAndDeleteCommand.CanExecute(null));
        Assert.Contains("requires sessions", vm.Notice);
    }

    [Fact]
    public async Task Receive_and_delete_needs_confirmation()
    {
        _service.Seed("orders", 3);
        var vm = Create();

        await vm.ReceiveAndDeleteCommand.ExecuteAsync(null);
        Assert.Equal(3, _service.Messages("orders").Count);

        _dialogs.ConfirmResult = true;
        await vm.ReceiveAndDeleteCommand.ExecuteAsync(null);
        Assert.Empty(_service.Messages("orders"));
        Assert.Equal(3, vm.Messages.Count);
        Assert.Equal("Received and deleted 3 messages.", vm.Status);
    }

    [Fact]
    public async Task Locked_messages_are_settled_one_by_one()
    {
        _service.Seed("orders", 3);
        var vm = Create();

        await vm.ReceiveLockedCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.LockedCount);
        Assert.Equal("Locked", vm.Messages[0].State);
        Assert.False(vm.PeekCommand.CanExecute(null));

        await vm.CompleteCommand.ExecuteAsync(null);
        await vm.DeferCommand.ExecuteAsync(null);

        var batch = _service.Batches.Single();
        Assert.Equal([("Complete", 1L, (string?)null), ("Defer", 2L, null)], batch.Settled);
        Assert.Equal([3L], vm.Messages.Select(m => m.SequenceNumber));
        Assert.Equal(1, vm.LockedCount);
        Assert.Same(vm.Messages[0], vm.SelectedMessage);
    }

    [Fact]
    public async Task Settling_the_last_message_releases_the_receiver()
    {
        _service.Seed("orders", 1);
        var vm = Create();
        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        await vm.AbandonCommand.ExecuteAsync(null);

        Assert.True(_service.Batches.Single().Disposed);
        Assert.False(vm.HasLockedMessages);
        Assert.True(vm.PeekCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dead_letter_asks_for_a_reason()
    {
        _service.Seed("orders", 2);
        var vm = Create();
        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        _dialogs.PromptResult = null;
        await vm.DeadLetterCommand.ExecuteAsync(null);
        Assert.Empty(_service.Batches.Single().Settled);

        _dialogs.PromptResult = "BadAddress";
        await vm.DeadLetterCommand.ExecuteAsync(null);
        Assert.Equal(("DeadLetter", 1L, "BadAddress"), _service.Batches.Single().Settled.Single());
    }

    [Fact]
    public async Task Release_all_abandons_by_disposing_and_clears_locked_rows()
    {
        _service.Seed("orders", 3);
        var vm = Create();
        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        await vm.ReleaseCommand.ExecuteAsync(null);

        Assert.True(_service.Batches.Single().Disposed);
        Assert.Empty(vm.Messages);
        Assert.Equal("Released 3 messages back to orders.", vm.Status);
    }

    [Fact]
    public async Task Receiving_nothing_does_not_hold_a_receiver()
    {
        var vm = Create();

        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        Assert.True(_service.Batches.Single().Disposed);
        Assert.Equal("No messages to receive.", vm.Status);
    }

    [Fact]
    public async Task Disposing_releases_locks()
    {
        _service.Seed("orders", 1);
        var vm = Create();
        await vm.ReceiveLockedCommand.ExecuteAsync(null);

        await vm.DisposeAsync();

        Assert.True(_service.Batches.Single().Disposed);
    }

    [Fact]
    public async Task Send_from_a_subscription_targets_its_topic()
    {
        var subscription = new EntityInfo { Kind = EntityKind.Subscription, Name = "all-events", Path = "events/Subscriptions/all-events" };
        var vm = Create(subscription);
        _dialogs.OnSend = async sender =>
        {
            sender.Body = "hello";
            await sender.SendCommand.ExecuteAsync(null);
            return true;
        };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Send to topic 'events'…", vm.SendLabel);
        Assert.Equal("events", _service.Sent.Single().Entity);
        Assert.Equal("Sent 1 message to events.", vm.Status);
    }

    [Fact]
    public async Task Resend_prefills_the_selected_message()
    {
        _service.Seed("orders", 1, _ => new MessageInfo
        {
            MessageId = "order-1",
            Subject = "order-created",
            ContentType = "application/json",
            Body = """{"id":1}"""u8.ToArray(),
            ApplicationProperties = new Dictionary<string, object?> { ["priority"] = "high", ["attempt"] = 2 },
        });
        var vm = Create();
        await vm.PeekCommand.ExecuteAsync(null);

        await vm.ResendCommand.ExecuteAsync(null);

        var sent = _service.Sent.Single().Message;
        Assert.Null(sent.MessageId);
        Assert.Equal("order-created", sent.Subject);
        Assert.Equal("{\n  \"id\": 1\n}", System.Text.Encoding.UTF8.GetString(sent.Body.Span).ReplaceLineEndings("\n"));
        Assert.Equal(2, sent.ApplicationProperties["attempt"]);
    }
}
