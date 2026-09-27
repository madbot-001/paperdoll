using Avalonia.Media.Imaging;
using Paperdoll.App.Preview;
using SkiaSharp;

namespace Paperdoll.App.Tests;

public class FloorCanvasTests
{
    [Fact]
    public async Task A_picture_reaches_the_window_pixel_for_pixel()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            using var picture = new SKBitmap(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            picture.Erase(SKColors.Transparent);
            picture.SetPixel(2, 1, new SKColor(255, 64, 0));

            using var shown = (WriteableBitmap)FloorCanvas.ToAvalonia(picture);
            using var pixels = shown.Lock();
            var bytes = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(pixels.Address + pixels.RowBytes + 2 * 4, bytes, 0, 4);

            Assert.Equal((3, 2), (shown.PixelSize.Width, shown.PixelSize.Height));
            Assert.Equal(new byte[] { 255, 64, 0, 255 }, bytes);
        }, TestContext.Current.CancellationToken);
    }
}
