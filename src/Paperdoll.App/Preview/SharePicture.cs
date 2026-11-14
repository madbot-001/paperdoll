using Paperdoll.Core.Rooms;
using SkiaSharp;

namespace Paperdoll.App.Preview;

/// <summary>What is behind the character in a picture to share.</summary>
public enum ShareBackground
{
    Floor = 0,
    Plain = 1,
    Transparent = 2,

    // 3 was a background no longer offered; the share window takes it for Floor.

    /// <summary>The demo room, as the preview shows it.</summary>
    Room = 4,
}

/// <summary>How a picture to share is laid out; remembered between runs.</summary>
/// <param name="AllSides">All four facings, or the front only.</param>
/// <param name="Zoom">Screen pixels per game pixel.</param>
/// <param name="Caption">The character's name and species under the picture.</param>
/// <param name="Credit">A line naming where the sprites come from and their licences.</param>
/// <param name="RoomTiles">How much of the demo room each panel shows: 0 close round the character, 3, 5 or 7 tiles across, or <see cref="WholeRoom"/>.</param>
public sealed record ShareOptions(bool AllSides = true, int Zoom = 4, ShareBackground Background = ShareBackground.Floor, bool Caption = true, bool Credit = true, int RoomTiles = 0)
{
    /// <summary>A <see cref="RoomTiles"/> showing every tile of the demo room.</summary>
    public const int WholeRoom = -1;
}

/// <summary>The words under a picture to share.</summary>
public sealed record ShareText(string Title, string Subtitle, string Credit);

/// <summary>
/// A picture of the character to share: each facing in its own panel, as in the preview's strip,
/// with the name and a sprite credit underneath.
/// </summary>
public static class SharePicture
{
    private static readonly SKColor Frame = new(0x16, 0x19, 0x1D);
    private static readonly SKColor Plain = new(0x2A, 0x2F, 0x35);
    private static readonly SKColor TitleColor = new(0xE6, 0xEA, 0xEE);
    private static readonly SKColor SubtitleColor = new(0xA7, 0xB1, 0xBC);
    private static readonly SKColor CreditColor = new(0x84, 0x8F, 0x9B);
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;

    /// <summary>The sizes offered, in screen pixels per game pixel.</summary>
    public static readonly int[] Zooms = [2, 3, 4, 6, 8];

    /// <summary>The most pixels a side of a picture may have; a wide view of the room is drawn smaller to stay within it.</summary>
    public const int MaxSide = 8192;

    /// <param name="sides">The character facing each way, in the order to show them.</param>
    /// <param name="scale">The character's species and height scale, as in the preview.</param>
    /// <param name="room">The demo room for <see cref="ShareBackground.Room"/>; without it, the plates.</param>
    public static SKBitmap Compose(IReadOnlyList<SKBitmap> sides, (float X, float Y) scale, ShareOptions options, ShareText text, RoomPicture? room = null)
    {
        var (panelWidth, panelHeight, zoom, view) = Panel(sides, scale, options, options.Background == ShareBackground.Room ? room : null);
        var (cellWidth, cellHeight) = (view.Width, view.Height);
        var pad = 6 * zoom;
        var gap = zoom;
        var cellsWidth = sides.Count * cellWidth + (sides.Count + 1) * gap;

        var lines = new List<(string Text, float Size, SKColor Color)>();
        if (options.Caption)
        {
            lines.Add((text.Title, 7 * zoom, TitleColor));
            if (text.Subtitle.Length > 0)
                lines.Add((text.Subtitle, 4.5f * zoom, SubtitleColor));
        }
        if (options.Credit && text.Credit.Length > 0)
            lines.Add((text.Credit, Math.Max(11, 3 * zoom), CreditColor));

        var fonts = lines.Select(l => new SKFont(Face, l.Size) { Edging = SKFontEdging.Antialias }).ToList();
        var textWidth = lines.Count == 0 ? 0 : (int)Math.Ceiling(lines.Select((l, i) => fonts[i].MeasureText(l.Text)).Max()) + 2 * pad;
        var captionHeight = lines.Count == 0 ? 0 : (int)Math.Ceiling(lines.Sum(l => l.Size * 1.35f)) + 2 * pad;

        var width = Math.Max(cellsWidth, textWidth);
        var height = cellHeight + 2 * gap + captionHeight;
        var picture = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(picture);
        canvas.Clear(options.Background == ShareBackground.Transparent ? SKColors.Transparent : Frame);

        var left = (width - cellsWidth) / 2 + gap;
        foreach (var side in sides)
        {
            using var cell = FloorCanvas.Compose(panelWidth, panelHeight, zoom, side, scale,
                floor: options.Background is ShareBackground.Floor or ShareBackground.Room,
                room: options.Background == ShareBackground.Room ? room : null);
            if (options.Background == ShareBackground.Plain)
            {
                using var fill = new SKPaint { Color = Plain };
                canvas.DrawRect(left, gap, cellWidth, cellHeight, fill);
            }
            canvas.DrawBitmap(cell, view, SKRect.Create(left, gap, cellWidth, cellHeight));
            left += cellWidth + gap;
        }

        // On a transparent picture the words get a dark edge, so they read on light and dark pages alike.
        var baseline = cellHeight + 2 * gap + pad;
        for (var i = 0; i < lines.Count; i++)
        {
            var (line, size, color) = lines[i];
            baseline += (int)Math.Ceiling(size * 1.1f);
            if (options.Background == ShareBackground.Transparent)
            {
                using var edge = new SKPaint { Color = Frame, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, zoom * 0.75f), StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
                canvas.DrawText(line, width / 2f, baseline, SKTextAlign.Center, fonts[i], edge);
            }
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawText(line, width / 2f, baseline, SKTextAlign.Center, fonts[i], paint);
            baseline += (int)Math.Ceiling(size * 0.25f);
        }
        foreach (var font in fonts)
            font.Dispose();
        return picture;
    }

    // A panel: the biggest side and 6 game pixels around it.
    private static (int Width, int Height) CellSize(IReadOnlyList<SKBitmap> sides, (float X, float Y) scale, int zoom) =>
        ((int)Math.Ceiling(sides.Max(s => s.Width * zoom * scale.X)) + 12 * zoom, (int)Math.Ceiling(sides.Max(s => s.Height * zoom * scale.Y)) + 12 * zoom);

    /// <summary>
    /// The size of each panel and the zoom it is drawn at. Close round the character, or with the demo
    /// room behind, as many tiles of it as <see cref="ShareOptions.RoomTiles"/> asks, the character in
    /// the middle and never cut off; drawn smaller when the picture would pass <see cref="MaxSide"/>.
    /// View is the part of the panel the picture shows: all of it, or for the whole room the room
    /// alone, without the empty space that keeping the character in the middle would add.
    /// </summary>
    /// <param name="room">The demo room drawn behind; without it, the panel is close round the character.</param>
    public static (int Width, int Height, int Zoom, SKRectI View) Panel(IReadOnlyList<SKBitmap> sides, (float X, float Y) scale, ShareOptions options, RoomPicture? room)
    {
        var zoom = Math.Clamp(options.Zoom, 1, 16);
        // An unknown value, from settings edited by hand, shows the room close.
        if (options.Background != ShareBackground.Room || room == null || options.RoomTiles is not (3 or 5 or 7 or ShareOptions.WholeRoom))
        {
            var (width, height) = CellSize(sides, scale, zoom);
            return (width, height, zoom, SKRectI.Create(width, height));
        }

        // The asked zoom, then each smaller one offered, then 1.
        var (cellWidth, cellHeight) = (0, 0);
        var view = SKRectI.Empty;
        foreach (var z in Zooms.Where(z => z < zoom).Reverse().Prepend(zoom).Append(1))
        {
            var (closeWidth, closeHeight) = (cellWidth, cellHeight) = CellSize(sides, scale, z);
            if (options.RoomTiles == ShareOptions.WholeRoom)
            {
                // Out to the room picture's farthest edge each way from the middle of the character's square.
                var (middleX, middleY) = (room.StandLeft + 16, room.StandTop + 16);
                cellWidth = Math.Max(cellWidth, 2 * Math.Max(middleX, room.Below.Width - middleX) * z);
                cellHeight = Math.Max(cellHeight, 2 * Math.Max(middleY, room.Below.Height - middleY) * z);
                // Of that, the room and the close view of the character, whichever reaches farther.
                var (closeLeft, closeTop) = ((cellWidth - closeWidth) / 2, (cellHeight - closeHeight) / 2);
                var (roomLeft, roomTop) = (cellWidth / 2 - middleX * z, cellHeight / 2 - middleY * z);
                view = new SKRectI(Math.Max(0, Math.Min(closeLeft, roomLeft)), Math.Max(0, Math.Min(closeTop, roomTop)),
                    Math.Min(cellWidth, Math.Max(closeLeft + closeWidth, roomLeft + room.Below.Width * z)),
                    Math.Min(cellHeight, Math.Max(closeTop + closeHeight, roomTop + room.Below.Height * z)));
            }
            else
            {
                // An odd number of tiles each way, so the view ends on whole tiles; 7 across keeps 5 down.
                cellWidth = Math.Max(cellWidth, options.RoomTiles * 32 * z);
                cellHeight = Math.Max(cellHeight, Math.Min(options.RoomTiles, 5) * 32 * z);
                view = SKRectI.Create(cellWidth, cellHeight);
            }
            // The words under the panels at their tallest: the name, the species and the credit.
            var words = (options.Caption ? 11.5f * z : 0) + (options.Credit ? Math.Max(11, 3 * z) : 0);
            var wordsHeight = words > 0 ? (int)Math.Ceiling(words * 1.35f) + 1 + 12 * z : 0;
            if (sides.Count * view.Width + (sides.Count + 1) * z <= MaxSide && view.Height + 2 * z + wordsHeight <= MaxSide)
                return (cellWidth, cellHeight, z, view);
        }
        return (cellWidth, cellHeight, 1, view);
    }

    /// <summary>The credits of the demo room's pictures a picture with these sides and options draws in its panels.</summary>
    /// <param name="picture">The room as drawn, which sets how far a view of the whole room reaches.</param>
    public static IReadOnlyList<Core.Editing.CreditLine> RoomCredits(IReadOnlyList<SKBitmap> sides, (float X, float Y) scale, ShareOptions options, RoomScene room, RoomPicture? picture = null)
    {
        var (width, height, zoom, _) = Panel(sides, scale, options, picture);
        return FloorCanvas.CreditsShown(room, width, height, zoom);
    }

    public static byte[] Png(SKBitmap picture)
    {
        using var data = picture.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
