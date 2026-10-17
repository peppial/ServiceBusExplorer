using Avalonia.Controls;
using Avalonia.Interactivity;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class ConnectionEditorWindow : Window
{
    public ConnectionEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("NameBox")?.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ConnectionEditorViewModel editor)
        {
            editor.Saved += (_, _) => Close(true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Don't leave a sign-in or network call running behind a closed dialog.
        if (DataContext is ConnectionEditorViewModel { TestCommand.IsRunning: true } editor)
        {
            editor.TestCommand.Cancel();
        }

        base.OnClosed(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
