using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace SbExplorer.Core.Messaging;

public enum BodyFormat
{
    Empty,
    Json,
    Xml,
    Text,
    Binary,
}

public sealed record FormattedBody(BodyFormat Format, string Text)
{
    public bool IsBinary => Format == BodyFormat.Binary;
}

public static class MessageBodyFormatter
{
    /// <summary>Hex dumps beyond this are truncated; the full body can still be saved to a file.</summary>
    public const int MaxHexBytes = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Picks the most readable view: pretty JSON or XML when the content type says so or the text looks like it,
    /// plain text for valid UTF-8, otherwise a hex dump.
    /// </summary>
    public static FormattedBody Format(ReadOnlySpan<byte> body, string? contentType)
    {
        if (body.IsEmpty)
        {
            return new FormattedBody(BodyFormat.Empty, string.Empty);
        }

        if (!TryDecodeText(body, out var text))
        {
            return new FormattedBody(BodyFormat.Binary, HexDump(body));
        }

        var type = contentType?.ToLowerInvariant() ?? string.Empty;
        var trimmed = text.TrimStart();
        var looksJson = type.Contains("json") || trimmed.StartsWith('{') || trimmed.StartsWith('[');
        var looksXml = type.Contains("xml") || trimmed.StartsWith('<');

        if (looksJson && TryPrettyJson(text, out var json))
        {
            return new FormattedBody(BodyFormat.Json, json);
        }

        if (looksXml && TryPrettyXml(text, out var xml))
        {
            return new FormattedBody(BodyFormat.Xml, xml);
        }

        return new FormattedBody(BodyFormat.Text, text);
    }

    public static bool TryPrettyJson(string text, out string pretty)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            pretty = JsonSerializer.Serialize(document.RootElement, Indented);
            return true;
        }
        catch (JsonException)
        {
            pretty = text;
            return false;
        }
    }

    public static bool TryPrettyXml(string text, out string pretty)
    {
        try
        {
            pretty = XDocument.Parse(text).ToString();
            return true;
        }
        catch (XmlException)
        {
            pretty = text;
            return false;
        }
    }

    public static string HexDump(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, MaxHexBytes);
        var builder = new StringBuilder((length / 16 + 1) * 78);
        for (var offset = 0; offset < length; offset += 16)
        {
            var line = bytes.Slice(offset, Math.Min(16, length - offset));
            builder.Append(offset.ToString("X8")).Append("  ");
            for (var i = 0; i < 16; i++)
            {
                builder.Append(i < line.Length ? line[i].ToString("X2") + " " : "   ");
                if (i == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(' ');
            foreach (var b in line)
            {
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            builder.Append('\n');
        }

        if (bytes.Length > length)
        {
            builder.Append($"… {bytes.Length - length:N0} more bytes not shown");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static bool TryDecodeText(ReadOnlySpan<byte> body, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }

        // Valid UTF-8 can still be binary (e.g. mostly zero bytes); treat control characters as binary.
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            {
                return false;
            }
        }

        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        return true;
    }
}
