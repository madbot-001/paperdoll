using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperdoll.App.Preview;

/// <summary>
/// Draws a character standing on station floor plates, at the game's scale: one plate is one
/// 32-pixel tile, so the character is the size it is in the game. The plates are drawn here, not
/// taken from a fork, so they need no credit.
/// </summary>
public static class FloorCanvas
{
    private static readonly SKColor Plate = new(0x30, 0x36, 0x3D);
    private static readonly SKColor Edge = new(0x34, 0x3A, 0x42);
    private static readonly SKColor Seam = new(0x2A, 0x2F, 0x35);
    private const int Tile = 32;

    /// <summary>
    /// A canvas of the given size with the sprite in the middle, scaled by <paramref name="zoom"/>
    /// and by the character's own scale (species size and height), as the game scales it.
    /// </summary>
    public static SKBitmap Compose(int width, int height, int zoom, SKBitmap sprite, (float X, float Y)? scale = null)
    {
        var (scaleX, scaleY) = scale ?? (1f, 1f);
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        var plate = Tile * zoom;

        // The plate under the character's middle tile lines up with the sprite's 32-pixel box.
        var boxX = width / 2 - Tile * zoom / 2;
        var boxY = height / 2 - Tile * zoom / 2;
        var startX = boxX % plate - plate;
        var startY = boxY % plate - plate;

        using var paint = new SKPaint { IsAntialias = false };
        for (var y = startY; y < height; y += plate)
        {
            for (var x = startX; x < width; x += plate)
            {
                paint.Color = Plate;
                canvas.DrawRect(x, y, plate, plate, paint);
                paint.Color = Edge;
                canvas.DrawRect(x, y, plate, zoom, paint);
                canvas.DrawRect(x, y, zoom, plate, paint);
                paint.Color = Seam;
                canvas.DrawRect(x, y + plate - zoom, plate, zoom, paint);
                canvas.DrawRect(x + plate - zoom, y, zoom, plate, paint);
            }
        }

        var w = sprite.Width * zoom * scaleX;
        var h = sprite.Height * zoom * scaleY;
        var destination = SKRect.Create(width / 2f - w / 2f, height / 2f - h / 2f, w, h);
        using var image = SKImage.FromBitmap(sprite);
        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return bitmap;
    }

    /// <summary>Converts a Skia bitmap for display.</summary>
    public static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
