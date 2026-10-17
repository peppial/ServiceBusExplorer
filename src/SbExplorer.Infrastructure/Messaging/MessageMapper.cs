using System.Text;
using System.Text.Json;
using Azure.Core.Amqp;
using Azure.Messaging.ServiceBus;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Infrastructure.Messaging;

public static class MessageMapper
{
    public static MessageInfo ToInfo(ServiceBusReceivedMessage message, bool isLocked)
    {
        var (bodyType, body) = ReadBody(message.GetRawAmqpMessage());
        return new MessageInfo
        {
            SequenceNumber = message.SequenceNumber,
            MessageId = message.MessageId,
            Subject = message.Subject,
            ContentType = message.ContentType,
            CorrelationId = message.CorrelationId,
            SessionId = message.SessionId,
            PartitionKey = message.PartitionKey,
            To = message.To,
            ReplyTo = message.ReplyTo,
            ReplyToSessionId = message.ReplyToSessionId,
            TimeToLive = message.TimeToLive,
            EnqueuedTime = message.EnqueuedTime,
            ScheduledEnqueueTime = message.ScheduledEnqueueTime,
            ExpiresAt = message.ExpiresAt,
            LockedUntil = isLocked ? message.LockedUntil : null,
            DeliveryCount = message.DeliveryCount,
            State = message.State switch
            {
                ServiceBusMessageState.Deferred => MessageState.Deferred,
                ServiceBusMessageState.Scheduled => MessageState.Scheduled,
                _ => MessageState.Active,
            },
            DeadLetterReason = message.DeadLetterReason,
            DeadLetterErrorDescription = message.DeadLetterErrorDescription,
            DeadLetterSource = message.DeadLetterSource,
            BodyType = bodyType,
            Body = body,
            ApplicationProperties = message.ApplicationProperties.ToDictionary(p => p.Key, p => (object?)p.Value),
            LockToken = isLocked ? message.LockToken : null,
        };
    }

    public static ServiceBusMessage ToServiceBusMessage(OutgoingMessage message)
    {
        var result = new ServiceBusMessage(new BinaryData(message.Body))
        {
            ContentType = message.ContentType,
            Subject = message.Subject,
            CorrelationId = message.CorrelationId,
            SessionId = message.SessionId,
            To = message.To,
            ReplyTo = message.ReplyTo,
            ReplyToSessionId = message.ReplyToSessionId,
        };

        // Leave MessageId to the SDK when unset so every message gets a unique one.
        if (!string.IsNullOrEmpty(message.MessageId))
        {
            result.MessageId = message.MessageId;
        }

        // PartitionKey must match SessionId when both are set; the SDK enforces it.
        if (!string.IsNullOrEmpty(message.PartitionKey))
        {
            result.PartitionKey = message.PartitionKey;
        }

        if (message.TimeToLive is { } ttl)
        {
            result.TimeToLive = ttl;
        }

        if (message.ScheduledEnqueueTime is { } scheduled)
        {
            result.ScheduledEnqueueTime = scheduled;
        }

        foreach (var (key, value) in message.ApplicationProperties)
        {
            result.ApplicationProperties[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Most messages carry a Data body. Java, Python and older AMQP clients may send a Value or Sequence body,
    /// which <see cref="ServiceBusReceivedMessage.Body"/> refuses to read, so those are rendered as text.
    /// </summary>
    private static (string BodyType, ReadOnlyMemory<byte> Body) ReadBody(AmqpAnnotatedMessage amqp)
    {
        if (amqp.Body.TryGetData(out var data) && data is not null)
        {
            var segments = data.ToList();
            if (segments.Count == 1)
            {
                return ("Data", segments[0]);
            }

            var buffer = new byte[segments.Sum(s => s.Length)];
            var offset = 0;
            foreach (var segment in segments)
            {
                segment.CopyTo(buffer.AsMemory(offset));
                offset += segment.Length;
            }

            return ("Data", buffer);
        }

        if (amqp.Body.TryGetValue(out var value))
        {
            return ("Value", value switch
            {
                null => ReadOnlyMemory<byte>.Empty,
                byte[] bytes => bytes,
                string text => Encoding.UTF8.GetBytes(text),
                _ => JsonSerializer.SerializeToUtf8Bytes(value),
            });
        }

        if (amqp.Body.TryGetSequence(out var sequence))
        {
            return ("Sequence", JsonSerializer.SerializeToUtf8Bytes(sequence));
        }

        return ("Unknown", ReadOnlyMemory<byte>.Empty);
    }
}
