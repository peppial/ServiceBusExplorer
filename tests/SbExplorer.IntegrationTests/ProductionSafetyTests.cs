using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

/// <summary>
/// Things that must not happen to a production namespace just because someone browsed or repaired it here.
/// Each test uses its own entities on the emulator and removes them afterwards.
/// </summary>
public sealed class ProductionSafetyTests : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly ServiceBusAdministrationClient _admin = new(ConnectionStringInfo.ForAdministration(Emulator.ConnectionString));
    private readonly ServiceBusClient _client = new(Emulator.ConnectionString);
    private readonly List<Func<Task>> _cleanup = [];
    private INamespaceSession? _session;

    private IMessageService Messages => _session!.Messages;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Emulator.SkipIfNotRunning();
        _session = await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "Emulator" }, Emulator.ConnectionString, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cleanup in _cleanup)
        {
            try
            {
                await cleanup();
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        await _client.DisposeAsync();
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Resubmitting_into_an_entity_with_duplicate_detection_does_not_lose_the_message()
    {
        var queue = $"safety-dup-{_suffix}";
        await _admin.CreateQueueAsync(new CreateQueueOptions(queue)
        {
            RequiresDuplicateDetection = true,
            DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5),
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(30),
        }, Ct);
        _cleanup.Add(() => _admin.DeleteQueueAsync(queue));

        await Messages.SendAsync(queue, [new OutgoingMessage { MessageId = "order-1", Body = "{}"u8.ToArray() }], Ct);
        await using (var batch = await Messages.ReceivePeekLockAsync(MessageSource.ForQueue(queue), 1, TimeSpan.FromSeconds(5), Ct))
        {
            await batch.DeadLetterAsync(batch.Messages.Single(), "Poison", null, Ct);
        }

        var dead = Assert.Single(await Messages.PeekAsync(MessageSource.ForQueue(queue, SubQueueKind.DeadLetter), 10, cancellationToken: Ct));
        await Messages.ResubmitAsync(MessageSource.ForQueue(queue, SubQueueKind.DeadLetter), [dead.SequenceNumber], queue, Ct);

        // The broker remembers "order-1" for five minutes; a copy with the same MessageId would be silently dropped
        // while the dead-lettered original is deleted.
        var back = Assert.Single(await Messages.PeekAsync(MessageSource.ForQueue(queue), 10, cancellationToken: Ct));
        Assert.Equal("order-1", back.ApplicationProperties["OriginalMessageId"]);
    }

    [Fact]
    public async Task Peeking_a_session_does_not_lock_it_away_from_consumers()
    {
        // Its own queue: the shared sbx-sessions queue is purged by other tests running in parallel.
        var queue = $"safety-sessions-{_suffix}";
        await _admin.CreateQueueAsync(new CreateQueueOptions(queue) { RequiresSession = true, DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        _cleanup.Add(() => _admin.DeleteQueueAsync(queue));
        await Messages.SendAsync(queue, [new OutgoingMessage { SessionId = "live", Body = "x"u8.ToArray() }], Ct);

        Assert.Single(await Messages.PeekAsync(MessageSource.ForQueue(queue).ForSession("live"), 10, cancellationToken: Ct));

        // A consumer accepting the session right after the peek must get it.
        await using var consumer = await _client.AcceptSessionAsync(queue, "live", cancellationToken: Ct);
        var received = Assert.Single(await consumer.ReceiveMessagesAsync(1, TimeSpan.FromSeconds(5), Ct));
        await consumer.CompleteMessageAsync(received, Ct);
    }

    [Fact]
    public async Task Updating_a_rule_keeps_typed_correlation_properties_and_sql_parameters()
    {
        var topic = $"safety-rules-{_suffix}";
        await _admin.CreateTopicAsync(new CreateTopicOptions(topic) { DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        _cleanup.Add(() => _admin.DeleteTopicAsync(topic));
        await _admin.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, "s") { DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        var correlation = new CorrelationRuleFilter { Subject = "invoice" };
        correlation.ApplicationProperties["tier"] = 5;
        correlation.ApplicationProperties["vip"] = true;
        await _admin.CreateRuleAsync(topic, "s", new CreateRuleOptions("typed", correlation), Ct);
        var sql = new SqlRuleFilter("amount > @min");
        sql.Parameters["@min"] = 100;
        await _admin.CreateRuleAsync(topic, "s", new CreateRuleOptions("parameters", sql), Ct);

        // Open each rule in the editor's model and save it unchanged, as the Update button does.
        foreach (var name in new[] { "typed", "parameters" })
        {
            var definition = await _session!.Entities.GetRuleAsync(topic, "s", name, Ct);
            await _session.Entities.UpdateRuleAsync(topic, "s", definition, Ct);
        }

        var typed = (CorrelationRuleFilter)(await _admin.GetRuleAsync(topic, "s", "typed", Ct)).Value.Filter;
        Assert.Equal(5, typed.ApplicationProperties["tier"]);
        Assert.Equal(true, typed.ApplicationProperties["vip"]);
        var parameters = (SqlRuleFilter)(await _admin.GetRuleAsync(topic, "s", "parameters", Ct)).Value.Filter;
        Assert.Equal(100, parameters.Parameters["@min"]);
    }

    [Fact]
    public async Task Purge_keeps_messages_that_arrive_after_it_started()
    {
        var queue = $"safety-purge-{_suffix}";
        await _admin.CreateQueueAsync(new CreateQueueOptions(queue) { DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        _cleanup.Add(() => _admin.DeleteQueueAsync(queue));
        await Messages.SendAsync(queue, Enumerable.Range(0, 30).Select(i => new OutgoingMessage { MessageId = $"old-{i}" }).ToList(), Ct);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Ct);
        var purgeStarted = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromSeconds(1.5), Ct);
        await Messages.SendAsync(queue, Enumerable.Range(0, 5).Select(i => new OutgoingMessage { MessageId = $"new-{i}" }).ToList(), Ct);

        var purged = await Messages.PurgeAsync(MessageSource.ForQueue(queue), cancellationToken: Ct, enqueuedBefore: purgeStarted);

        Assert.Equal(30, purged);
        var left = await Messages.PeekAsync(MessageSource.ForQueue(queue), 50, cancellationToken: Ct);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"new-{i}"), left.Select(m => m.MessageId));
        Assert.All(left, m => Assert.Equal(1, m.DeliveryCount));
    }

    [Fact]
    public async Task Peeking_messages_never_changes_them()
    {
        var queue = $"safety-peek-{_suffix}";
        await _admin.CreateQueueAsync(new CreateQueueOptions(queue) { DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        _cleanup.Add(() => _admin.DeleteQueueAsync(queue));
        await Messages.SendAsync(queue, [new OutgoingMessage { MessageId = "peek-only" }], Ct);

        for (var i = 0; i < 3; i++)
        {
            await Messages.PeekAsync(MessageSource.ForQueue(queue), 100, cancellationToken: Ct);
        }

        var message = Assert.Single(await Messages.PeekAsync(MessageSource.ForQueue(queue), 100, cancellationToken: Ct));
        Assert.Equal((0, MessageState.Active), (message.DeliveryCount, message.State));
    }

    [Fact]
    public async Task A_read_only_connection_peeks_but_cannot_send_or_change_anything()
    {
        var queue = $"safety-ro-{_suffix}";
        await _admin.CreateQueueAsync(new CreateQueueOptions(queue) { DefaultMessageTimeToLive = TimeSpan.FromMinutes(30) }, Ct);
        _cleanup.Add(() => _admin.DeleteQueueAsync(queue));
        await Messages.SendAsync(queue, [new OutgoingMessage { MessageId = "existing" }], Ct);

        await using var readOnly = await new AzureNamespaceConnector()
            .ConnectAsync(new ConnectionProfile { Name = "prod", ReadOnly = true }, Emulator.ConnectionString, Ct);

        Assert.Single(await readOnly.Messages.PeekAsync(MessageSource.ForQueue(queue), 10, cancellationToken: Ct));
        await Assert.ThrowsAsync<ReadOnlyConnectionException>(() =>
            readOnly.Messages.SendAsync(queue, [new OutgoingMessage { MessageId = "should-not-arrive" }], Ct));
        await Assert.ThrowsAsync<ReadOnlyConnectionException>(() => readOnly.Messages.PurgeAsync(MessageSource.ForQueue(queue), cancellationToken: Ct));
        await Assert.ThrowsAsync<ReadOnlyConnectionException>(() =>
            readOnly.Entities.DeleteAsync(new EntityInfo { Kind = EntityKind.Queue, Name = queue, Path = queue }, Ct));

        var left = Assert.Single(await Messages.PeekAsync(MessageSource.ForQueue(queue), 10, cancellationToken: Ct));
        Assert.Equal(("existing", 0), (left.MessageId, left.DeliveryCount));
        Assert.Contains(await _session!.Entities.GetQueuesAsync(Ct), q => q.Name == queue);
    }
}
