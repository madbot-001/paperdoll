using Avalonia.Media.Imaging;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Rooms;
using SkiaSharp;

namespace Paperdoll.App.Preview;

/// <summary>What the preview stands the character on.</summary>
public enum FloorMode
{
    /// <summary>Grey plates drawn by Paperdoll.</summary>
    Plain = 0,

    // 1 was a floor no longer offered; settings holding it load as Plain.

    /// <summary>A small hand-made room drawn from the fork's sprites.</summary>
    DemoRoom = 2,

    /// <summary>One flat colour, with no plates or seams.</summary>
    Blank = 3,
}

/// <summary>The squares a picture shows, counted from the one under the character: columns east, rows south.</summary>
public readonly record struct TileSpan(int Left, int Right, int Top, int Bottom);

/// <summary>
/// Draws station floor plates at game scale (one plate = one 32-pixel tile). Drawn here rather
/// than taken from a fork, so they need no sprite credit. In their place it can draw a demo room,
/// whose pictures are credited (<see cref="CreditsShown(RoomScene, int, int, int)"/>).
/// </summary>
public static class FloorCanvas
{
    /// <summary>The plates' fill, which a tile without a picture also gets.</summary>
    public static readonly SKColor Plate = new(0x30, 0x36, 0x3D);
    private static readonly SKColor Edge = new(0x34, 0x3A, 0x42);
    private static readonly SKColor Seam = new(0x2A, 0x2F, 0x35);
    private static readonly SKColor Space = new(0x0B, 0x0D, 0x10);
    private const int Tile = 32;

    /// <summary>Canvas with the sprite centred, scaled by <paramref name="zoom"/> and the character's own species/height scale.</summary>
    /// <param name="floor">Whether to draw the floor plates; without them the canvas is transparent.</param>
    /// <param name="room">The demo room to draw in place of the plates, its standing square under the character; what stands in front of the character is drawn over it, and its light, if lit, lights the character.</param>
    /// <param name="blank">Whether to fill the floor with the plates' colour alone, without plates or seams.</param>
    public static SKBitmap Compose(int width, int height, int zoom, SKBitmap sprite, (float X, float Y)? scale = null, bool floor = true, RoomPicture? room = null, bool blank = false)
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
        room = floor ? room : null;
        if (room != null)
        {
            canvas.Clear(Space);
            DrawRoom(canvas, boxX, boxY, zoom, room, room.Below);
        }
        else if (floor && blank)
            canvas.Clear(Plate);
        for (var y = startY; floor && room == null && !blank && y < height; y += plate)
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
        using var lit = room?.Light != null ? room.LightCharacter(sprite, scaleX, scaleY) : null;
        using var image = SKImage.FromBitmap(lit ?? sprite);
        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Nearest), null);
        if (room != null)
            DrawRoom(canvas, boxX, boxY, zoom, room, room.Above);
        return bitmap;
    }

    // One of the room's pictures, scaled up, with the standing square on the character's.
    private static void DrawRoom(SKCanvas canvas, int boxX, int boxY, int zoom, RoomPicture room, SKImage picture) =>
        canvas.DrawImage(picture, SKRect.Create(boxX - room.StandLeft * zoom, boxY - room.StandTop * zoom, picture.Width * zoom, picture.Height * zoom),
            new SKSamplingOptions(SKFilterMode.Nearest), null);

    /// <summary>The squares a canvas of this size shows around the character's.</summary>
    public static TileSpan Span(int width, int height, int zoom)
    {
        var plate = Tile * zoom;
        var boxX = width / 2 - Tile * zoom / 2;
        var boxY = height / 2 - Tile * zoom / 2;
        return new TileSpan(-Ceiling(boxX, plate), Ceiling(width - boxX, plate) - 1, -Ceiling(boxY, plate), Ceiling(height - boxY, plate) - 1);
    }

    private static int Ceiling(int value, int step) => (value + step - 1) / step;

    /// <summary>The credits of the pictures of a demo room a canvas of this size shows, by path.</summary>
    public static IReadOnlyList<CreditLine> CreditsShown(RoomScene room, int width, int height, int zoom)
    {
        // Columns east and rows south of the standing square, which the canvas shows in part or whole.
        var span = Span(width, height, zoom);
        var (x, y) = room.Room.Stand;
        return room.CreditsWithin(new Core.Rendering.Box(x + span.Left, y - span.Bottom, x + span.Right + 1, y + 1 - span.Top));
    }

    /// <summary>The picture as the window shows it, its pixels copied straight across.</summary>
    public static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        // Every picture Paperdoll makes is already in this form; anything else is drawn into it.
        if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Premul)
        {
            using var converted = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(converted))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(bitmap, 0, 0);
            }
            return ToAvalonia(converted);
        }

        var result = new WriteableBitmap(new Avalonia.PixelSize(Math.Max(1, bitmap.Width), Math.Max(1, bitmap.Height)), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using var target = result.Lock();
        var source = bitmap.GetPixels();
        var rowBytes = Math.Min(bitmap.RowBytes, target.RowBytes);
        var row = new byte[rowBytes];
        for (var y = 0; y < bitmap.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(source + y * bitmap.RowBytes, row, 0, rowBytes);
            System.Runtime.InteropServices.Marshal.Copy(row, 0, target.Address + y * target.RowBytes, rowBytes);
        }
        return result;
    }
}
