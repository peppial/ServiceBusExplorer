using Azure.Messaging.ServiceBus;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Infrastructure.Messaging;

public sealed class AzureMessageService(ServiceBusClient client) : IMessageService
{
    private const int MaxReceiveBatch = 100;

    public async Task<IReadOnlyList<MessageInfo>> PeekAsync(
        MessageSource source, int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(source, ServiceBusReceiveMode.PeekLock);
        var messages = new List<MessageInfo>(maxMessages);
        var from = fromSequenceNumber;

        // A single peek can return fewer messages than asked for even when more exist.
        while (messages.Count < maxMessages)
        {
            var page = await receiver.PeekMessagesAsync(maxMessages - messages.Count, from, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            messages.AddRange(page.Select(m => MessageMapper.ToInfo(m, isLocked: false)));
            from = page[^1].SequenceNumber + 1;
        }

        return messages;
    }

    public async Task<IReadOnlyList<MessageInfo>> ReceiveAndDeleteAsync(
        MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(source, ServiceBusReceiveMode.ReceiveAndDelete);
        var messages = new List<MessageInfo>(maxMessages);
        var wait = maxWaitTime;
        while (messages.Count < maxMessages)
        {
            var batch = await receiver.ReceiveMessagesAsync(Math.Min(maxMessages - messages.Count, MaxReceiveBatch), wait, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            messages.AddRange(batch.Select(m => MessageMapper.ToInfo(m, isLocked: false)));

            // Once messages are flowing, don't sit through the full wait to discover the entity is empty.
            wait = TimeSpan.FromSeconds(1);
        }

        return messages;
    }

    public async Task<IReceivedBatch> ReceivePeekLockAsync(
        MessageSource source, int maxMessages, TimeSpan maxWaitTime, CancellationToken cancellationToken = default)
    {
        var receiver = CreateReceiver(source, ServiceBusReceiveMode.PeekLock);
        try
        {
            var received = new List<ServiceBusReceivedMessage>(maxMessages);
            var wait = maxWaitTime;
            while (received.Count < maxMessages)
            {
                var batch = await receiver.ReceiveMessagesAsync(Math.Min(maxMessages - received.Count, MaxReceiveBatch), wait, cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                received.AddRange(batch);
                wait = TimeSpan.FromSeconds(1);
            }

            return new ReceivedBatch(receiver, received);
        }
        catch
        {
            await receiver.DisposeAsync();
            throw;
        }
    }

    public async Task SendAsync(string entityName, IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken = default)
    {
        await using var sender = client.CreateSender(entityName);
        var batch = await sender.CreateMessageBatchAsync(cancellationToken);
        try
        {
            foreach (var message in messages)
            {
                var sbMessage = MessageMapper.ToServiceBusMessage(message);
                if (batch.TryAddMessage(sbMessage))
                {
                    continue;
                }

                if (batch.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"The message is larger than the {batch.MaxSizeInBytes / 1024:N0} KB the entity accepts.");
                }

                await sender.SendMessagesAsync(batch, cancellationToken);
                batch.Dispose();
                batch = await sender.CreateMessageBatchAsync(cancellationToken);
                if (!batch.TryAddMessage(sbMessage))
                {
                    throw new InvalidOperationException(
                        $"The message is larger than the {batch.MaxSizeInBytes / 1024:N0} KB the entity accepts.");
                }
            }

            if (batch.Count > 0)
            {
                await sender.SendMessagesAsync(batch, cancellationToken);
            }
        }
        finally
        {
            batch.Dispose();
        }
    }

    private ServiceBusReceiver CreateReceiver(MessageSource source, ServiceBusReceiveMode mode)
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = mode,
            SubQueue = source.SubQueue switch
            {
                SubQueueKind.DeadLetter => SubQueue.DeadLetter,
                SubQueueKind.TransferDeadLetter => SubQueue.TransferDeadLetter,
                _ => SubQueue.None,
            },
        };

        return source.IsSubscription
            ? client.CreateReceiver(source.Topic!, source.Subscription!, options)
            : client.CreateReceiver(source.EntityPath, options);
    }

    private sealed class ReceivedBatch : IReceivedBatch
    {
        private readonly ServiceBusReceiver _receiver;
        private readonly Dictionary<string, ServiceBusReceivedMessage> _unsettled;

        public ReceivedBatch(ServiceBusReceiver receiver, IReadOnlyList<ServiceBusReceivedMessage> messages)
        {
            _receiver = receiver;
            _unsettled = messages.ToDictionary(m => m.LockToken);
            Messages = messages.Select(m => MessageMapper.ToInfo(m, isLocked: true)).ToList();
        }

        public IReadOnlyList<MessageInfo> Messages { get; }

        public Task CompleteAsync(MessageInfo message, CancellationToken cancellationToken = default) =>
            SettleAsync(message, m => _receiver.CompleteMessageAsync(m, cancellationToken));

        public Task AbandonAsync(MessageInfo message, CancellationToken cancellationToken = default) =>
            SettleAsync(message, m => _receiver.AbandonMessageAsync(m, cancellationToken: cancellationToken));

        public Task DeadLetterAsync(MessageInfo message, string reason, string? description, CancellationToken cancellationToken = default) =>
            SettleAsync(message, m => _receiver.DeadLetterMessageAsync(m, reason, description, cancellationToken));

        public Task DeferAsync(MessageInfo message, CancellationToken cancellationToken = default) =>
            SettleAsync(message, m => _receiver.DeferMessageAsync(m, cancellationToken: cancellationToken));

        public async ValueTask DisposeAsync()
        {
            foreach (var message in _unsettled.Values.ToList())
            {
                try
                {
                    await _receiver.AbandonMessageAsync(message);
                }
                catch (ServiceBusException)
                {
                    // The lock already expired; the message is back on the entity anyway.
                }
            }

            _unsettled.Clear();
            await _receiver.DisposeAsync();
        }

        private async Task SettleAsync(MessageInfo message, Func<ServiceBusReceivedMessage, Task> settle)
        {
            if (message.LockToken is null || !_unsettled.TryGetValue(message.LockToken, out var received))
            {
                throw new InvalidOperationException($"Message {message.SequenceNumber} is not locked by this receive or was already settled.");
            }

            await settle(received);
            _unsettled.Remove(message.LockToken);
        }
    }
}
