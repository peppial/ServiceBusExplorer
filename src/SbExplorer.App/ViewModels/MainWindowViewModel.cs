using CommunityToolkit.Mvvm.ComponentModel;

namespace SbExplorer.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "Not connected";
}
