using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Tests;

public class ReadOnlyServicesTests
{
    private static readonly MessageSource Orders = MessageSource.ForQueue("orders");

    [Fact]
    public async Task Entity_reads_pass_through_and_every_change_is_refused()
    {
        var inner = new InMemoryEntityService();
        inner.Add(new QueueDefinition { Name = "orders" });
        var service = new ReadOnlyEntityService(inner);
        var entity = new EntityInfo { Kind = EntityKind.Queue, Name = "orders", Path = "orders" };

        Assert.Single(await service.GetQueuesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("orders", (await service.GetQueueAsync("orders", TestContext.Current.CancellationToken)).Name);

        Func<Task>[] writes =
        [
            () => service.CreateQueueAsync(new QueueDefinition { Name = "x" }),
            () => service.CreateTopicAsync(new TopicDefinition { Name = "x" }),
            () => service.CreateSubscriptionAsync(new SubscriptionDefinition { Topic = "t", Name = "x" }),
            () => service.CreateRuleAsync("t", "s", new RuleDefinition { Name = "x" }),
            () => service.UpdateQueueAsync(new QueueDefinition { Name = "orders" }),
            () => service.UpdateTopicAsync(new TopicDefinition { Name = "t" }),
            () => service.UpdateSubscriptionAsync(new SubscriptionDefinition { Topic = "t", Name = "s" }),
            () => service.UpdateRuleAsync("t", "s", new RuleDefinition { Name = "r" }),
            () => service.DeleteAsync(entity),
            () => service.SetStatusAsync(entity, EntityStatusValue.Disabled),
        ];
        foreach (var write in writes)
        {
            await Assert.ThrowsAsync<ReadOnlyConnectionException>(write);
        }

        Assert.Single(inner.Queues);
    }

    [Fact]
    public async Task Message_peeks_pass_through_and_everything_else_is_refused()
    {
        var inner = new RecordingMessageService();
        var service = new ReadOnlyMessageService(inner);

        await service.PeekAsync(Orders, 10, cancellationToken: TestContext.Current.CancellationToken);
        await service.GetSessionStateAsync(Orders.ForSession("s"), TestContext.Current.CancellationToken);

        Func<Task>[] writes =
        [
            () => service.ReceiveAndDeleteAsync(Orders, 1, TimeSpan.Zero),
            () => service.ReceivePeekLockAsync(Orders, 1, TimeSpan.Zero),
            () => service.SendAsync("orders", [new OutgoingMessage()]),
            () => service.ReceiveDeferredAsync(Orders, [1]),
            () => service.CancelScheduledAsync("orders", [1]),
            () => service.PurgeAsync(Orders),
            () => service.PurgeAllSessionsAsync(Orders),
            () => service.SetSessionStateAsync(Orders.ForSession("s"), null),
            () => service.DeleteAsync(Orders, [1]),
            () => service.ResubmitAsync(Orders, [1], "orders"),
        ];
        foreach (var write in writes)
        {
            var error = await Assert.ThrowsAsync<ReadOnlyConnectionException>(write);
            Assert.Contains("read-only", error.Message);
        }

        Assert.Equal(["Peek", "GetSessionState"], inner.Calls);
    }

    [Fact]
    public void Read_only_is_part_of_a_saved_profile()
    {
        var profile = new ConnectionProfile { Name = "prod", ReadOnly = true };

        Assert.NotEqual(profile, profile with { ReadOnly = false });
    }

    private sealed class RecordingMessageService : IMessageService
    {
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<MessageInfo>> PeekAsync(MessageSource source, int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("Peek");
            return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
        }

        public Task<ReadOnlyMemory<byte>?> GetSessionStateAsync(MessageSource source, CancellationToken cancellationToken = default)
        {
            Calls.Add("GetSessionState");
            return Task.FromResult<ReadOnlyMemory<byte>?>(null);
        }

        public Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<MessageInfo>>();
        public Task<IReceivedBatch> ReceivePeekLockAsync(MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default) => Fail<IReceivedBatch>();
        public Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default) => Fail<bool>();
        public Task<IReceivedBatch> ReceiveDeferredAsync(MessageSource source, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default) => Fail<IReceivedBatch>();
        public Task CancelScheduledAsync(string entityName, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default) => Fail<bool>();
        public Task<long> PurgeAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default, DateTimeOffset? enqueuedBefore = null) => Fail<long>();
        public Task<long> PurgeAllSessionsAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => Fail<long>();
        public Task SetSessionStateAsync(MessageSource source, ReadOnlyMemory<byte>? state, CancellationToken cancellationToken = default) => Fail<bool>();
        public Task<IReadOnlyList<long>> DeleteAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<long>>();
        public Task<IReadOnlyList<long>> ResubmitAsync(MessageSource source, IReadOnlyCollection<long> sequenceNumbers, string targetEntity, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<long>>();

        private static Task<T> Fail<T>() => throw new InvalidOperationException("A write reached the real service.");
    }
}
