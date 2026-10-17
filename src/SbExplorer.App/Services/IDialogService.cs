using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Services;

public interface IDialogService
{
    /// <summary>"Connect to a Service Bus Namespace"; true when the user chose OK.</summary>
    Task<bool> ShowConnectDialogAsync(ConnectDialogViewModel dialog);

    /// <summary>Shows the send message dialog; true when messages were sent.</summary>
    Task<bool> ShowSendMessageAsync(SendMessageViewModel sender);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialValue);

    /// <summary>Lets the user pick a file and writes <paramref name="content"/> to it; false when cancelled.</summary>
    Task<bool> SaveFileAsync(string suggestedName, ReadOnlyMemory<byte> content);

    Task CopyTextAsync(string text);

    /// <summary>"Retrieve messages": peek or receive, how many and from where; true on OK.</summary>
    Task<bool> ShowReceiveOptionsAsync(ReceiveOptionsViewModel options);

    /// <summary>Lets the user pick a file and returns its text; null when cancelled.</summary>
    Task<string?> OpenTextFileAsync(string title);

    /// <summary>Shows a message with one button per option; returns the chosen index, or null when cancelled.</summary>
    Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> options);
}
