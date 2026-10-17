using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbExplorer.App.Services;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;

namespace SbExplorer.App.ViewModels;

public sealed class SessionRowViewModel(string sessionId, int? messageCount, DateTimeOffset? oldest)
{
    public string SessionId { get; } = sessionId;
    public int? MessageCountValue { get; } = messageCount;
    public string MessageCount => MessageCountValue?.ToString("N0") ?? "—";
    public string Oldest => oldest is { } value ? MessageRowViewModel.LocalTime(value) : string.Empty;
}

/// <summary>
/// The Sessions tab of a session-enabled queue or subscription. The SDK can't list sessions, so they are found
/// by peeking messages and grouping by session ID, or opened directly by ID.
/// </summary>
public partial class SessionsViewModel(
    IMessageService service, IDialogService dialogs, EntityInfo entity, ILogService? log = null, bool readOnly = false)
    : ObservableObject, IAsyncDisposable
{
    private readonly Dictionary<string, MessagesViewModel> _sessionMessages = [];

    public EntityInfo Entity { get; } = entity;
    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];

    private MessageSource EntitySource => Entity.Kind == EntityKind.Subscription
        ? MessageSource.ForSubscription(Entity.Path.Split('/')[0], Entity.Name)
        : MessageSource.ForQueue(Entity.Name);

    [ObservableProperty]
    private int _scanLimit = 1000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSession), nameof(Messages))]
    [NotifyCanExecuteChangedFor(nameof(LoadStateCommand), nameof(SaveStateCommand), nameof(ClearStateCommand))]
    private SessionRowViewModel? _selectedSession;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveStateCommand))]
    private string _stateText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveStateCommand))]
    private bool _stateIsBinary;

    [ObservableProperty]
    private string? _stateSummary;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _hasError;

    public bool HasSelectedSession => SelectedSession is not null;

    /// <summary>The Messages view for the selected session, kept per session so locks survive switching.</summary>
    public MessagesViewModel? Messages
    {
        get
        {
            if (SelectedSession is not { } session)
            {
                return null;
            }

            if (!_sessionMessages.TryGetValue(session.SessionId, out var messages))
            {
                messages = new MessagesViewModel(service, dialogs, Entity, session.SessionId, log, readOnly: readOnly);
                _sessionMessages[session.SessionId] = messages;
            }

            return messages;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FindSessionsAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var messages = await service.PeekAsync(EntitySource, ScanLimit, cancellationToken: cancellationToken);
            var selected = SelectedSession?.SessionId;
            var found = messages
                .Where(m => !string.IsNullOrEmpty(m.SessionId))
                .GroupBy(m => m.SessionId!)
                .Select(g => new SessionRowViewModel(g.Key, g.Count(), g.Min(m => m.EnqueuedTime)))
                .OrderBy(r => r.SessionId, StringComparer.Ordinal)
                .ToList();

            // Keep sessions opened by ID even if the scan didn't see them.
            var manual = Sessions.Where(r => r.MessageCountValue is null && found.All(f => f.SessionId != r.SessionId)).ToList();
            Sessions.Clear();
            foreach (var row in found.Concat(manual))
            {
                Sessions.Add(row);
            }

            SelectedSession = Sessions.FirstOrDefault(r => r.SessionId == selected) ?? Sessions.FirstOrDefault();
            var limitNote = messages.Count >= ScanLimit ? $" Stopped at {ScanLimit:N0} messages; raise the limit to look further." : string.Empty;
            var sessions = found.Count == 1 ? "1 session" : $"{found.Count:N0} sessions";
            return $"Found {sessions} in {messages.Count:N0} peeked messages.{limitNote} Sessions with state but no messages can be opened by ID.";
        });
    }

    [RelayCommand]
    private async Task OpenSessionAsync()
    {
        var id = (await dialogs.PromptAsync("Open session", "Session ID:", string.Empty))?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var row = Sessions.FirstOrDefault(r => r.SessionId == id);
        if (row is null)
        {
            row = new SessionRowViewModel(id, null, null);
            Sessions.Add(row);
        }

        SelectedSession = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task LoadStateAsync(CancellationToken cancellationToken)
    {
        var sessionId = SelectedSession!.SessionId;
        await RunAsync(async () =>
        {
            var state = await service.GetSessionStateAsync(EntitySource.ForSession(sessionId), cancellationToken);
            ShowState(state);
            return state is null ? $"Session '{sessionId}' has no state." : $"Loaded the state of session '{sessionId}'.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveState))]
    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        var sessionId = SelectedSession!.SessionId;
        var bytes = Encoding.UTF8.GetBytes(StateText);
        await RunAsync(async () =>
        {
            await service.SetSessionStateAsync(EntitySource.ForSession(sessionId), bytes, cancellationToken);
            ShowState(bytes);
            return $"Saved the state of session '{sessionId}'.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanChangeState))]
    private async Task ClearStateAsync(CancellationToken cancellationToken)
    {
        var sessionId = SelectedSession!.SessionId;
        if (!await dialogs.ConfirmAsync("Clear session state", $"Remove the state of session '{sessionId}'?", "Clear state"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await service.SetSessionStateAsync(EntitySource.ForSession(sessionId), null, cancellationToken);
            ShowState(null);
            return $"Cleared the state of session '{sessionId}'.";
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var messages in _sessionMessages.Values)
        {
            await messages.DisposeAsync();
        }

        _sessionMessages.Clear();
    }

    async partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        ShowState(null, summary: null);
        if (value is null)
        {
            return;
        }

        // Reading session state means accepting (locking) the session, which a live consumer may be waiting for,
        // so it only happens when asked for with Load State. Peeking the session's messages doesn't lock it.
        StateSummary = "Not loaded — Load State locks the session briefly";
        if (Messages is { } messages && messages.Messages.Count == 0 && messages.PeekCommand.CanExecute(null))
        {
            await messages.PeekCommand.ExecuteAsync(null);
        }
    }

    private void ShowState(ReadOnlyMemory<byte>? state, string? summary = "")
    {
        if (state is not { } bytes)
        {
            StateText = string.Empty;
            StateIsBinary = false;
            StateSummary = summary is null ? null : "No state";
            return;
        }

        var formatted = MessageBodyFormatter.Format(bytes.Span, contentType: null);
        StateIsBinary = formatted.IsBinary;
        StateText = formatted.Text;
        var format = formatted.Format is BodyFormat.Json or BodyFormat.Xml ? formatted.Format.ToString().ToUpperInvariant() : formatted.Format.ToString();
        StateSummary = $"{format} · {DisplayFormat.Size(bytes.Length)}" + (StateIsBinary ? " · binary state is read-only here" : string.Empty);
    }

    private bool CanSaveState() => !readOnly && SelectedSession is not null && !StateIsBinary;

    private bool CanChangeState() => !readOnly && SelectedSession is not null;

    private async Task RunAsync(Func<Task<string>> operation)
    {
        Status = "Working…";
        HasError = false;
        try
        {
            Status = await operation();
            log?.Info($"{Entity.Path}: {Status}");
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            Status = ErrorMessages.Describe(ex);
            HasError = true;
            log?.Error($"{Entity.Path}: {Status}");
        }
    }
}
