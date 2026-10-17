using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class LogView : UserControl
{
    private INotifyCollectionChanged? _entries;

    public LogView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_entries is not null)
        {
            _entries.CollectionChanged -= OnEntriesChanged;
        }

        _entries = (DataContext as MainWindowViewModel)?.Log.Entries;
        if (_entries is not null)
        {
            _entries.CollectionChanged += OnEntriesChanged;
        }
    }

    // Keep the newest line in view, like the original log window.
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?[^1] is { } last)
        {
            LogList.ScrollIntoView(last);
        }
    }

    private async void OnCopyAll(object? sender, RoutedEventArgs e) => await CopyAsync(LogList.Items.OfType<LogEntry>());

    private async void OnCopySelected(object? sender, RoutedEventArgs e) =>
        await CopyAsync(LogList.SelectedItems?.OfType<LogEntry>().OrderBy(l => l.Time) ?? Enumerable.Empty<LogEntry>());

    private void OnClearSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && LogList.SelectedItems is { } selected)
        {
            foreach (var entry in selected.OfType<LogEntry>().ToList())
            {
                vm.Log.Entries.Remove(entry);
            }
        }
    }

    private async Task CopyAsync(IEnumerable<LogEntry> entries)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(MainWindowViewModel.LogText(entries));
        }
    }
}
