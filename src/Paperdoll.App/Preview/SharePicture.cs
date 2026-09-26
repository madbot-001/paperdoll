using SkiaSharp;

namespace Paperdoll.App.Preview;

/// <summary>What is behind the character in a picture to share.</summary>
public enum ShareBackground
{
    Floor,
    Plain,
    Transparent,
}

/// <summary>How a picture to share is laid out; remembered between runs.</summary>
/// <param name="AllSides">All four facings, or the front only.</param>
/// <param name="Zoom">Screen pixels per game pixel.</param>
/// <param name="Caption">The character's name and species under the picture.</param>
/// <param name="Credit">A line naming where the sprites come from and their licences.</param>
public sealed record ShareOptions(bool AllSides = true, int Zoom = 4, ShareBackground Background = ShareBackground.Floor, bool Caption = true, bool Credit = true);

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

    /// <param name="sides">The character facing each way, in the order to show them.</param>
    /// <param name="scale">The character's species and height scale, as in the preview.</param>
    public static SKBitmap Compose(IReadOnlyList<SKBitmap> sides, (float X, float Y) scale, ShareOptions options, ShareText text)
    {
        var zoom = Math.Clamp(options.Zoom, 1, 16);
        var pad = 6 * zoom;
        var gap = zoom;
        var cellWidth = (int)Math.Ceiling(sides.Max(s => s.Width * zoom * scale.X)) + 2 * pad;
        var cellHeight = (int)Math.Ceiling(sides.Max(s => s.Height * zoom * scale.Y)) + 2 * pad;
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
            using var cell = FloorCanvas.Compose(cellWidth, cellHeight, zoom, side, scale, floor: options.Background == ShareBackground.Floor);
            if (options.Background == ShareBackground.Plain)
            {
                using var fill = new SKPaint { Color = Plain };
                canvas.DrawRect(left, gap, cellWidth, cellHeight, fill);
            }
            canvas.DrawBitmap(cell, left, gap);
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

    public static byte[] Png(SKBitmap picture)
    {
        using var data = picture.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
