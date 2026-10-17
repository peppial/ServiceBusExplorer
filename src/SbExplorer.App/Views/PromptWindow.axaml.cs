using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SbExplorer.App.Views;

public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Label
    {
        get => LabelText.Text ?? string.Empty;
        set => LabelText.Text = value;
    }

    public string Value
    {
        get => ValueBox.Text ?? string.Empty;
        set => ValueBox.Text = value;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Value);
}
