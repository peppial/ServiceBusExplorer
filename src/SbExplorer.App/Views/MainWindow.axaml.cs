using Avalonia.Controls;
using Avalonia.Input;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnConnectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.ConnectCommand.CanExecute(null))
        {
            vm.ConnectCommand.Execute(null);
        }
    }
}
