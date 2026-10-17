using System.Globalization;
using System.Text.RegularExpressions;

namespace SbExplorer.Core.Messaging;

/// <summary>Parses when to schedule a message: a local date and time, or an offset such as <c>+15m</c>.</summary>
public static partial class ScheduleTime
{
    private static readonly string[] AbsoluteFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    public static bool TryParse(string text, DateTimeOffset now, out DateTimeOffset value, out string? error)
    {
        value = default;
        error = null;
        var trimmed = text.Trim();

        var relative = RelativePattern().Match(trimmed);
        if (relative.Success)
        {
            var amount = int.Parse(relative.Groups["n"].Value, CultureInfo.InvariantCulture);
            value = relative.Groups["unit"].Value switch
            {
                "s" => now.AddSeconds(amount),
                "m" => now.AddMinutes(amount),
                "h" => now.AddHours(amount),
                _ => now.AddDays(amount),
            };
        }
        else if (DateTime.TryParseExact(trimmed, AbsoluteFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
        {
            value = new DateTimeOffset(local);
        }
        else
        {
            error = "Use a local time like 2026-10-17 18:30, or an offset like +15m, +2h or +1d.";
            return false;
        }

        if (value <= now)
        {
            error = "The scheduled time must be in the future.";
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"^\+(?<n>\d{1,6})\s*(?<unit>[smhd])$")]
    private static partial Regex RelativePattern();
}
