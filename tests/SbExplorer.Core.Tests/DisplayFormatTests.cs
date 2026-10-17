using SbExplorer.Core.Management;

namespace SbExplorer.Core.Tests;

public class DisplayFormatTests
{
    public static TheoryData<TimeSpan, string> Durations => new()
    {
        { TimeSpan.MaxValue, "Never" },
        { TimeSpan.FromDays(10675199), "Never" },
        { TimeSpan.Zero, "0 s" },
        { TimeSpan.FromSeconds(30), "30 s" },
        { TimeSpan.FromMinutes(1), "1 min" },
        { TimeSpan.FromMinutes(90), "1 h 30 min" },
        { TimeSpan.FromDays(1), "1 day" },
        { TimeSpan.FromDays(14), "14 days" },
        { TimeSpan.FromMilliseconds(1500), "1.5 s" },
    };

    [Theory]
    [MemberData(nameof(Durations))]
    public void Formats_durations(TimeSpan value, string expected) => Assert.Equal(expected, DisplayFormat.Duration(value));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void Formats_sizes(long bytes, string expected) => Assert.Equal(expected, DisplayFormat.Size(bytes));

    [Fact]
    public void Unset_timestamps_show_a_dash()
    {
        Assert.Equal("—", DisplayFormat.Timestamp(DateTimeOffset.MinValue));
        Assert.Equal("—", DisplayFormat.Timestamp(new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
