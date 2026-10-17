using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SbExplorer.App.Views;

/// <summary>
/// The dotted connector lines of a classic Windows tree, drawn for one row: a branch at the row's own level and a
/// continuation line for every ancestor that still has siblings below it.
/// </summary>
public sealed class TreeLines : Control
{
    public const double Indent = 19;

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<TreeLines, IBrush?>(nameof(Stroke), Brushes.Gray);

    private static readonly DashStyle Dotted = new([1, 1], 0);
    private TreeView? _tree;

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tree = this.FindAncestorOfType<TreeView>();
        if (_tree is not null)
        {
            // Siblings appear, disappear or get filtered out; a repaint after each layout keeps the lines right.
            _tree.LayoutUpdated += OnTreeLayoutUpdated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_tree is not null)
        {
            _tree.LayoutUpdated -= OnTreeLayoutUpdated;
            _tree = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(((this.FindAncestorOfType<TreeViewItem>()?.Level ?? 0) + 1) * Indent, 0);

    public override void Render(DrawingContext context)
    {
        if (this.FindAncestorOfType<TreeViewItem>() is not { } item)
        {
            return;
        }

        var pen = new Pen(Stroke, 1, Dotted);
        var height = Bounds.Height;
        var middle = Math.Floor(height / 2) + 0.5;

        var x = Column(item.Level);
        var top = item.Level == 0 && IsFirst(item) ? middle : 0;
        context.DrawLine(pen, new Point(x, top), new Point(x, IsLast(item) ? middle : height));
        context.DrawLine(pen, new Point(x, middle), new Point(x + Indent / 2, middle));

        for (var ancestor = OwnerItem(item); ancestor is not null; ancestor = OwnerItem(ancestor))
        {
            if (!IsLast(ancestor))
            {
                var ax = Column(ancestor.Level);
                context.DrawLine(pen, new Point(ax, 0), new Point(ax, height));
            }
        }
    }

    private void OnTreeLayoutUpdated(object? sender, EventArgs e) => InvalidateVisual();

    private static double Column(int level) => Math.Floor(level * Indent + Indent / 2) + 0.5;

    private static TreeViewItem? OwnerItem(TreeViewItem item) => ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;

    private static bool IsFirst(TreeViewItem item)
    {
        var owner = ItemsControl.ItemsControlFromItemContainer(item);
        return owner is null || owner.IndexFromContainer(item) <= 0;
    }

    /// <summary>Last among its visible siblings, so the line below it stops.</summary>
    private static bool IsLast(TreeViewItem item)
    {
        var owner = ItemsControl.ItemsControlFromItemContainer(item);
        if (owner is null)
        {
            return true;
        }

        for (var i = owner.ItemCount - 1; i > owner.IndexFromContainer(item); i--)
        {
            if (owner.ContainerFromIndex(i) is not { IsVisible: false })
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Indents the expander box by the item's depth.</summary>
public sealed class TreeIndentConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly TreeIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        new Thickness(value is int level ? level * TreeLines.Indent : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
