using SkiaSharp;

namespace Paperdoll.App.Preview;

/// <summary>Pictures of items for lists, which draw most items small in the middle of their frame.</summary>
public static class ItemIcon
{
    /// <summary>
    /// The drawn part of the sprite, centred in a square of <paramref name="size"/> pixels and
    /// enlarged by whole steps so the pixels stay sharp (shrunk instead if it is too big).
    /// </summary>
    public static SKBitmap Fit(SKBitmap sprite, int size)
    {
        int left = sprite.Width, top = sprite.Height, right = -1, bottom = -1;
        for (var y = 0; y < sprite.Height; y++)
        {
            for (var x = 0; x < sprite.Width; x++)
            {
                if (sprite.GetPixel(x, y).Alpha == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        var icon = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (right < 0)
            return icon;
        var (width, height) = (right - left + 1, bottom - top + 1);
        var longest = Math.Max(width, height);
        var scale = longest <= size ? size / longest : size / (float)longest;
        var (w, h) = (width * scale, height * scale);

        using var canvas = new SKCanvas(icon);
        using var image = SKImage.FromBitmap(sprite);
        canvas.DrawImage(image, SKRect.Create(left, top, width, height), SKRect.Create((size - w) / 2f, (size - h) / 2f, w, h),
            new SKSamplingOptions(SKFilterMode.Nearest), null);
        return icon;
    }
}
