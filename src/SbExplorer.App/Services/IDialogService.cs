using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Services;

public interface IDialogService
{
    /// <summary>Shows the add/edit connection dialog; true when the user saved.</summary>
    Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor);

    /// <summary>Shows the send message dialog; true when messages were sent.</summary>
    Task<bool> ShowSendMessageAsync(SendMessageViewModel sender);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialValue);

    /// <summary>Lets the user pick a file and writes <paramref name="content"/> to it; false when cancelled.</summary>
    Task<bool> SaveFileAsync(string suggestedName, ReadOnlyMemory<byte> content);

    Task CopyTextAsync(string text);
}
