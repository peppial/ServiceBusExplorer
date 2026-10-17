using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SbExplorer.App.Views;

/// <summary>Maps a node's IconKey ("Queue", "Topic", …) to the matching DrawingImage resource in App.axaml.</summary>
public sealed class IconConverter : IValueConverter
{
    public static readonly IconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource($"{key}Icon", out var icon) == true
            ? icon as IImage
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
