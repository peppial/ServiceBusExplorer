using Azure.Messaging.ServiceBus;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Infrastructure.Messaging;

public sealed class AzureMessageService(ServiceBusClient client) : IMessageService
{
    private const int MaxReceiveBatch = 100;
    private const int PurgeBatch = 500;

    /// <summary>How many messages a delete or resubmit looks through before giving up on the rest.</summary>
    private const int MaxScan = 5000;

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

    public async Task<IReceivedBatch> ReceiveDeferredAsync(
        MessageSource source, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        var receiver = CreateReceiver(source, ServiceBusReceiveMode.PeekLock);
        try
        {
            var received = await receiver.ReceiveDeferredMessagesAsync(sequenceNumbers, cancellationToken);
            return new ReceivedBatch(receiver, received);
        }
        catch
        {
            await receiver.DisposeAsync();
            throw;
        }
    }

    public async Task CancelScheduledAsync(string entityName, IReadOnlyList<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        await using var sender = client.CreateSender(entityName);
        await sender.CancelScheduledMessagesAsync(sequenceNumbers, cancellationToken);
    }

    public async Task<long> PurgeAsync(MessageSource source, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(source, ServiceBusReceiveMode.ReceiveAndDelete, prefetch: PurgeBatch);
        long purged = 0;
        var wait = TimeSpan.FromSeconds(5);
        while (true)
        {
            // Bodies aren't needed, so nothing is mapped; only the count matters.
            var batch = await receiver.ReceiveMessagesAsync(PurgeBatch, wait, cancellationToken);
            if (batch.Count == 0)
            {
                return purged;
            }

            purged += batch.Count;
            progress?.Report(purged);
            wait = TimeSpan.FromSeconds(2);
        }
    }

    public async Task<IReadOnlyList<long>> DeleteAsync(
        MessageSource source, IReadOnlyCollection<long> sequenceNumbers, CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(source, ServiceBusReceiveMode.PeekLock);
        return await ProcessMatchingAsync(receiver, sequenceNumbers, _ => Task.CompletedTask, cancellationToken);
    }

    public async Task<IReadOnlyList<long>> ResubmitAsync(
        MessageSource source,
        IReadOnlyCollection<long> sequenceNumbers,
        string targetEntity,
        CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(source, ServiceBusReceiveMode.PeekLock);
        await using var sender = client.CreateSender(targetEntity);
        return await ProcessMatchingAsync(
            receiver,
            sequenceNumbers,
            message =>
            {
                var copy = OutgoingMessage.CopyOf(MessageMapper.ToInfo(message, isLocked: false));
                return sender.SendMessageAsync(MessageMapper.ToServiceBusMessage(copy), cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Locks messages batch by batch, runs <paramref name="handle"/> on each requested one and completes it,
    /// keeping every other message locked so it isn't received twice, then abandons those at the end.
    /// </summary>
    private static async Task<IReadOnlyList<long>> ProcessMatchingAsync(
        ServiceBusReceiver receiver,
        IReadOnlyCollection<long> sequenceNumbers,
        Func<ServiceBusReceivedMessage, Task> handle,
        CancellationToken cancellationToken)
    {
        var wanted = sequenceNumbers.ToHashSet();
        var done = new List<long>(wanted.Count);
        var skipped = new List<ServiceBusReceivedMessage>();
        try
        {
            var scanned = 0;
            while (wanted.Count > 0 && scanned < MaxScan)
            {
                var batch = await receiver.ReceiveMessagesAsync(MaxReceiveBatch, TimeSpan.FromSeconds(2), cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                scanned += batch.Count;
                foreach (var message in batch)
                {
                    if (!wanted.Remove(message.SequenceNumber))
                    {
                        skipped.Add(message);
                        continue;
                    }

                    try
                    {
                        await handle(message);
                    }
                    catch
                    {
                        await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
                        throw;
                    }

                    await receiver.CompleteMessageAsync(message, cancellationToken);
                    done.Add(message.SequenceNumber);
                }
            }
        }
        finally
        {
            foreach (var message in skipped)
            {
                try
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
                }
                catch (ServiceBusException)
                {
                    // Lock already expired; the message is available again either way.
                }
            }
        }

        return done;
    }

    private ServiceBusReceiver CreateReceiver(MessageSource source, ServiceBusReceiveMode mode, int prefetch = 0)
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = mode,
            PrefetchCount = prefetch,
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
