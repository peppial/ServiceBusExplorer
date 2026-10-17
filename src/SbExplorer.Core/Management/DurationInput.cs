using System.Globalization;
using System.Text.RegularExpressions;

namespace SbExplorer.Core.Management;

/// <summary>Durations typed into entity settings: <c>30s</c>, <c>5m</c>, <c>2h</c>, <c>7d</c>, <c>1.00:00:00</c> or <c>never</c>.</summary>
public static partial class DurationInput
{
    public const string NeverText = "Never";

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Equals(NeverText, StringComparison.OrdinalIgnoreCase) || trimmed.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            value = TimeSpan.MaxValue;
            return true;
        }

        var shorthand = ShorthandPattern().Match(trimmed);
        if (shorthand.Success)
        {
            var amount = double.Parse(shorthand.Groups["n"].Value, CultureInfo.InvariantCulture);
            value = shorthand.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            return value > TimeSpan.Zero;
        }

        return TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out value) && value > TimeSpan.Zero;
    }

    /// <summary>The shortest round-trippable text: <c>5m</c>, <c>1h</c>, <c>14d</c>, <c>Never</c>, else <c>d.hh:mm:ss</c>.</summary>
    public static string Format(TimeSpan value)
    {
        if (value >= TimeSpan.FromDays(10_000))
        {
            return NeverText;
        }

        if (value.Ticks % TimeSpan.TicksPerDay == 0)
        {
            return $"{(long)value.TotalDays}d";
        }

        if (value.Ticks % TimeSpan.TicksPerHour == 0)
        {
            return $"{(long)value.TotalHours}h";
        }

        if (value.Ticks % TimeSpan.TicksPerMinute == 0)
        {
            return $"{(long)value.TotalMinutes}m";
        }

        if (value.Ticks % TimeSpan.TicksPerSecond == 0)
        {
            return $"{(long)value.TotalSeconds}s";
        }

        return value.ToString("c", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^(?<n>\d+(\.\d+)?)\s*(?<unit>[smhdSMHD])$")]
    private static partial Regex ShorthandPattern();
}
