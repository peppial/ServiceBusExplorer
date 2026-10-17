using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SbExplorer.App.Views;

public partial class ReceiveOptionsWindow : Window
{
    public ReceiveOptionsWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
