using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;

namespace SbExplorer.App.Services;

public sealed class DialogService : IDialogService
{
    public async Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor)
    {
        var window = new ConnectionEditorWindow { DataContext = editor };
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var window = new ConfirmWindow { Title = title, Message = message, ConfirmText = confirmText };
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    private static Window Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? throw new InvalidOperationException("Dialogs need a main window.");
}
