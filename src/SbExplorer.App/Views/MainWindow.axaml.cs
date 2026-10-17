using Avalonia.Controls;
using Avalonia.Input;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel { Explorer: { } explorer })
        {
            return;
        }

        // Cmd on macOS, Ctrl elsewhere.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.F5 || (e.Key == Key.R && e.KeyModifiers == command))
        {
            if (explorer.RefreshCommand.CanExecute(null))
            {
                explorer.RefreshCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == command)
        {
            Explorer.FocusFilter();
            e.Handled = true;
        }
    }

    private void OnConnectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.ConnectCommand.CanExecute(null))
        {
            vm.ConnectCommand.Execute(null);
        }
    }
}
