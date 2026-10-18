using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SbExplorer.App.Views;

/// <summary>Shows the code for Entra device-code sign-in; the SDK would otherwise print it to a console nobody sees.</summary>
public partial class DeviceCodeWindow : Window
{
    public DeviceCodeWindow() => InitializeComponent();

    public DeviceCodeWindow(string message, string code, Uri verificationUri) : this()
    {
        MessageText.Text = message;
        CodeText.Text = code;
        VerificationUri = verificationUri;
    }

    public Uri? VerificationUri { get; }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(CodeText.Text ?? string.Empty);
        }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (VerificationUri is { } uri)
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
