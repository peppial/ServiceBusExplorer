using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

public sealed record PropertyRow(string Name, string Value, string Type = "");

/// <summary>Body, system properties and application properties of the selected message.</summary>
public partial class MessageDetailViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly FormattedBody _formatted;

    public MessageDetailViewModel(MessageInfo message, IDialogService dialogs)
    {
        Message = message;
        _dialogs = dialogs;
        _formatted = MessageBodyFormatter.Format(message.Body.Span, message.ContentType);

        SystemProperties = BuildSystemProperties(message);
        ApplicationProperties = message.ApplicationProperties
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PropertyRow(p.Key, PropertyValues.Format(p.Value), p.Value?.GetType().Name ?? "null"))
            .ToList();
    }

    public MessageInfo Message { get; }
    public IReadOnlyList<PropertyRow> SystemProperties { get; }
    public IReadOnlyList<PropertyRow> ApplicationProperties { get; }
    public bool HasApplicationProperties => ApplicationProperties.Count > 0;

    /// <summary>Text bodies can be flipped to a hex view; binary bodies are always hex.</summary>
    public bool CanToggleHex => !_formatted.IsBinary && _formatted.Format != BodyFormat.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    private bool _showHex;

    public string BodyText => _formatted.Format == BodyFormat.Empty
        ? "(empty body)"
        : ShowHex && CanToggleHex ? MessageBodyFormatter.HexDump(Message.Body.Span) : _formatted.Text;

    public string BodySummary
    {
        get
        {
            var format = _formatted.Format switch
            {
                BodyFormat.Json => "JSON",
                BodyFormat.Xml => "XML",
                BodyFormat.Text => "Text",
                BodyFormat.Binary => "Binary",
                _ => "Empty",
            };
            var bodyType = Message.BodyType == "Data" ? string.Empty : $" · AMQP {Message.BodyType} body";
            return $"{format} · {DisplayFormat.Size(Message.Body.Length)}{bodyType}";
        }
    }

    [RelayCommand]
    private Task CopyBodyAsync() => _dialogs.CopyTextAsync(BodyText);

    [RelayCommand]
    private Task SaveBodyAsync()
    {
        var extension = _formatted.Format switch
        {
            BodyFormat.Json => "json",
            BodyFormat.Xml => "xml",
            BodyFormat.Text => "txt",
            _ => "bin",
        };
        var name = string.IsNullOrWhiteSpace(Message.MessageId) ? $"message-{Message.SequenceNumber}" : Message.MessageId;
        return _dialogs.SaveFileAsync($"{SafeFileName(name)}.{extension}", Message.Body);
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '_' : c));

    private static IReadOnlyList<PropertyRow> BuildSystemProperties(MessageInfo m)
    {
        var rows = new List<PropertyRow>
        {
            new("Sequence number", m.SequenceNumber.ToString()),
            new("Message ID", m.MessageId ?? string.Empty),
            new("State", m.State.ToString()),
            new("Enqueued", MessageRowViewModel.LocalTime(m.EnqueuedTime)),
            new("Expires", MessageRowViewModel.LocalTime(m.ExpiresAt)),
            new("Time to live", DisplayFormat.Duration(m.TimeToLive)),
            new("Delivery count", m.DeliveryCount.ToString()),
        };

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                rows.Add(new(name, value));
            }
        }

        Add("Subject", m.Subject);
        Add("Content type", m.ContentType);
        Add("Correlation ID", m.CorrelationId);
        Add("Session ID", m.SessionId);
        Add("Partition key", m.PartitionKey);
        Add("To", m.To);
        Add("Reply to", m.ReplyTo);
        Add("Reply to session ID", m.ReplyToSessionId);
        if (m.State == MessageState.Scheduled)
        {
            Add("Scheduled for", MessageRowViewModel.LocalTime(m.ScheduledEnqueueTime));
        }

        if (m.LockedUntil is { } lockedUntil)
        {
            Add("Locked until", MessageRowViewModel.LocalTime(lockedUntil));
        }

        Add("Dead-letter reason", m.DeadLetterReason);
        Add("Dead-letter description", m.DeadLetterErrorDescription);
        Add("Dead-letter source", m.DeadLetterSource);
        return rows;
    }
}
