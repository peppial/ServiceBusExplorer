using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

public partial class ApplicationPropertyRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private PropertyValueType _type = PropertyValueType.String;
}

/// <summary>The send dialog: a new message, or a clone of an existing one.</summary>
public partial class SendMessageViewModel : ObservableObject
{
    public const int MaxCount = 1000;

    private readonly IMessageService _messages;
    private readonly TimeProvider _clock;

    /// <summary>Set when cloning a non-text message; resent byte-for-byte because it can't be edited as text.</summary>
    private readonly ReadOnlyMemory<byte>? _binaryBody;

    public SendMessageViewModel(
        IMessageService messages, string target, bool requiresSession, MessageInfo? template = null, TimeProvider? clock = null)
    {
        _messages = messages;
        _clock = clock ?? TimeProvider.System;
        Target = target;
        RequiresSession = requiresSession;
        ApplicationProperties.CollectionChanged += OnApplicationPropertiesChanged;

        if (template is null)
        {
            _contentType = "application/json";
            return;
        }

        var formatted = MessageBodyFormatter.Format(template.Body.Span, template.ContentType);
        if (formatted.IsBinary)
        {
            _binaryBody = template.Body;
            _body = formatted.Text;
        }
        else
        {
            _body = formatted.Text;
        }

        _contentType = template.ContentType ?? string.Empty;
        _subject = template.Subject ?? string.Empty;
        _correlationId = template.CorrelationId ?? string.Empty;
        _sessionId = template.SessionId ?? string.Empty;
        _to = template.To ?? string.Empty;
        _replyTo = template.ReplyTo ?? string.Empty;

        // A fresh MessageId by default, so duplicate detection doesn't silently drop the resend.
        foreach (var (key, value) in template.ApplicationProperties)
        {
            ApplicationProperties.Add(new ApplicationPropertyRowViewModel
            {
                Name = key,
                Value = PropertyValues.Format(value),
                Type = PropertyValues.TypeOf(value),
            });
        }
    }

    public string Target { get; }
    public bool RequiresSession { get; }
    public string Title => Heading ?? $"Send to {Target}";
    public bool IsBinaryBody => _binaryBody is not null;

    public IReadOnlyList<string> ContentTypes { get; } =
        ["application/json", "text/plain", "application/xml", "application/octet-stream"];

    public IReadOnlyList<PropertyValueType> PropertyTypes { get; } = Enum.GetValues<PropertyValueType>();

    public ObservableCollection<ApplicationPropertyRowViewModel> ApplicationProperties { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyWarning))]
    private string _body = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyWarning))]
    private string _contentType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _messageId = string.Empty;

    [ObservableProperty]
    private string _subject = string.Empty;

    [ObservableProperty]
    private string _correlationId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _partitionKey = string.Empty;

    [ObservableProperty]
    private string _to = string.Empty;

    [ObservableProperty]
    private string _replyTo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _timeToLive = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private int _count = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _schedule;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _scheduleAt = "+10m";

    [ObservableProperty]
    private string? _sendError;

    /// <summary>Overrides the dialog title, e.g. when editing a dead-lettered message before resubmitting it.</summary>
    public string? Heading { get; init; }

    /// <summary>Number of messages sent by the last successful send.</summary>
    public int SentCount { get; private set; }

    public event EventHandler? Sent;

    /// <summary>A non-blocking hint, e.g. the body isn't valid JSON for a JSON content type.</summary>
    public string? BodyWarning
    {
        get
        {
            if (IsBinaryBody || string.IsNullOrWhiteSpace(Body))
            {
                return null;
            }

            if (ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) && !MessageBodyFormatter.TryPrettyJson(Body, out _))
            {
                return "The body isn't valid JSON.";
            }

            if (ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase) && !MessageBodyFormatter.TryPrettyXml(Body, out _))
            {
                return "The body isn't valid XML.";
            }

            return null;
        }
    }

    public string? ValidationError
    {
        get
        {
            if (RequiresSession && string.IsNullOrWhiteSpace(SessionId))
            {
                return $"{Target} requires sessions; enter a session ID.";
            }

            if (Count is < 1 or > MaxCount)
            {
                return $"Count must be between 1 and {MaxCount}.";
            }

            if (Schedule && !ScheduleTime.TryParse(ScheduleAt, _clock.GetLocalNow(), out _, out var scheduleError))
            {
                return scheduleError;
            }

            if (!string.IsNullOrWhiteSpace(TimeToLive) && !TryParseTimeToLive(TimeToLive, out _))
            {
                return "Time to live must look like 00:10:00 (hh:mm:ss) or 1.00:00:00 (d.hh:mm:ss).";
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in ApplicationProperties)
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    return "Every application property needs a name.";
                }

                if (!names.Add(property.Name.Trim()))
                {
                    return $"Application property '{property.Name}' appears twice.";
                }

                if (!PropertyValues.TryParse(property.Value, property.Type, out _, out var error))
                {
                    return $"{property.Name}: {error}";
                }
            }

            return null;
        }
    }

    public bool IsValid => ValidationError is null;

    public IReadOnlyList<OutgoingMessage> BuildMessages()
    {
        var body = _binaryBody ?? Encoding.UTF8.GetBytes(Body);
        var properties = ApplicationProperties.ToDictionary(
            p => p.Name.Trim(),
            p => PropertyValues.TryParse(p.Value, p.Type, out var value, out _) ? value : null);
        TimeSpan? ttl = TryParseTimeToLive(TimeToLive, out var parsed) ? parsed : null;
        DateTimeOffset? scheduled = Schedule && ScheduleTime.TryParse(ScheduleAt, _clock.GetLocalNow(), out var at, out _) ? at : null;

        return Enumerable.Range(1, Count).Select(i => new OutgoingMessage
        {
            Body = body,
            ContentType = Blank(ContentType),
            MessageId = string.IsNullOrWhiteSpace(MessageId) ? null : Count == 1 ? MessageId.Trim() : $"{MessageId.Trim()}-{i}",
            Subject = Blank(Subject),
            CorrelationId = Blank(CorrelationId),
            SessionId = Blank(SessionId),
            PartitionKey = Blank(PartitionKey),
            To = Blank(To),
            ReplyTo = Blank(ReplyTo),
            TimeToLive = ttl,
            ScheduledEnqueueTime = scheduled,
            ApplicationProperties = properties,
        }).ToList();
    }

    [RelayCommand]
    private void AddProperty() => ApplicationProperties.Add(new ApplicationPropertyRowViewModel());

    [RelayCommand]
    private void RemoveProperty(ApplicationPropertyRowViewModel property) => ApplicationProperties.Remove(property);

    [RelayCommand]
    private void FormatBody()
    {
        if (MessageBodyFormatter.TryPrettyJson(Body, out var json))
        {
            Body = json;
        }
        else if (MessageBodyFormatter.TryPrettyXml(Body, out var xml))
        {
            Body = xml;
        }
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        SendError = null;
        try
        {
            var messages = BuildMessages();
            await _messages.SendAsync(Target, messages, cancellationToken);
            SentCount = messages.Count;
            Sent?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SendError = ErrorMessages.Describe(ex);
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseTimeToLive(string text, out TimeSpan value) =>
        TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out value) && value > TimeSpan.Zero;

    // Property rows are edited in place, so validation has to watch each row as well as the collection.
    private void OnApplicationPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.NewItems?.OfType<ApplicationPropertyRowViewModel>() ?? [])
        {
            row.PropertyChanged += OnApplicationPropertyChanged;
        }

        foreach (var row in e.OldItems?.OfType<ApplicationPropertyRowViewModel>() ?? [])
        {
            row.PropertyChanged -= OnApplicationPropertyChanged;
        }

        OnApplicationPropertyChanged(sender, new PropertyChangedEventArgs(null));
    }

    private void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(IsValid));
        SendCommand.NotifyCanExecuteChanged();
    }
}
