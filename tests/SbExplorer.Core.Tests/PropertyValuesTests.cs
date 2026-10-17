using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Tests;

public class PropertyValuesTests
{
    [Theory]
    [InlineData("42", PropertyValueType.Int32, 42)]
    [InlineData("9000000000", PropertyValueType.Int64, 9000000000L)]
    [InlineData("1.5", PropertyValueType.Double, 1.5)]
    [InlineData("true", PropertyValueType.Boolean, true)]
    [InlineData("eu", PropertyValueType.String, "eu")]
    public void Parses_typed_values(string text, PropertyValueType type, object expected)
    {
        Assert.True(PropertyValues.TryParse(text, type, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parses_dates_as_utc_and_guids()
    {
        PropertyValues.TryParse("2026-10-17T12:00:00Z", PropertyValueType.DateTime, out var date, out _);
        PropertyValues.TryParse("0f8fad5b-d9cb-469f-a165-70867728950e", PropertyValueType.Guid, out var guid, out _);

        Assert.Equal(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), date);
        Assert.Equal(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), guid);
    }

    [Fact]
    public void Rejects_bad_values_with_a_message()
    {
        Assert.False(PropertyValues.TryParse("abc", PropertyValueType.Int32, out _, out var error));
        Assert.Equal("'abc' is not a valid Int32.", error);
    }

    [Fact]
    public void Round_trips_type_and_format()
    {
        object?[] values = [7, 7L, 2.5, false, "x", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Guid.Empty];
        foreach (var value in values)
        {
            var type = PropertyValues.TypeOf(value);
            Assert.True(PropertyValues.TryParse(PropertyValues.Format(value), type, out var parsed, out _));
            Assert.Equal(value, parsed);
        }
    }
}
