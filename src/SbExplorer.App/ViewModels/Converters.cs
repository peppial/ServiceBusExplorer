using Avalonia.Data.Converters;

namespace SbExplorer.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter DimmedOpacity =
        new FuncValueConverter<bool, double>(dimmed => dimmed ? 0.5 : 1.0);
}
