using System.Globalization;

namespace SbExplorer.Core.Messaging;

public enum PropertyValueType
{
    String,
    Int32,
    Int64,
    Double,
    Boolean,
    DateTime,
    Guid,
}

/// <summary>Converts application property values between their typed form and text the user can edit.</summary>
public static class PropertyValues
{
    public static bool TryParse(string text, PropertyValueType type, out object? value, out string? error)
    {
        error = null;
        var culture = CultureInfo.InvariantCulture;
        var ok = true;
        switch (type)
        {
            case PropertyValueType.String:
                value = text;
                break;
            case PropertyValueType.Int32:
                ok = int.TryParse(text, NumberStyles.Integer, culture, out var i);
                value = i;
                break;
            case PropertyValueType.Int64:
                ok = long.TryParse(text, NumberStyles.Integer, culture, out var l);
                value = l;
                break;
            case PropertyValueType.Double:
                ok = double.TryParse(text, NumberStyles.Float, culture, out var d);
                value = d;
                break;
            case PropertyValueType.Boolean:
                ok = bool.TryParse(text, out var b);
                value = b;
                break;
            case PropertyValueType.DateTime:
                ok = DateTimeOffset.TryParse(text, culture, DateTimeStyles.AssumeUniversal, out var dt);
                value = dt.UtcDateTime;
                break;
            case PropertyValueType.Guid:
                ok = Guid.TryParse(text, out var g);
                value = g;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        if (!ok)
        {
            value = null;
            error = $"'{text}' is not a valid {type}.";
        }

        return ok;
    }

    public static PropertyValueType TypeOf(object? value) => value switch
    {
        int or short or byte or sbyte or ushort => PropertyValueType.Int32,
        long or uint or ulong => PropertyValueType.Int64,
        double or float or decimal => PropertyValueType.Double,
        bool => PropertyValueType.Boolean,
        DateTime or DateTimeOffset => PropertyValueType.DateTime,
        Guid => PropertyValueType.Guid,
        _ => PropertyValueType.String,
    };

    public static string Format(object? value) => value switch
    {
        null => "null",
        DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
