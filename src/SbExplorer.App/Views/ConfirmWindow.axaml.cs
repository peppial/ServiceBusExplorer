using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SbExplorer.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    public string Message
    {
        get => MessageText.Text ?? string.Empty;
        set => MessageText.Text = value;
    }

    public string ConfirmText
    {
        get => ConfirmButton.Content as string ?? string.Empty;
        set => ConfirmButton.Content = value;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
