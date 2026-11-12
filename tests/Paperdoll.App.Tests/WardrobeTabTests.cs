using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperdoll.App.Controls;
using Paperdoll.Core.Tests;
using static Paperdoll.App.EditorTab;
using static Paperdoll.App.Tests.TabTests;

namespace Paperdoll.App.Tests;

/// <summary>Trying clothing on in the Wardrobe tab, on made-up fork "w".</summary>
public class WardrobeTabTests
{
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    private static void PressWardrobe(MainWindow window)
    {
        Find<Button>(window, "WardrobeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    // A window with dress-up switched on from the toolbar, where the fork has clothes to wear.
    private static Task OnWindow(Core.Forks.ForkInfo fork, Func<MainWindow, Core.Editing.EditorSession, Task> test) =>
        TabTests.OnWindow(fork, (window, editor) =>
        {
            if (Find<Button>(window, "WardrobeButton").IsEnabled)
                PressWardrobe(window);
            return test(window, editor);
        });

    private static TreeView Explorer(MainWindow window) => Find<TreeView>(window, "Explorer");

    private static string HeaderText(TreeViewItem item) =>
        string.Join(" ", ((Panel)item.Header!).Children.OfType<TextBlock>().Select(t => t.Text));

    private static List<string> SideRows(MainWindow window) => Explorer(window).Items.OfType<TreeViewItem>().Select(HeaderText).ToList();

    // Opens the Wardrobe tab on a slot, as clicking its row in the side list does. Downloads start
    // without the pause the window leaves for arrowing down the list.
    private static void OpenSlot(MainWindow window, string name)
    {
        SetPause(window, _ => Task.CompletedTask);
        window.ShowTab(Wardrobe);
        Explorer(window).SelectedItem = Explorer(window).Items.OfType<TreeViewItem>().First(i => HeaderText(i).StartsWith(name + " ", StringComparison.Ordinal));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetPause(MainWindow window, Func<CancellationToken, Task> pause) =>
        typeof(MainWindow).GetField("_downloadPause", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, pause);

    // Lets downloads released by the test run to their end.
    private static void Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static bool NoteShown(MainWindow window, string start) =>
        InPage<TextBlock>(window).Any(t => t.IsVisible && t.Text?.StartsWith(start, StringComparison.Ordinal) == true);

    private static DataGrid Grid(MainWindow window) => Find<DataGrid>(window, "WardrobeGrid");

    private static List<WardrobeRow> Rows(MainWindow window) => Grid(window).ItemsSource!.Cast<WardrobeRow>().ToList();

    private static void Pick(MainWindow window, string name)
    {
        Grid(window).SelectedItem = Rows(window).First(r => r.Name == name);
        Dispatcher.UIThread.RunJobs();
    }

    private static object? TabTitle(MainWindow window) =>
        Find<TabStripPanel>(window, "TabStrip").Children.OfType<RadioButton>().First(b => (EditorTab)b.Tag! == Wardrobe).Content;

    [Fact]
    public async Task The_wardrobe_takes_the_place_of_every_tab_only_while_it_is_on()
    {
        await TabTests.OnWindow(TestForks.W, (window, editor) =>
        {
            // Off at first: the usual tabs, none of them the Wardrobe.
            Assert.False(editor.DressUpShown);
            Assert.DoesNotContain(Wardrobe, window.TabsShown);
            Assert.Contains(Loadout, window.TabsShown);
            window.ShowTab(Loadout);

            PressWardrobe(window);
            Assert.Equal([Wardrobe, Credits, Messages, Source], window.TabsShown);
            Assert.Equal(Wardrobe, window.ShownTab);
            Assert.True(EditorTabs.HasSideList(Wardrobe));

            // Off again: the tabs are back, on the one that was open.
            PressWardrobe(window);
            Assert.DoesNotContain(Wardrobe, window.TabsShown);
            Assert.Equal(Loadout, window.ShownTab);
            return Task.CompletedTask;
        });
        await OnWindow(TestForks.A, (window, _) =>
        {
            Assert.DoesNotContain(Loadout, window.TabsShown);
            Assert.DoesNotContain(Wardrobe, window.TabsShown);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task The_side_list_has_a_row_per_slot_with_what_it_holds() => OnWindow(TestForks.W, (window, _) =>
    {
        window.ShowTab(Wardrobe);

        Assert.Equal(
            ["Head from the job: cap", "Neck from the job: nothing", "Jumpsuit from the job: clerk uniform", "Back from the job: nothing"],
            SideRows(window));
        Assert.Equal("Wardrobe: Head", Find<TextBlock>(window, "InspectorTitle").Text);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text?.StartsWith("Dress-up is for the preview and pictures only.") == true);
        Assert.Equal(["From the job (cap)", "Nothing", "cap", "long scarf", "paper hat"], Rows(window).Select(r => r.Name));
        Assert.Equal("From the job (cap)", ((WardrobeRow)Grid(window).SelectedItem!).Name);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_toolbar_button_turns_dress_up_off_and_on_keeping_what_was_tried_on() => OnWindow(TestForks.W, (window, editor) =>
    {
        var button = Find<Button>(window, "WardrobeButton");
        var file = editor.File!.ToYaml();
        OpenSlot(window, "Neck");
        Pick(window, "long scarf");
        Assert.True(button.IsEnabled);
        Assert.Contains("on", button.Classes);

        // Off: the job's outfit again, the scarf kept for later.
        PressWardrobe(window);
        Assert.DoesNotContain("on", button.Classes);
        Assert.NotEqual(Wardrobe, window.ShownTab);
        Assert.False(editor.Outfit()!.ContainsKey("neck"));
        Assert.True(editor.HasDressUp);

        // On: the Wardrobe is open again, wearing the scarf.
        PressWardrobe(window);
        Assert.Contains("on", button.Classes);
        Assert.Equal(Wardrobe, window.ShownTab);
        Assert.Equal("ClothingNeckScarf", editor.Outfit()!["neck"]);
        Assert.Contains("Neck long scarf", SideRows(window));
        Assert.DoesNotContain("*", window.Title);
        Assert.Equal(file, editor.File!.ToYaml());
        return Task.CompletedTask;
    });

    [Fact]
    public Task Picking_an_item_keeps_the_list_where_it_was() => OnWindow(TestForks.W, (window, _) =>
    {
        OpenSlot(window, "Head");
        var shown = Grid(window).ItemsSource;

        Pick(window, "long scarf");

        // The same list, with only the changed rows swapped, so it does not go back to its top.
        Assert.Same(shown, Grid(window).ItemsSource);
        Assert.Equal("Yes", Rows(window).Single(r => r.Name == "long scarf").Worn);
        Assert.Equal("", Rows(window)[0].Worn);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_toolbar_button_is_off_limits_where_nothing_is_worn() => OnWindow(TestForks.A, (window, _) =>
    {
        Assert.False(Find<Button>(window, "WardrobeButton").IsEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Picking_an_item_puts_it_on_and_marks_the_tab_until_reset() => OnWindow(TestForks.W, (window, editor) =>
    {
        var file = editor.File!.ToYaml();
        OpenSlot(window, "Neck");
        Assert.Equal("Wardrobe", TabTitle(window));

        Pick(window, "long scarf");

        Assert.Equal("ClothingNeckScarf", editor.Outfit()!["neck"]);
        Assert.Contains("Neck long scarf", SideRows(window));
        Assert.Equal("Wardrobe *", TabTitle(window));
        Assert.Equal("Yes", Rows(window).Single(r => r.Name == "long scarf").Worn);
        // Not a change to the character.
        Assert.DoesNotContain("*", window.Title);
        Assert.Equal(file, editor.File!.ToYaml());

        var reset = InPage<Button>(window).Single(b => (string?)b.Content == "Reset to the job's outfit");
        Assert.True(reset.IsEnabled);
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(editor.HasDressUp);
        Assert.False(editor.Outfit()!.ContainsKey("neck"));
        Assert.Equal("Wardrobe", TabTitle(window));
        Assert.False(InPage<Button>(window).Single(b => (string?)b.Content == "Reset to the job's outfit").IsEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Nothing_takes_the_job_item_off() => OnWindow(TestForks.W, (window, editor) =>
    {
        OpenSlot(window, "Head");

        Pick(window, "Nothing");

        Assert.False(editor.Outfit()!.ContainsKey("head"));
        Assert.Equal("Head nothing", SideRows(window)[0]);

        Pick(window, "From the job (cap)");
        Assert.Equal("ClothingHeadCap", editor.Outfit()!["head"]);
        Assert.False(editor.HasDressUp);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_search_box_filters_the_list_and_keeps_focus() => OnWindow(TestForks.W, (window, _) =>
    {
        OpenSlot(window, "Head");
        var search = Find<TextBox>(window, "WardrobeFilter");

        Type(search, "SCARF");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["From the job (cap)", "Nothing", "long scarf"], Rows(window).Select(r => r.Name));
        Assert.True(search.IsFocused);

        // By id too.
        search.Text = "HeadPaper";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["From the job (cap)", "Nothing", "paper hat"], Rows(window).Select(r => r.Name));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_hat_with_no_picture_for_the_slot_says_so() => OnWindow(TestForks.W, (window, editor) =>
    {
        OpenSlot(window, "Head");

        Pick(window, "paper hat");

        Assert.Equal("ClothingHeadPaper", editor.Outfit()!["head"]);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "The paper hat has no picture for this slot on this body, so nothing is drawn.");
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_failed_download_leaves_the_slot_and_says_so() => OnWindow(TestForks.W, (window, editor) =>
    {
        ((MemoryForkStore)editor.Store).FetchFails = new HttpRequestException("No network.");
        OpenSlot(window, "Back");

        Pick(window, "tall backpack");

        Assert.False(editor.Outfit()!.ContainsKey("back"));
        Assert.False(editor.HasDressUp);
        Assert.Equal("Wardrobe", TabTitle(window));
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "The pictures of the tall backpack could not be downloaded, so the slot is as it was. No network." && t.IsVisible);
        Assert.Equal("From the job (nothing)", ((WardrobeRow)Grid(window).SelectedItem!).Name);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Credits_list_the_sprites_of_clothing_tried_on() => OnWindow(TestForks.W, (window, _) =>
    {
        OpenSlot(window, "Back");
        Pick(window, "tall backpack");

        window.ShowTab(Credits);

        Assert.Contains(Find<DataGrid>(window, "CreditsGrid").ItemsSource!.Cast<CreditRow>(), r => r.Rsi == "Clothing/Back/tall.rsi");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Downloads_in_different_slots_each_finish() => OnWindow(TestForks.W, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        var hold = store.FetchHold = new TaskCompletionSource();
        OpenSlot(window, "Head");
        Pick(window, "paper hat");
        Assert.True(NoteShown(window, "Downloading the pictures of the paper hat"));

        OpenSlot(window, "Neck");
        Pick(window, "long scarf");
        hold.SetResult();
        Settle();

        Assert.Equal("ClothingHeadPaper", editor.Outfit()!["head"]);
        Assert.Equal("ClothingNeckScarf", editor.Outfit()!["neck"]);
        Assert.False(NoteShown(window, "Downloading"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Picking_what_the_slot_wears_during_a_download_keeps_it() => OnWindow(TestForks.W, (window, editor) =>
    {
        var hold = ((MemoryForkStore)editor.Store).FetchHold = new TaskCompletionSource();
        OpenSlot(window, "Head");
        Pick(window, "paper hat");

        Pick(window, "From the job (cap)");
        hold.SetResult();
        Settle();

        Assert.Equal("ClothingHeadCap", editor.Outfit()!["head"]);
        Assert.False(editor.HasDressUp);
        Assert.False(NoteShown(window, "Downloading"));
        Assert.Equal("From the job (cap)", ((WardrobeRow)Grid(window).SelectedItem!).Name);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Arrowing_down_the_list_downloads_only_the_row_it_stops_on() => OnWindow(TestForks.W, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        OpenSlot(window, "Head");
        var pause = new TaskCompletionSource();
        SetPause(window, ct => pause.Task.WaitAsync(ct));

        Pick(window, "long scarf");
        Pick(window, "paper hat");
        Assert.Empty(store.Fetches);
        pause.SetResult();
        Settle();

        Assert.Equal(["Resources/Textures/Clothing/Head/paper.rsi/meta.json"], Assert.Single(store.Fetches));
        Assert.Equal("ClothingHeadPaper", editor.Outfit()!["head"]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_message_about_a_pick_goes_when_dress_up_is_cleared() => OnWindow(TestForks.W, (window, editor) =>
    {
        ((MemoryForkStore)editor.Store).FetchFails = new HttpRequestException("No network.");
        OpenSlot(window, "Back");
        Pick(window, "tall backpack");
        Assert.True(NoteShown(window, "The pictures of the tall backpack could not be downloaded"));

        Apply(window, s =>
        {
            s.NewCharacter("Human");
            return s.LastFixes;
        });
        OpenSlot(window, "Back");

        Assert.False(NoteShown(window, "The pictures of the tall backpack"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_pick_waiting_to_download_goes_with_a_random_character() => OnWindow(TestForks.W, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        OpenSlot(window, "Head");
        var pause = new TaskCompletionSource();
        SetPause(window, ct => pause.Task.WaitAsync(ct));
        Pick(window, "paper hat");

        Apply(window, s => s.Randomize(Core.Characters.RandomParts.Name, new Random(1)));
        pause.SetResult();
        Settle();

        Assert.False(editor.HasDressUp);
        Assert.Equal("ClothingHeadCap", editor.Outfit()!["head"]);
        Assert.Empty(store.Fetches);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Searching_while_an_item_downloads_does_not_stop_it() => OnWindow(TestForks.W, (window, editor) =>
    {
        var hold = ((MemoryForkStore)editor.Store).FetchHold = new TaskCompletionSource();
        OpenSlot(window, "Head");
        Pick(window, "paper hat");

        Type(Find<TextBox>(window, "WardrobeFilter"), "cap");
        Settle();
        hold.SetResult();
        Settle();

        Assert.Equal("ClothingHeadPaper", editor.Outfit()!["head"]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_download_that_fails_after_dress_up_was_cleared_says_nothing() => OnWindow(TestForks.W, (window, editor) =>
    {
        var store = (MemoryForkStore)editor.Store;
        var hold = store.FetchHold = new TaskCompletionSource();
        store.FetchFails = new HttpRequestException("No network.");
        OpenSlot(window, "Back");
        Pick(window, "tall backpack");

        Apply(window, s => s.Randomize(Core.Characters.RandomParts.Name, new Random(1)));
        hold.SetResult();
        Settle();
        OpenSlot(window, "Back");

        Assert.False(NoteShown(window, "The pictures of the tall backpack"));
        Assert.DoesNotContain("Could not download", Find<TextBlock>(window, "StatusText").Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_download_stopped_by_other_store_work_is_not_a_failure() => OnWindow(TestForks.W, (window, editor) =>
    {
        var hold = ((MemoryForkStore)editor.Store).FetchHold = new TaskCompletionSource();
        OpenSlot(window, "Head");
        Pick(window, "paper hat");

        var cleanUp = editor.CleanUpStoreAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(cleanUp.IsCompleted);
        hold.SetResult();
        Settle();

        Assert.True(cleanUp.IsCompletedSuccessfully);
        Assert.Equal("ClothingHeadCap", editor.Outfit()!["head"]);
        Assert.False(NoteShown(window, "The pictures of the paper hat"));
        Assert.False(NoteShown(window, "Downloading"));
        Assert.Equal("Stopped downloading the pictures of the paper hat.", Find<TextBlock>(window, "StatusText").Text);
        return Task.CompletedTask;
    });
}
