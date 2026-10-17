using Avalonia.Controls;

namespace SbExplorer.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();

    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
}
