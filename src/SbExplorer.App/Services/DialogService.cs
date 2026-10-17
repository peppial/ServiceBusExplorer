using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
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

    public async Task<bool> ShowSendMessageAsync(SendMessageViewModel sender)
    {
        var window = new SendMessageWindow { DataContext = sender };
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var window = new ConfirmWindow { Title = title, Message = message, ConfirmText = confirmText };
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<string?> PromptAsync(string title, string label, string initialValue)
    {
        var window = new PromptWindow { Title = title, Label = label, Value = initialValue };
        return await window.ShowDialog<string?>(Owner);
    }

    public async Task<bool> SaveFileAsync(string suggestedName, ReadOnlyMemory<byte> content)
    {
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = true,
        });
        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(content);
        return true;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Owner.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static Window Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? throw new InvalidOperationException("Dialogs need a main window.");
}
