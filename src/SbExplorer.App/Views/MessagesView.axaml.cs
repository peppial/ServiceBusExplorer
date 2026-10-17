using System.ComponentModel;
using Avalonia.Controls;
using SbExplorer.App.ViewModels;

namespace SbExplorer.App.Views;

public partial class MessagesView : UserControl
{
    private MessagesViewModel? _viewModel;

    public MessagesView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MessagesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateColumns();
    }

    // SelectedItems isn't bindable, so multi-selection is pushed to the view model here.
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is not null && MessageTable.SelectedItems is { } selected)
        {
            _viewModel.SelectedRows = selected.OfType<MessageRowViewModel>().ToList();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessagesViewModel.IsDeadLetterView))
        {
            UpdateColumns();
        }
    }

    private void OnSelectAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MessageTable.SelectAll();

    // Columns aren't in the visual tree, so their visibility can't be bound to the view model.
    private void UpdateColumns() => DeadLetterReasonColumn.IsVisible = _viewModel?.IsDeadLetterView == true;
}
