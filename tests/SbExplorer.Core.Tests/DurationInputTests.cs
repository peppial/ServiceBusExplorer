using SbExplorer.Core.Management;

namespace SbExplorer.Core.Tests;

public class DurationInputTests
{
    public static TheoryData<string, TimeSpan> Valid => new()
    {
        { "30s", TimeSpan.FromSeconds(30) },
        { "5m", TimeSpan.FromMinutes(5) },
        { "1.5h", TimeSpan.FromMinutes(90) },
        { "14D", TimeSpan.FromDays(14) },
        { "00:01:00", TimeSpan.FromMinutes(1) },
        { "1.00:00:00", TimeSpan.FromDays(1) },
        { "never", TimeSpan.MaxValue },
        { " Never ", TimeSpan.MaxValue },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Parses(string text, TimeSpan expected)
    {
        Assert.True(DurationInput.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0m")]
    [InlineData("-5m")]
    [InlineData("00:00:00")]
    public void Rejects(string text) => Assert.False(DurationInput.TryParse(text, out _));

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(300, "5m")]
    [InlineData(3600, "1h")]
    [InlineData(5400, "90m")]
    [InlineData(86400 * 14, "14d")]
    [InlineData(90.5, "00:01:30.5000000")]
    public void Formats_shortest_round_trippable_text(double seconds, string expected)
    {
        var value = TimeSpan.FromSeconds(seconds);

        Assert.Equal(expected, DurationInput.Format(value));
        Assert.True(DurationInput.TryParse(DurationInput.Format(value), out var parsed));
        Assert.Equal(value, parsed);
    }

    [Fact]
    public void Formats_service_bus_unlimited_as_never()
    {
        Assert.Equal("Never", DurationInput.Format(TimeSpan.MaxValue));
        Assert.Equal("Never", DurationInput.Format(TimeSpan.FromDays(10675199)));
    }
}
