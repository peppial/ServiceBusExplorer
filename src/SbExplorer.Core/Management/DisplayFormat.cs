using System.Globalization;

namespace SbExplorer.Core.Management;

/// <summary>Human-readable formatting for property values shown in the UI.</summary>
public static class DisplayFormat
{
    // Service Bus reports "no limit" as TimeSpan.MaxValue, sometimes rounded down by serialization.
    private static readonly TimeSpan Unlimited = TimeSpan.FromDays(10_000);

    public static string Duration(TimeSpan value)
    {
        if (value >= Unlimited)
        {
            return "Never";
        }

        if (value <= TimeSpan.Zero)
        {
            return "0 s";
        }

        var parts = new List<string>(4);
        if (value.Days > 0)
        {
            parts.Add(value.Days == 1 ? "1 day" : $"{value.Days} days");
        }

        if (value.Hours > 0)
        {
            parts.Add($"{value.Hours} h");
        }

        if (value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} min");
        }

        if (value.Seconds > 0 || value.Milliseconds > 0)
        {
            parts.Add(value.Milliseconds > 0
                ? $"{value.TotalSeconds % 60:0.###} s".Replace(',', '.')
                : $"{value.Seconds} s");
        }

        return string.Join(" ", parts);
    }

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }

    public static string Timestamp(DateTimeOffset value) =>
        value <= DateTimeOffset.MinValue.AddDays(1) || value.Year < 1971
            ? "—"
            : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string YesNo(bool value) => value ? "Yes" : "No";

    public static string OrDash(string? value) => string.IsNullOrEmpty(value) ? "—" : value;
}
