using Avalonia.Controls;

namespace SbExplorer.App.Views;

public partial class EntityTreeView : UserControl
{
    public EntityTreeView() => InitializeComponent();

    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
}
