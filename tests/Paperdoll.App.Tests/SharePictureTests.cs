using Paperdoll.App.Preview;
using SkiaSharp;

namespace Paperdoll.App.Tests;

public class SharePictureTests
{
    // A 32-pixel frame with a 2 by 2 white square in the middle.
    private static SKBitmap Sprite()
    {
        var sprite = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 15; y < 17; y++)
        for (var x = 15; x < 17; x++)
            sprite.SetPixel(x, y, SKColors.White);
        return sprite;
    }

    private static readonly ShareText Text = new("Test Person", "Human", "Sprites from Test for Space Station 14, CC-BY-SA-3.0");

    [Fact]
    public void Each_side_has_its_own_panel_and_the_words_go_below()
    {
        var sides = Enumerable.Range(0, 4).Select(_ => Sprite()).ToList();

        using var bare = SharePicture.Compose(sides, (1f, 1f), new ShareOptions(Zoom: 2, Caption: false, Credit: false), Text);
        using var words = SharePicture.Compose(sides, (1f, 1f), new ShareOptions(Zoom: 2), Text);

        // A panel is the 64-pixel sprite and 12 pixels around it; 2-pixel gaps between and around panels.
        Assert.Equal((4 * 88 + 5 * 2, 88 + 2 * 2), (bare.Width, bare.Height));
        Assert.True(words.Height > bare.Height);
        Assert.Equal(SKColors.White, bare.GetPixel(2 + 44, 2 + 44));
    }

    [Fact]
    public void A_transparent_picture_is_clear_around_the_character()
    {
        using var picture = SharePicture.Compose([Sprite()], (1f, 1f), new ShareOptions(Zoom: 2, Background: ShareBackground.Transparent, Caption: false, Credit: false), Text);
        using var floor = SharePicture.Compose([Sprite()], (1f, 1f), new ShareOptions(Zoom: 2, Caption: false, Credit: false), Text);

        Assert.Equal(0, picture.GetPixel(5, 5).Alpha);
        Assert.Equal(255, picture.GetPixel(2 + 44, 2 + 44).Alpha);
        Assert.Equal(255, floor.GetPixel(5, 5).Alpha);
    }
}
