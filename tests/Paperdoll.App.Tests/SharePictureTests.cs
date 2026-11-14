using Paperdoll.App.Preview;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Tests;
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

    private static readonly SKColor Green = new(30, 200, 30);

    // A made-up room picture, all one colour, tiles wide and high at 32 pixels a tile, the character's square at the given tile.
    private static RoomPicture Room(int tilesWide, int tilesHigh, int standX, int standY)
    {
        using var below = new SKBitmap(tilesWide * 32, tilesHigh * 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        below.Erase(Green);
        using var above = new SKBitmap(tilesWide * 32, tilesHigh * 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        above.Erase(SKColors.Transparent);
        return new RoomPicture(SKImage.FromBitmap(below), SKImage.FromBitmap(above), standX * 32, standY * 32);
    }

    private static ShareOptions Bare(int tiles, ShareBackground background = ShareBackground.Room) =>
        new(AllSides: false, Zoom: 2, Background: background, Caption: false, Credit: false, RoomTiles: tiles);

    [Fact]
    public void Close_shows_the_room_in_a_panel_the_size_it_always_was()
    {
        var room = Room(9, 5, 2, 1);
        using var before = SharePicture.Compose([Sprite()], (1f, 1f), new ShareOptions(AllSides: false, Zoom: 2, Background: ShareBackground.Room, Caption: false, Credit: false), Text, room);
        using var close = SharePicture.Compose([Sprite()], (1f, 1f), Bare(0), Text, room);

        Assert.Equal(0, new ShareOptions().RoomTiles);
        Assert.Equal((88 + 2 * 2, 88 + 2 * 2), (before.Width, before.Height));
        Assert.Equal(before.Pixels, close.Pixels);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(7, 5)]
    public void A_number_of_tiles_sets_the_panel_width_and_a_height_to_match(int tiles, int tilesHigh)
    {
        using var picture = SharePicture.Compose([Sprite()], (1f, 1f), Bare(tiles), Text, Room(9, 5, 2, 1));

        // 64 pixels a tile at 2x, and the 2-pixel gaps around the panel.
        Assert.Equal((tiles * 64 + 2 * 2, tilesHigh * 64 + 2 * 2), (picture.Width, picture.Height));
        Assert.Equal(SKColors.White, picture.GetPixel(picture.Width / 2, picture.Height / 2));
    }

    [Fact]
    public void Other_backgrounds_ignore_how_much_room_is_shown()
    {
        var room = Room(9, 5, 2, 1);
        foreach (var background in new[] { ShareBackground.Floor, ShareBackground.Plain, ShareBackground.Transparent })
        {
            using var close = SharePicture.Compose([Sprite()], (1f, 1f), Bare(0, background), Text, room);
            using var wide = SharePicture.Compose([Sprite()], (1f, 1f), Bare(7, background), Text, room);
            Assert.Equal(SharePicture.Png(close), SharePicture.Png(wide));
        }
    }

    [Fact]
    public void Whole_room_shows_every_tile_and_no_more()
    {
        // 9 by 5 tiles, the character's square 2 east and 1 south of the top-left one.
        using var picture = SharePicture.Compose([Sprite()], (1f, 1f), Bare(ShareOptions.WholeRoom), Text, Room(9, 5, 2, 1));

        // The picture is the room alone, with no empty space added to keep the character in the middle.
        Assert.Equal((9 * 64 + 2 * 2, 5 * 64 + 2 * 2), (picture.Width, picture.Height));
        Assert.Equal(SKColors.White, picture.GetPixel(2 + 160, 2 + 96));
        Assert.Equal(Green, picture.GetPixel(2, 2));
        Assert.Equal(Green, picture.GetPixel(2 + 575, 2 + 319));
    }

    [Fact]
    public void A_tall_character_is_not_cut_off_by_a_narrow_view()
    {
        using var tall = new SKBitmap(32, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
        tall.Erase(SKColors.White);
        var room = Room(9, 5, 2, 1);
        using var close = SharePicture.Compose([tall], (1f, 1.5f), Bare(0), Text, room);
        using var three = SharePicture.Compose([tall], (1f, 1.5f), Bare(3), Text, room);

        // The doll is 288 pixels high at 2x and 1.5 times its height, more than 3 tiles.
        Assert.Equal((3 * 64 + 2 * 2, close.Height), (three.Width, three.Height));
        Assert.Equal(SKColors.White, three.GetPixel(three.Width / 2, 2 + 12 + 1));
    }

    [Fact]
    public void A_picture_that_would_pass_8192_pixels_is_drawn_smaller()
    {
        // A room 40 tiles wide is 10240 pixels at 8x.
        var room = Room(40, 2, 0, 0);
        var options = new ShareOptions(AllSides: false, Zoom: 8, Background: ShareBackground.Room, RoomTiles: ShareOptions.WholeRoom);
        using var picture = SharePicture.Compose([Sprite()], (1f, 1f), options, Text, room);

        Assert.InRange(picture.Width, 1, 8192);
        Assert.InRange(picture.Height, 1, 8192);
        Assert.Equal(6, SharePicture.Panel([Sprite()], (1f, 1f), options, room).Zoom);
        // The room, and the 6 game pixels kept west of the character, who stands at its west end.
        Assert.Equal((40 * 32 + 6) * 6 + 2 * 6, picture.Width);
    }

    [Fact]
    public void Settings_from_before_show_the_room_close()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<ShareOptions>("""{"AllSides":false,"Zoom":6,"Background":4,"Caption":true,"Credit":true}""")!;

        Assert.Equal(new ShareOptions(false, 6, ShareBackground.Room), old);
    }

    [Fact]
    public async Task The_credits_name_what_a_wider_view_of_the_room_draws()
    {
        // A room 7 tiles wide, the character in the middle and the crate at the west end, 3 tiles away.
        var store = TestForks.Store();
        await using var session = new EditorSession(store);
        await session.LoadForkAsync(TestRooms.Fork, update: false, ct: TestContext.Current.CancellationToken);
        var room = Core.Rooms.Room.TryParse("""
            id: test
            stand: "3,1"
            legend: { f: FloorTest }
            tiles:
            - "fffffff"
            - "fffffff"
            - "fffffff"
            entities:
            - { proto: TestCrate, pos: "0.5,1.5" }
            """, out var problem) ?? throw new InvalidDataException(problem);
        var scene = await session.RoomAsync(room, TestContext.Current.CancellationToken);
        var picture = scene.Paint(FloorCanvas.Plate);

        var close = SharePicture.RoomCredits([Sprite()], (1f, 1f), Bare(0), scene, picture).Select(c => c.Rsi).ToList();
        var seven = SharePicture.RoomCredits([Sprite()], (1f, 1f), Bare(7), scene, picture).Select(c => c.Rsi).ToList();

        Assert.Contains("Tiles/test.png", close);
        Assert.DoesNotContain("Structures/crate.rsi", close);
        Assert.Contains("Structures/crate.rsi", seven);
    }
}
