using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Tests;

public class ScheduleTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(3));

    [Theory]
    [InlineData("+30s", 30)]
    [InlineData("+15m", 15 * 60)]
    [InlineData(" +2h ", 2 * 3600)]
    [InlineData("+1d", 86400)]
    public void Parses_relative_offsets(string text, int seconds)
    {
        Assert.True(ScheduleTime.TryParse(text, Now, out var value, out var error), error);
        Assert.Equal(Now.AddSeconds(seconds), value);
    }

    [Fact]
    public void Parses_absolute_local_times()
    {
        var future = DateTime.Now.AddDays(2);
        var text = future.ToString("yyyy-MM-dd HH:mm");

        Assert.True(ScheduleTime.TryParse(text, DateTimeOffset.Now, out var value, out _));
        Assert.Equal(new DateTimeOffset(future.Year, future.Month, future.Day, future.Hour, future.Minute, 0, value.Offset), value);
    }

    [Theory]
    [InlineData("tomorrow", "Use a local time")]
    [InlineData("+0m", "must be in the future")]
    [InlineData("2001-01-01 10:00", "must be in the future")]
    public void Rejects_bad_or_past_times(string text, string expected)
    {
        Assert.False(ScheduleTime.TryParse(text, Now, out _, out var error));
        Assert.Contains(expected, error);
    }
}
