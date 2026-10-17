using Avalonia.Controls;
using Avalonia.Interactivity;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class ConnectWindow : Window
{
    public ConnectWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ConnectDialogViewModel dialog)
        {
            dialog.Confirmed += (_, _) => Close(true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Don't leave a sign-in or network call running behind a closed dialog.
        if (DataContext is ConnectDialogViewModel { Editor.TestCommand.IsRunning: true } dialog)
        {
            dialog.Editor.TestCommand.Cancel();
        }

        base.OnClosed(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
