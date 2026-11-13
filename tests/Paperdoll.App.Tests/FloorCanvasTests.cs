using Avalonia.Media.Imaging;
using Paperdoll.App.Preview;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Tests;
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

    [Fact]
    public void A_doll_with_unshaded_layers_is_drawn_as_before_wherever_no_light_falls_on_it()
    {
        using var glowing = TestRooms.Wearing("GlowVest");
        using var plain = TestRooms.Wearing("PlainVest");
        using var floor = SKImage.FromBitmap(new SKBitmap(64, 64));
        var room = new RoomPicture(floor, floor, 16, 16);

        // A facing thumbnail, a portrait, and the demo room drawn without lighting.
        foreach (var (width, height, zoom, picture) in new[] { (72, 72, 2, (RoomPicture?)null), (36, 36, 1, null), (320, 200, 6, room) })
        {
            using var before = FloorCanvas.Compose(width, height, zoom, plain, room: picture);
            using var after = FloorCanvas.Compose(width, height, zoom, glowing, room: picture);
            Assert.Equal(before.Pixels, after.Pixels);
        }
    }
}
