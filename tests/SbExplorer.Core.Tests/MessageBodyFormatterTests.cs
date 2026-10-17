using System.Text;
using SbExplorer.Core.Messaging;

namespace SbExplorer.Core.Tests;

public class MessageBodyFormatterTests
{
    // Offset (8) + 2 spaces + 16 hex pairs with trailing spaces (48) + mid gap (1) + separator (1).
    private const int AsciiColumn = 60;

    private static FormattedBody Format(string text, string? contentType = null) =>
        MessageBodyFormatter.Format(Encoding.UTF8.GetBytes(text), contentType);

    [Fact]
    public void Pretty_prints_json_by_content_type_or_by_shape()
    {
        var byType = Format("""{"a":1,"b":[true,"é"]}""", "application/json");
        var bySniff = Format("""  [1,2]""");

        Assert.Equal(BodyFormat.Json, byType.Format);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    \"é\"\n  ]\n}", byType.Text.ReplaceLineEndings("\n"));
        Assert.Equal(BodyFormat.Json, bySniff.Format);
    }

    [Fact]
    public void Invalid_json_falls_back_to_text()
    {
        var body = Format("{not json", "application/json");

        Assert.Equal(BodyFormat.Text, body.Format);
        Assert.Equal("{not json", body.Text);
    }

    [Fact]
    public void Pretty_prints_xml()
    {
        var body = Format("<payment><id>P-1</id></payment>", "application/xml");

        Assert.Equal(BodyFormat.Xml, body.Format);
        Assert.Equal("<payment>\n  <id>P-1</id>\n</payment>", body.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Plain_text_stays_as_is_and_bom_is_stripped()
    {
        Assert.Equal(new FormattedBody(BodyFormat.Text, "hello\r\nworld\t!"), Format("hello\r\nworld\t!"));
        Assert.Equal("hi", MessageBodyFormatter.Format([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'], null).Text);
    }

    [Fact]
    public void Binary_and_control_characters_become_hex()
    {
        var invalidUtf8 = MessageBodyFormatter.Format(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x7F, 0x80 }, "application/octet-stream");
        var controlChars = MessageBodyFormatter.Format(new byte[] { 0x41, 0x00, 0x42 }, null);

        Assert.Equal(BodyFormat.Binary, invalidUtf8.Format);
        Assert.StartsWith("00000000  00 01 02 FF FE 7F 80 ", invalidUtf8.Text);
        Assert.Equal(".......", invalidUtf8.Text[AsciiColumn..]);
        Assert.Equal(BodyFormat.Binary, controlChars.Format);
    }

    [Fact]
    public void Empty_body()
    {
        Assert.Equal(BodyFormat.Empty, MessageBodyFormatter.Format([], "application/json").Format);
    }

    [Fact]
    public void Hex_dump_lays_out_16_bytes_per_line_and_truncates()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)(0x41 + i)).ToArray();
        var lines = MessageBodyFormatter.HexDump(bytes).Split('\n');

        Assert.Equal("00000000  41 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("00000010  51 52 53 54 ", lines[1]);
        Assert.Equal("QRST", lines[1][AsciiColumn..]);
        Assert.Equal("ABCDEFGHIJKLMNOP", lines[0][AsciiColumn..]);

        var large = MessageBodyFormatter.HexDump(new byte[MessageBodyFormatter.MaxHexBytes + 10]);
        Assert.EndsWith("… 10 more bytes not shown", large);
    }
}
