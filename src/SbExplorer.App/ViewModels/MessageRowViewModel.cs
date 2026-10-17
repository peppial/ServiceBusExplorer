using System.Globalization;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

/// <summary>One row in the message list.</summary>
public sealed class MessageRowViewModel(MessageInfo message)
{
    public MessageInfo Message { get; } = message;

    public long SequenceNumber => Message.SequenceNumber;
    public string MessageId => Message.MessageId ?? string.Empty;
    public string Subject => Message.Subject ?? string.Empty;
    public string ContentType => Message.ContentType ?? string.Empty;
    public string SessionId => Message.SessionId ?? string.Empty;
    public string Enqueued => LocalTime(Message.EnqueuedTime);
    public int DeliveryCount => Message.DeliveryCount;
    public string Size => DisplayFormat.Size(Message.Body.Length);
    public string State => Message.LockToken is not null ? "Locked" : Message.State.ToString();
    public string DeadLetterReason => Message.DeadLetterReason ?? string.Empty;

    public static string LocalTime(DateTimeOffset value) =>
        value.Year < 1971 ? string.Empty : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
