#:package SkiaSharp

// Draws the app icon and writes the PNG sizes plus a Windows .ico.
//   dotnet run build/icon/make-icon.cs -- <output-dir>
// build/icon/make-icon.sh also builds the macOS .icns from the PNGs.
using SkiaSharp;

var output = args.Length > 0 ? args[0] : "src/SbExplorer.App/Assets";
Directory.CreateDirectory(output);

int[] pngSizes = [16, 32, 64, 128, 256, 512, 1024];
var pngs = pngSizes.ToDictionary(size => size, Render);

File.WriteAllBytes(Path.Combine(output, "icon.png"), pngs[512]);
var iconset = Path.Combine(output, "icon.iconset");
Directory.CreateDirectory(iconset);
foreach (var size in new[] { 16, 32, 128, 256, 512 })
{
    File.WriteAllBytes(Path.Combine(iconset, $"icon_{size}x{size}.png"), pngs[size]);
    File.WriteAllBytes(Path.Combine(iconset, $"icon_{size}x{size}@2x.png"), pngs[size * 2]);
}

WriteIco(Path.Combine(output, "icon.ico"), [16, 24, 32, 48, 64, 128, 256]);
Console.WriteLine($"Wrote icon.png, icon.ico and icon.iconset/ to {output}");

byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 1024f);

    // Rounded tile with a blue gradient, inset like a macOS app icon.
    using var tile = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 100), new SKPoint(0, 924),
            [new SKColor(0x2E, 0x8F, 0xE6), new SKColor(0x0A, 0x4F, 0xA3)], SKShaderTileMode.Clamp),
    };
    canvas.DrawRoundRect(new SKRoundRect(new SKRect(100, 100, 924, 924), 190), tile);

    // Three messages queued left to right, fading as they wait.
    float[] opacity = [1f, 0.78f, 0.55f];
    for (var i = 0; i < 3; i++)
    {
        var top = 300 + i * 150;
        using var bar = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * opacity[i])) };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(250, top, 774, top + 110), 55), bar);
        using var dot = new SKPaint { IsAntialias = true, Color = new SKColor(0x0A, 0x4F, 0xA3).WithAlpha((byte)(255 * opacity[i])) };
        canvas.DrawCircle(310, top + 55, 26, dot);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// An .ico is a small directory followed by PNG-encoded images (supported since Windows Vista).
void WriteIco(string path, int[] sizes)
{
    var images = sizes.Select(s => (Size: s, Png: pngs.TryGetValue(s, out var p) ? p : Render(s))).ToList();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
    {
        writer.Write(png);
    }
}
