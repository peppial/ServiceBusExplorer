using System.Collections.ObjectModel;
using System.Globalization;

namespace SbExplorer.App.Services;

public sealed record LogEntry(DateTimeOffset Time, string Message, bool IsError)
{
    public string Text => $"<{Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}> {Message}";
}

/// <summary>The Log panel: one timestamped line per operation, like the original's log window.</summary>
public interface ILogService
{
    ObservableCollection<LogEntry> Entries { get; }
    void Info(string message);
    void Error(string message);
    void Clear();
}

public sealed class LogService(TimeProvider? clock = null) : ILogService
{
    private const int MaxEntries = 5000;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Info(string message) => Add(message, isError: false);

    public void Error(string message) => Add(message, isError: true);

    public void Clear() => Entries.Clear();

    private void Add(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Entries.Add(new LogEntry(_clock.GetUtcNow(), message.Trim(), isError));
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
