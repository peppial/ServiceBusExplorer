using System.Text;
using Azure.Messaging.ServiceBus;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Messaging;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

/// <summary>Runs against the session-enabled sbx-sessions queue; each test starts from an empty queue.</summary>
public sealed class SessionTests : IAsyncLifetime
{
    private static readonly MessageSource Queue = MessageSource.ForQueue("sbx-sessions");
    private INamespaceSession? _session;

    private IMessageService Messages => _session!.Messages;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Emulator.SkipIfNotRunning();
        _session = await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "Emulator" }, Emulator.ConnectionString, Ct);
        await Messages.PurgeAllSessionsAsync(Queue, cancellationToken: Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private Task SendAsync(params (string Session, string Body)[] messages) =>
        Messages.SendAsync("sbx-sessions", messages.Select(m => new OutgoingMessage
        {
            SessionId = m.Session,
            Body = Encoding.UTF8.GetBytes(m.Body),
        }).ToList(), Ct);

    [Fact]
    public async Task Peek_and_receive_within_one_session()
    {
        await SendAsync(("a", "a1"), ("b", "b1"), ("a", "a2"));

        var peeked = await Messages.PeekAsync(Queue.ForSession("a"), 10, cancellationToken: Ct);
        Assert.Equal(["a1", "a2"], peeked.Select(m => Encoding.UTF8.GetString(m.Body.Span)));

        await using (var batch = await Messages.ReceivePeekLockAsync(Queue.ForSession("a"), 10, TimeSpan.FromSeconds(5), Ct))
        {
            Assert.Equal(2, batch.Messages.Count);
            foreach (var message in batch.Messages)
            {
                await batch.CompleteAsync(message, Ct);
            }
        }

        Assert.Empty(await Messages.PeekAsync(Queue.ForSession("a"), 10, cancellationToken: Ct));
        Assert.Single(await Messages.PeekAsync(Queue.ForSession("b"), 10, cancellationToken: Ct));
    }

    [Fact]
    public async Task Session_state_round_trips_and_clears()
    {
        await SendAsync(("s", "x"));
        var source = Queue.ForSession("s");

        Assert.Null(await Messages.GetSessionStateAsync(source, Ct));
        await Messages.SetSessionStateAsync(source, "{\"step\":3}"u8.ToArray(), Ct);
        Assert.Equal("{\"step\":3}", Encoding.UTF8.GetString((await Messages.GetSessionStateAsync(source, Ct))!.Value.Span));

        await Messages.SetSessionStateAsync(source, null, Ct);
        Assert.Null(await Messages.GetSessionStateAsync(source, Ct));
    }

    [Fact]
    public async Task Peek_falls_back_to_scanning_when_a_consumer_holds_the_session()
    {
        await SendAsync(("locked", "l1"), ("other", "o1"));
        await using var client = new ServiceBusClient(Emulator.ConnectionString);
        await using var consumer = await client.AcceptSessionAsync("sbx-sessions", "locked", cancellationToken: Ct);

        var peeked = await Messages.PeekAsync(Queue.ForSession("locked"), 10, cancellationToken: Ct);

        Assert.Equal("l1", Encoding.UTF8.GetString(Assert.Single(peeked).Body.Span));
    }

    [Fact]
    public async Task Purge_all_sessions_empties_the_queue()
    {
        await SendAsync(("a", "1"), ("b", "2"), ("c", "3"), ("c", "4"));

        var purged = await Messages.PurgeAllSessionsAsync(Queue, cancellationToken: Ct);

        Assert.Equal(4, purged);
        Assert.Empty(await Messages.PeekAsync(Queue, 10, cancellationToken: Ct));
    }
}
