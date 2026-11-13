using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Paperdoll.App.Preview;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Tests;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using SkiaSharp;
using static Paperdoll.App.Tests.TabTests;

namespace Paperdoll.App.Tests;

/// <summary>The preview standing the character in a demo room, on made-up fork "r" with the test room in place of the bar.</summary>
public class DemoRoomTests
{
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    // The Floor box's items: plain plates, then the demo room.
    internal const int PlainItem = 0, RoomItem = 1;

    private static readonly SKColor Plate = new(0x30, 0x36, 0x3D);
    private static readonly SKColor Space = new(0x0B, 0x0D, 0x10);

    // Lets requests run until the condition holds.
    internal static void WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done());
    }

    // A pixel of the main preview, counted from the top-left corner of the character's square,
    // which is 64 pixels a side at 2x.
    internal static SKColor Pixel(MainWindow window, int x, int y)
    {
        var bitmap = (WriteableBitmap)Find<Image>(window, "PreviewImage").Source!;
        var left = bitmap.PixelSize.Width / 2 - 32 + x;
        var top = bitmap.PixelSize.Height / 2 - 32 + y;
        using var pixels = bitmap.Lock();
        var bytes = new byte[4];
        Marshal.Copy(pixels.Address + top * pixels.RowBytes + left * 4, bytes, 0, 4);
        return new SKColor(bytes[0], bytes[1], bytes[2], bytes[3]);
    }

    internal static string Note(MainWindow window) =>
        Find<TextBlock>(window, "FloorNote") is { IsVisible: true } note ? note.Text ?? "" : "";

    internal static ShareWindow OpenShare(MainWindow window)
    {
        typeof(MainWindow).GetMethod("OnShare", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [null, new Avalonia.Interactivity.RoutedEventArgs()]);
        Dispatcher.UIThread.RunJobs();
        return window.OwnedWindows.OfType<ShareWindow>().Single();
    }

    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);

    // The room's pictures as last drawn.
    private static RoomPicture? PictureShown(MainWindow window) =>
        Field(window, "_room") is { } room ? (RoomPicture?)room.GetType().GetField("Item3")!.GetValue(room) : null;

    // A user rooms folder of the window's own, holding bar.yml with this text; its path.
    private static string UseRooms(MainWindow window, string bar)
    {
        var folder = Path.Combine(TestData.Directory, "rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "bar.yml"), bar);
        typeof(MainWindow).GetField("_roomsFolder", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, folder);
        return folder;
    }

    // The test room in place of the bar, and the demo room chosen at 2x, where the preview shows the whole room.
    private static void ChooseRoom(MainWindow window)
    {
        UseRooms(window, TestRooms.Room);
        Find<ComboBox>(window, "ZoomBox").SelectedItem = "2x";
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        Dispatcher.UIThread.RunJobs();
    }

    // The character's square at 2x is 64 pixels a side; the doll fills it with red.
    private static void WaitForRoom(MainWindow window) => WaitFor(() => Pixel(window, 4, -60) == TestRooms.Blue);

    private static List<string> Messages(MainWindow window)
    {
        window.ShowTab(EditorTab.Credits);
        window.ShowTab(EditorTab.Messages);
        return Find<ListBox>(window, "MessagesList").ItemsSource!.Cast<string>().ToList();
    }

    [Fact]
    public Task The_room_is_drawn_around_the_character_with_what_stands_in_front_over_its_feet() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        WaitForRoom(window);

        // The doll, and over its lower half the railing that stands south of it.
        Assert.Equal(TestRooms.Red, Pixel(window, 4, 8));
        Assert.Equal(TestRooms.Yellow, Pixel(window, 4, 40));
        Assert.Equal(TestRooms.Yellow, Pixel(window, 4, 64 + 16));
        Assert.Equal(TestRooms.Blue, Pixel(window, 4, 64 + 40));
        // West, the crate; east, the decal tinted orange; south-east, a tile with no picture;
        // past the room, space.
        Assert.Equal(TestRooms.Green, Pixel(window, -64 + 8, 56));
        Assert.Equal(new SKColor(255, 128, 0), Pixel(window, 96, 32));
        Assert.Equal(Plate, Pixel(window, 96, 96));
        Assert.Equal(Space, Pixel(window, -100, 4));
        // No light shines in it, so it is drawn evenly lit, as the note says.
        Assert.Equal("No light shines in the demo room; drawn without lighting.", Note(window));
        Find<CheckBox>(window, "LightingBox").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", Note(window));
        Assert.Equal(TestRooms.Yellow, Pixel(window, 4, 40));
        return Task.CompletedTask;
    });

    // The lit test room in place of the bar, chosen at 2x; the doll stands a tile east of the lamp.
    private static void ChooseLitRoom(MainWindow window)
    {
        UseRooms(window, TestRooms.LitRoom);
        Find<ComboBox>(window, "ZoomBox").SelectedItem = "2x";
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        // Until it comes, the doll stands on plain plates in its own red.
        WaitFor(() => Pixel(window, 4, 8) is { Red: > 0 } doll && doll != TestRooms.Red);
    }

    // The room's lit pictures as last drawn.
    private static RoomPicture? LitShown(MainWindow window) =>
        Field(window, "_room") is { } room ? (RoomPicture?)room.GetType().GetField("Item4")!.GetValue(room) : null;

    [Fact]
    public Task The_lamp_lights_the_room_and_the_character_and_unshaded_layers_keep_their_colour() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseLitRoom(window);
        Assert.Equal("", Note(window));

        // The floor a tile from the lamp, and three tiles from it: darker further away.
        var near = Pixel(window, -32, -32);
        var far = Pixel(window, 136, -32);
        Assert.True(near.Blue > far.Blue && far.Blue > 0 && near.Blue < TestRooms.Blue.Blue, $"near {near}, far {far}");
        // The crate's glow is unshaded: white in the dark. The doll is dimmed.
        Assert.Equal(TestRooms.White, Pixel(window, 130, 2));
        Assert.InRange(Pixel(window, 4, 8).Red, 60, 160);

        // Without lighting, the room and the doll in their own colours, and the box saved.
        Find<CheckBox>(window, "LightingBox").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TestRooms.Red, Pixel(window, 4, 8));
        Assert.Equal(TestRooms.Blue, Pixel(window, -32, -32));
        Assert.Equal(TestRooms.Blue, Pixel(window, 136, -32));
        Assert.Equal(TestRooms.White, Pixel(window, 130, 2));
        Assert.Equal("", Note(window));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Animation_never_works_the_light_out_again() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseLitRoom(window);
        var scene = Field(window, "_room")!.GetType().GetField("Item2")!.GetValue(Field(window, "_room")) as RoomScene;
        var light = scene!.Light;
        var lit = LitShown(window);

        var draw = typeof(MainWindow).GetMethod("DrawPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 3; i++)
            draw.Invoke(window, []);

        Assert.NotNull(light);
        Assert.Same(light, scene.Light);
        Assert.Same(lit, LitShown(window));
        Assert.Same(light, lit!.Light);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_picture_to_share_is_lit_as_the_preview_is() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseLitRoom(window);
        int RedPixels()
        {
            var share = OpenShare(window);
            share.FindControl<RadioButton>("RoomButton")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            var picture = (SKBitmap)typeof(ShareWindow).GetField("_picture", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(share)!;
            var count = 0;
            for (var y = 0; y < picture.Height; y++)
            for (var x = 0; x < picture.Width; x++)
                count += picture.GetPixel(x, y) == TestRooms.Red ? 1 : 0;
            share.Close();
            return count;
        }

        // Lit, the doll is never its full red; without lighting it is.
        Assert.Equal(0, RedPixels());
        Find<CheckBox>(window, "LightingBox").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(RedPixels() > 100);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Animation_redraws_the_character_and_not_the_room() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        WaitForRoom(window);
        var drawn = PictureShown(window);

        // The doll plays two frames, so the preview redraws on a timer.
        Assert.True(((DispatcherTimer)Field(window, "_animation")!).IsEnabled);
        var draw = typeof(MainWindow).GetMethod("DrawPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 3; i++)
            draw.Invoke(window, []);

        Assert.NotNull(drawn);
        Assert.Same(drawn, PictureShown(window));
        Assert.Equal(TestRooms.Yellow, Pixel(window, 4, 40));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_room_that_arrives_after_another_floor_was_chosen_is_not_drawn() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        var hold = new TaskCompletionSource();
        store.FetchHold = hold;
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        // Still downloading when plain plates are chosen.
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Dispatcher.UIThread.RunJobs();
        hold.SetResult();
        WaitFor(() => store.Fetches.Count == 1 && Field(window, "_roomCancel") == null);

        Assert.Null(Field(window, "_room"));
        Assert.Equal(Plate, Pixel(window, 8, -56));
        Assert.Equal("", Note(window));

        // Chosen again, the room the session kept is drawn without another download.
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        WaitForRoom(window);
        Assert.Single(store.Fetches);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_failed_download_keeps_the_plates_and_gives_the_reason_in_Messages() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        ((MemoryForkStore)editor.Store).FetchFails = new HttpRequestException("offline");
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        WaitFor(() => Note(window).StartsWith("Could not show", StringComparison.Ordinal));

        Assert.Equal("Could not show the demo room; plain plates instead. See Messages.", Note(window));
        Assert.Equal(Plate, Pixel(window, 8, -56));
        Assert.Contains("Floor: offline The preview shows plain plates instead.", Messages(window));
        Assert.Equal("Could not show the demo room: offline", Find<TextBlock>(window, "StatusText").Text);

        // Back to plain plates, the message goes.
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Messages(window), m => m.StartsWith("Floor:", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_download_that_times_out_fails_once_rather_than_asking_again() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        store.FetchFails = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        WaitFor(() => Note(window).StartsWith("Could not show", StringComparison.Ordinal));

        Assert.Contains(Messages(window), m => m.StartsWith("Floor: The download stopped", StringComparison.Ordinal));
        store.FetchFails = null;
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.Empty(store.Fetches);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_credits_list_each_sprite_the_room_draws() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        ChooseRoom(window);
        WaitForRoom(window);
        window.ShowTab(EditorTab.Credits);

        var rows = Find<DataGrid>(window, "CreditsGrid").ItemsSource!.Cast<CreditRow>().ToList();
        Assert.Contains(new CreditRow("Structures/crate.rsi", "CC-BY-SA-3.0", "Crate made up"), rows);
        Assert.Contains(new CreditRow("Structures/railing.rsi", "CC-BY-SA-3.0", "Railing made up"), rows);
        Assert.Contains(new CreditRow("Structures/sticker.png", "CC0-1.0", "Sticker made up"), rows);
        Assert.Contains(new CreditRow("Decals/paint.png", "unknown", "no credit listed"), rows);
        Assert.Contains(new CreditRow("Tiles/test.png", "CC0-1.0", "Tile made up"), rows);
        // The doll itself, as before.
        Assert.Contains(new CreditRow("Mobs/doll.rsi", "CC0-1.0", "Doll made up"), rows);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_user_room_file_that_cannot_be_read_is_in_Messages_while_the_room_shows() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        UseRooms(window, "id: bar\n");
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Messages(window), m => m.StartsWith("Demo room: ", StringComparison.Ordinal) && m.Contains("bar.yml could not be read: it has no legend", StringComparison.Ordinal));
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Messages(window), m => m.StartsWith("Demo room: ", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_room_file_fixed_since_is_read_again_when_the_demo_room_is_chosen_again() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        var folder = UseRooms(window, "id: bar\n");
        Find<ComboBox>(window, "ZoomBox").SelectedItem = "2x";
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Messages(window), m => m.StartsWith("Demo room: ", StringComparison.Ordinal));

        // Fixed, then the demo room chosen again: the message goes and the room shows.
        File.WriteAllText(Path.Combine(folder, "bar.yml"), TestRooms.Room);
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        WaitForRoom(window);
        Assert.DoesNotContain(Messages(window), m => m.StartsWith("Demo room: ", StringComparison.Ordinal));
        var first = PictureShown(window);

        // Changed again, the room is drawn again; chosen again unchanged, its pictures are kept.
        File.WriteAllText(Path.Combine(folder, "bar.yml"), TestRooms.Room.Replace("decals:", "decals: []\nunused:", StringComparison.Ordinal));
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        WaitFor(() => PictureShown(window) != first && Pixel(window, 96, 32) == TestRooms.Blue);
        var second = PictureShown(window);
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.PlainItem;
        Find<ComboBox>(window, "FloorBox").SelectedIndex = DemoRoomTests.RoomItem;
        WaitForRoom(window);
        Assert.Same(second, PictureShown(window));
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_picture_window_offers_the_demo_room_only_while_one_is_shown() => OnWindow(TestRooms.Fork, (window, editor) =>
    {
        editor.PreviewJob = "Clerk";
        var share = OpenShare(window);
        Assert.False(share.FindControl<RadioButton>("RoomButton")!.IsVisible);
        share.Close();

        ChooseRoom(window);
        WaitForRoom(window);
        share = OpenShare(window);
        var button = share.FindControl<RadioButton>("RoomButton")!;
        Assert.True(button.IsVisible);
        button.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var credits = (string)typeof(ShareWindow).GetField("_fullCredits", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(share)!;
        Assert.Contains("The Test bar demo room around the character:", credits);
        Assert.Contains("- Structures/railing.rsi: CC-BY-SA-3.0; Railing made up", credits);
        Assert.Contains("- Tiles/test.png: CC0-1.0; Tile made up", credits);
        share.Close();
        return Task.CompletedTask;
    });
}

/// <summary>
/// Run alone: it points Paperdoll's own folder elsewhere for a while, which other tests reading
/// the run's folder must not see.
/// </summary>
[CollectionDefinition(nameof(OwnFolder), DisableParallelization = true)]
public sealed class OwnFolder;

/// <summary>The demo room as the floor kept between runs, on made-up fork "r".</summary>
[Collection(nameof(OwnFolder))]
public class DemoRoomSettingsTests
{
    [Fact]
    public async Task The_demo_room_is_kept_for_the_next_start_and_older_settings_still_load()
    {
        var data = Path.Combine(TestData.Directory, "room-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        static void LoadSettings(MainWindow window) =>
            typeof(MainWindow).GetMethod("LoadSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, []);
        static ComboBox Floor(MainWindow window) => window.FindControl<ComboBox>("FloorBox")!;
        try
        {
            await OnWindow(TestRooms.Fork, (window, _) =>
            {
                LoadSettings(window);
                Floor(window).SelectedIndex = DemoRoomTests.RoomItem;
                Dispatcher.UIThread.RunJobs();
                return Task.CompletedTask;
            });
            var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(data, "settings.json")))!;
            Assert.Equal(FloorMode.DemoRoom, saved.Floor);
            Assert.True(saved.Lighting);

            await OnWindow(TestRooms.Fork, (window, _) =>
            {
                LoadSettings(window);
                Assert.Equal(DemoRoomTests.RoomItem, Floor(window).SelectedIndex);
                                // Lighting is offered with the demo room only, and kept.
                var lighting = window.FindControl<CheckBox>("LightingBox")!;
                Assert.True(lighting is { IsEnabled: true, IsChecked: true });
                lighting.IsChecked = false;
                Floor(window).SelectedIndex = DemoRoomTests.PlainItem;
                Dispatcher.UIThread.RunJobs();
                Assert.False(lighting.IsEnabled);
                Floor(window).SelectedIndex = DemoRoomTests.RoomItem;
                Dispatcher.UIThread.RunJobs();
                return Task.CompletedTask;
            });
            Assert.False(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(data, "settings.json")))!.Lighting);
            await OnWindow(TestRooms.Fork, (window, _) =>
            {
                LoadSettings(window);
                Assert.True(window.FindControl<CheckBox>("LightingBox")! is { IsEnabled: true, IsChecked: false });
                return Task.CompletedTask;
            });

            // Settings written before lighting, with a floor no longer offered (1) and its
            // stations: plain plates, and lighting on, offered only with the demo room.
            File.WriteAllText(Path.Combine(data, "settings.json"), """{"Autosave":true,"Floor":1,"Stations":{"r":"Test"}}""");
            await OnWindow(TestRooms.Fork, (window, _) =>
            {
                LoadSettings(window);
                Assert.Equal(DemoRoomTests.PlainItem, Floor(window).SelectedIndex);
                Assert.True(window.FindControl<CheckBox>("LightingBox")! is { IsEnabled: false, IsChecked: true });
                Assert.Equal("Plain", ((ComboBoxItem)Floor(window).SelectedItem!).Content);
                return Task.CompletedTask;
            });
            // The demo room written as 2 before that floor went is still the demo room.
            File.WriteAllText(Path.Combine(data, "settings.json"), """{"Autosave":true,"Floor":2,"Stations":{"r":"Test"}}""");
            await OnWindow(TestRooms.Fork, (window, _) =>
            {
                LoadSettings(window);
                Assert.Equal("Demo room", ((ComboBoxItem)Floor(window).SelectedItem!).Content);
                Assert.True(window.FindControl<CheckBox>("LightingBox")!.IsEnabled);
                return Task.CompletedTask;
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
            if (Directory.Exists(data))
                Directory.Delete(data, true);
        }
    }
}
