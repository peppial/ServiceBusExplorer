using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class MainWindow : Window
{
    private const double DefaultTreeWidth = 380;
    private const double DefaultLogHeight = 190;
    private MainWindowViewModel? _viewModel;

    public MainWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.SavedConnections.CollectionChanged -= OnSavedConnectionsChanged;
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.SavedConnections.CollectionChanged += OnSavedConnectionsChanged;
            _viewModel.PropertyChanged += OnViewModelChanged;
        }

        BuildSavedConnectionsMenu();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null)
        {
            return;
        }

        // Cmd on macOS, Ctrl elsewhere.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.N && e.KeyModifiers == command)
        {
            _viewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
        else if (_viewModel.Explorer is { } explorer && (e.Key == Key.F5 || (e.Key == Key.R && e.KeyModifiers == command)))
        {
            if (explorer.RefreshCommand.CanExecute(null))
            {
                explorer.RefreshCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (_viewModel.Explorer is not null && e.Key == Key.F && e.KeyModifiers == command)
        {
            TreePanel.FocusFilter();
            e.Handled = true;
        }
    }

    // A submenu bound to a collection would also restyle its own header, so the items are built here.
    private void BuildSavedConnectionsMenu()
    {
        SavedConnectionsMenu.Items.Clear();
        foreach (var profile in _viewModel?.SavedConnections ?? [])
        {
            SavedConnectionsMenu.Items.Add(new MenuItem
            {
                Header = profile.FullyQualifiedNamespace is { } host && host != profile.Name ? $"{profile.Name}  ({host})" : profile.Name,
                Command = _viewModel!.ConnectSavedCommand,
                CommandParameter = profile,
            });
        }
    }

    private void OnSavedConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => BuildSavedConnectionsMenu();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsLogVisible))
        {
            Layout.RowDefinitions[2].Height = _viewModel!.IsLogVisible ? new GridLength(DefaultLogHeight) : new GridLength(0);
        }
    }

    private void OnSetDefaultLayout(object? sender, RoutedEventArgs e)
    {
        ((Grid)Layout.Children[0]).ColumnDefinitions[0].Width = new GridLength(DefaultTreeWidth);
        Layout.RowDefinitions[2].Height = new GridLength(DefaultLogHeight);
        if (_viewModel is { IsLogVisible: false })
        {
            _viewModel.IsLogVisible = true;
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
