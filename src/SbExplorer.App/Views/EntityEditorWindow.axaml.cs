using Avalonia.Controls;
using Avalonia.Interactivity;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class EntityEditorWindow : Window
{
    public EntityEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EntityEditorViewModel editor)
        {
            editor.Saved += (_, _) => Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
