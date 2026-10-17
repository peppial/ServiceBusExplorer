using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SbExplorer.App.Views;

/// <summary>
/// Maps a node's IconKey ("Queue", "TopicsFolder", …) to the original explorer's icon in Assets/Tree.
/// The icons come from github.com/paolosalvatori/ServiceBusExplorer (MIT; see NOTICE).
/// </summary>
public sealed class IconConverter : IValueConverter
{
    public static readonly IconConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Load(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static Bitmap? Load(string key) => Cache.GetOrAdd(key, static name =>
    {
        var uri = new Uri($"avares://ServiceBusExplorer/Assets/Tree/{name}.png");
        return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
    });
}
