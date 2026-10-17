using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Services;

public interface IDialogService
{
    /// <summary>Shows the add/edit connection dialog; true when the user saved.</summary>
    Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}
