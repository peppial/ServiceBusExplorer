using Avalonia.Controls;
using Avalonia.Interactivity;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class SendMessageWindow : Window
{
    public SendMessageWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SendMessageViewModel sender)
        {
            sender.Sent += (_, _) => Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
