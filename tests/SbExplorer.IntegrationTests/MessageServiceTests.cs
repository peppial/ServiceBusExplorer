using System.Text;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Messaging;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

/// <summary>
/// Round trips through the emulator's sbx-tests queue. Tests in one class run sequentially, and each one
/// drains the queue first so they don't see each other's messages.
/// </summary>
public sealed class MessageServiceTests : IAsyncLifetime
{
    private static readonly MessageSource Queue = MessageSource.ForQueue("sbx-tests");
    private static readonly MessageSource DeadLetters = MessageSource.ForQueue("sbx-tests", SubQueueKind.DeadLetter);
    private INamespaceSession? _session;

    private IMessageService Messages => _session!.Messages;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Emulator.SkipIfNotRunning();
        _session = await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "Emulator" }, Emulator.ConnectionString, Ct);
        await Messages.ReceiveAndDeleteAsync(Queue, 1000, TimeSpan.FromSeconds(1), Ct);
        await Messages.ReceiveAndDeleteAsync(DeadLetters, 1000, TimeSpan.FromSeconds(1), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Send_then_peek_returns_body_and_properties()
    {
        await Messages.SendAsync("sbx-tests",
        [
            new OutgoingMessage
            {
                Body = "{\"n\":1}"u8.ToArray(),
                ContentType = "application/json",
                MessageId = "peek-1",
                Subject = "test",
                ApplicationProperties = new Dictionary<string, object?> { ["attempt"] = 3, ["region"] = "eu" },
            },
        ], Ct);

        var peeked = Assert.Single(await Messages.PeekAsync(Queue, 10, cancellationToken: Ct));

        Assert.Equal(("peek-1", "test", "application/json"), (peeked.MessageId, peeked.Subject, peeked.ContentType));
        Assert.Equal("{\"n\":1}", Encoding.UTF8.GetString(peeked.Body.Span));
        Assert.Equal(3, peeked.ApplicationProperties["attempt"]);
        Assert.Null(peeked.LockToken);
    }

    [Fact]
    public async Task Peek_pages_by_sequence_number()
    {
        await Messages.SendAsync("sbx-tests", Enumerable.Range(0, 5).Select(i => new OutgoingMessage { Body = new[] { (byte)i } }).ToList(), Ct);

        var first = await Messages.PeekAsync(Queue, 3, cancellationToken: Ct);
        var rest = await Messages.PeekAsync(Queue, 10, first[^1].SequenceNumber + 1, Ct);

        Assert.Equal(3, first.Count);
        Assert.Equal(2, rest.Count);
        Assert.True(rest[0].SequenceNumber > first[^1].SequenceNumber);
    }

    [Fact]
    public async Task Peek_lock_complete_and_dead_letter()
    {
        await Messages.SendAsync("sbx-tests", [new OutgoingMessage { MessageId = "a" }, new OutgoingMessage { MessageId = "b" }], Ct);

        await using (var batch = await Messages.ReceivePeekLockAsync(Queue, 2, TimeSpan.FromSeconds(5), Ct))
        {
            Assert.Equal(2, batch.Messages.Count);
            Assert.All(batch.Messages, m => Assert.NotNull(m.LockToken));
            await batch.CompleteAsync(batch.Messages.Single(m => m.MessageId == "a"), Ct);
            await batch.DeadLetterAsync(batch.Messages.Single(m => m.MessageId == "b"), "TestReason", "from a test", Ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => batch.CompleteAsync(batch.Messages[0], Ct));
        }

        Assert.Empty(await Messages.PeekAsync(Queue, 10, cancellationToken: Ct));
        var dead = Assert.Single(await Messages.PeekAsync(DeadLetters, 10, cancellationToken: Ct));
        Assert.Equal(("b", "TestReason", "from a test"), (dead.MessageId, dead.DeadLetterReason, dead.DeadLetterErrorDescription));
    }

    [Fact]
    public async Task Disposing_a_batch_abandons_unsettled_messages()
    {
        await Messages.SendAsync("sbx-tests", [new OutgoingMessage { MessageId = "keep" }], Ct);

        await using (await Messages.ReceivePeekLockAsync(Queue, 1, TimeSpan.FromSeconds(5), Ct))
        {
        }

        var again = Assert.Single(await Messages.PeekAsync(Queue, 10, cancellationToken: Ct));
        Assert.Equal(1, again.DeliveryCount);
    }

    [Fact]
    public async Task Receive_and_delete_drains_up_to_the_limit()
    {
        await Messages.SendAsync("sbx-tests", Enumerable.Range(0, 4).Select(_ => new OutgoingMessage()).ToList(), Ct);

        var received = await Messages.ReceiveAndDeleteAsync(Queue, 3, TimeSpan.FromSeconds(5), Ct);
        var remaining = await Messages.PeekAsync(Queue, 10, cancellationToken: Ct);

        Assert.Equal(3, received.Count);
        Assert.Single(remaining);
    }
}
