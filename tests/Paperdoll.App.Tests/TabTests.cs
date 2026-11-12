using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperdoll.App.Controls;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Tests;
using static Paperdoll.App.EditorTab;

namespace Paperdoll.App.Tests;

public class TabTests
{
    [Fact]
    public async Task A_plain_fork_shows_the_base_tabs_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        editor.NewCharacter("Human");

        Assert.Equal([Appearance, Jobs, Markings, Description, Credits, Messages, Source], EditorTabs.Visible(editor));
    }

    [Fact]
    public async Task A_fork_with_everything_shows_every_tab_in_lobby_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.C, update: false, ct: ct);
        editor.NewCharacter("Human");

        // Cybernetics and the character info tabs are Starlight's, which has no Description tab;
        // the Wardrobe shows only while dress-up is on.
        Assert.Equal(Enum.GetValues<EditorTab>().Except([Cybernetics, IcInfo, OocInfo, Wardrobe]), EditorTabs.Visible(editor));
    }

    [Fact]
    public void Tabs_with_many_items_have_a_side_list()
    {
        var all = Enum.GetValues<EditorTab>();

        Assert.Equal([Traits, Markings, Loadout, Wardrobe, Records], all.Where(EditorTabs.HasSideList));
        Assert.Equal([Credits, Messages, Source], all.Where(EditorTabs.IsUtility));
    }

    private static TabStripPanel Strip()
    {
        var panel = new TabStripPanel();
        for (var i = 0; i < 5; i++)
        {
            var tab = new Border { Width = 80, Height = 20 };
            TabStripPanel.SetIsUtility(tab, i >= 3);
            panel.Children.Add(tab);
        }
        return panel;
    }

    [Fact]
    public async Task The_strip_puts_utility_tabs_at_the_far_right_when_one_row_fits()
    {
        var ct = TestContext.Current.CancellationToken;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var panel = Strip();
            panel.Measure(new Size(600, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 600, panel.DesiredSize.Height));

            Assert.Equal(20, panel.DesiredSize.Height);
            Assert.Equal([0.0, 80, 160, 440, 520], panel.Children.Select(c => c.Bounds.X));
            Assert.All(panel.Children, c => Assert.Equal(0, c.Bounds.Y));
        }, ct);
    }

    [Fact]
    public async Task The_strip_wraps_and_lets_utility_tabs_flow_when_it_is_too_narrow()
    {
        var ct = TestContext.Current.CancellationToken;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var panel = Strip();
            panel.Measure(new Size(250, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 250, panel.DesiredSize.Height));

            Assert.Equal(40, panel.DesiredSize.Height);
            Assert.Equal(
                [new Point(0, 0), new Point(80, 0), new Point(160, 0), new Point(0, 20), new Point(80, 20)],
                panel.Children.Select(c => c.Bounds.Position));
        }, ct);
    }

    // Opens the window on a new character on the fork, as a user would see it, and runs the test in it.
    internal static async Task OnWindow(ForkInfo fork, Func<MainWindow, EditorSession, Task> test, double width = 1100, double height = 720)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(fork, update: false, ct: ct);
        editor.NewCharacter("Human");

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var window = new MainWindow(editor) { Width = width, Height = height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                await test(window, editor);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, ct);
    }

    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    // The tab the strip marks as open.
    private static object? Checked(MainWindow window) =>
        Find<TabStripPanel>(window, "TabStrip").Children.OfType<RadioButton>().Single(b => b.IsChecked == true).Tag;

    [Fact]
    public Task The_window_opens_on_Appearance_with_the_forks_tabs() => OnWindow(TestForks.C, (window, editor) =>
    {
        Assert.Equal(Appearance, window.ShownTab);
        Assert.Equal(EditorTabs.Visible(editor), window.TabsShown);
        Assert.Equal(window.TabsShown.Cast<object>(), Find<TabStripPanel>(window, "TabStrip").Children.Select(c => c.Tag));
        Assert.Equal(Appearance, Checked(window));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Selecting_a_part_opens_the_tab_that_holds_it() => OnWindow(TestForks.C, (window, _) =>
    {
        window.ShowPart("Records/Employment");
        Assert.Equal(Records, window.ShownTab);
        Assert.Equal(Records, Checked(window));
        window.ShowPart("Torso/Chest");
        Assert.Equal(Markings, window.ShownTab);
        window.ShowPart("Traits");
        Assert.Equal(Traits, window.ShownTab);
        window.ShowPart("Outfit");
        Assert.Equal(Loadout, window.ShownTab);
        window.ShowPart("");
        Assert.Equal(Appearance, window.ShownTab);
        Assert.Equal(Appearance, Checked(window));
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_part_on_a_tab_the_fork_lacks_opens_Appearance() => OnWindow(TestForks.A, (window, _) =>
    {
        window.ShowTab(Markings);
        window.ShowPart("Records");
        Assert.Equal(Appearance, window.ShownTab);
        window.ShowTab(Markings);
        window.ShowPart("Allergies");
        Assert.Equal(Appearance, window.ShownTab);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_tab_that_goes_away_hands_over_to_Appearance() => OnWindow(TestForks.C, async (window, _) =>
    {
        window.ShowTab(Records);
        Assert.Equal(Records, window.ShownTab);

        var load = (Task<bool>)typeof(MainWindow).GetMethod("LoadForkAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(window, [TestForks.A, false])!;
        Assert.True(await load);

        Assert.Equal(Appearance, window.ShownTab);
        Assert.DoesNotContain(Records, window.TabsShown);
        Assert.Equal(Appearance, Checked(window));
    });

    [Fact]
    public Task A_tab_remembers_its_item() => OnWindow(TestForks.C, (window, _) =>
    {
        window.ShowPart("Records/Medical");
        window.ShowTab(Jobs);
        window.ShowTab(Records);

        var item = (TreeViewItem)Find<TreeView>(window, "Explorer").SelectedItem!;
        Assert.StartsWith("Medical", ((Panel)item.Header!).Children.OfType<TextBlock>().First().Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Only_tabs_with_many_items_show_the_side_list() => OnWindow(TestForks.C, (window, _) =>
    {
        var side = Find<Border>(window, "SideList");
        foreach (var tab in window.TabsShown)
        {
            window.ShowTab(tab);
            Assert.Equal(tab, window.ShownTab);
            Assert.Equal(EditorTabs.HasSideList(tab), side.IsVisible);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Every_tab_is_reachable_at_the_smallest_window() => OnWindow(TestForks.C, (window, _) =>
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<ScrollViewer>(window, "PageHost").Bounds.Height >= 200);

        var buttons = Find<TabStripPanel>(window, "TabStrip").Children.OfType<RadioButton>().ToList();
        Assert.Equal(Enum.GetValues<EditorTab>().Except([Cybernetics, IcInfo, OocInfo, Wardrobe]).Count(), buttons.Count);
        foreach (var button in buttons)
        {
            var at = button.TranslatePoint(default, window)!.Value;
            Assert.True(button.Bounds.Width > 0);
            Assert.InRange(at.X, 0, window.ClientSize.Width - button.Bounds.Width);
            Assert.InRange(at.Y, 0, window.ClientSize.Height - button.Bounds.Height);

            var middle = at + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(button.Tag, window.ShownTab);
        }
        return Task.CompletedTask;
    }, width: 900, height: 600);

    [Fact]
    public Task Ctrl_number_keys_follow_the_strip() => OnWindow(TestForks.A, (window, _) =>
    {
        window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Markings, window.ShownTab);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_dragged_split_between_page_and_table_stays_through_edits_and_clicks() => OnWindow(TestForks.A, (window, _) =>
    {
        window.ShowTab(Markings);
        var rows = Find<Grid>(window, "PageArea").RowDefinitions;
        // As dragging the splitter between the page and the table would leave it.
        rows[0].Height = new GridLength(120);
        rows[2].Height = new GridLength(1, GridUnitType.Star);

        Func<EditorSession, IReadOnlyList<Core.Profiles.RuleFix>> edit = s => s.Edit(f => f.Age = 40);
        typeof(MainWindow).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [edit, false]);
        var explorer = Find<TreeView>(window, "Explorer");
        var organ = explorer.Items.OfType<TreeViewItem>().First();
        var layer = organ.Items.OfType<TreeViewItem>().First();
        explorer.SelectedItem = layer;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(layer, explorer.SelectedItem);
        Assert.Equal(Markings, window.ShownTab);
        Assert.Equal(new GridLength(120), rows[0].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), rows[2].Height);

        // Another tab has its own split, and this one is as it was left when the tab is shown again.
        window.ShowTab(Jobs);
        window.ShowTab(Markings);
        Assert.Equal(new GridLength(120), rows[0].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), rows[2].Height);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Each_tab_keeps_the_split_the_user_left_it_with() => OnWindow(TestForks.C, (window, _) =>
    {
        var rows = Find<Grid>(window, "PageArea").RowDefinitions;
        window.ShowTab(Markings);
        rows[0].Height = new GridLength(120);
        window.ShowTab(Loadout);
        Assert.Equal(new GridLength(1, GridUnitType.Star), rows[0].Height);
        rows[0].Height = new GridLength(90);

        window.ShowTab(Appearance);
        window.ShowTab(Loadout);
        Assert.Equal(new GridLength(90), rows[0].Height);
        window.ShowTab(Markings);
        Assert.Equal(new GridLength(120), rows[0].Height);
        return Task.CompletedTask;
    });

    internal static void Apply(MainWindow window, Func<EditorSession, IReadOnlyList<Core.Profiles.RuleFix>> edit) =>
        typeof(MainWindow).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [edit, false]);

    private static string HeaderText(TreeViewItem item) =>
        string.Concat(((Panel)item.Header!).Children.OfType<TextBlock>().Select(t => t.Text));

    // The key of the part the list has selected; the part's type is private to the window.
    private static string SelectedKey(TreeView explorer)
    {
        var tag = ((TreeViewItem)explorer.SelectedItem!).Tag!;
        return (string)tag.GetType().GetProperty("Key")!.GetValue(tag)!;
    }

    private static IEnumerable<TreeViewItem> AllItems(ItemsControl root) =>
        root.Items.OfType<TreeViewItem>().SelectMany(i => AllItems(i).Prepend(i));

    [Fact]
    public Task An_edit_that_changes_nothing_in_the_list_leaves_the_list_alone() => OnWindow(TestForks.A, (window, _) =>
    {
        Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        window.ShowTab(Markings);
        var explorer = Find<TreeView>(window, "Explorer");
        var remembered = explorer.Items[0];

        Apply(window, s => s.Edit(f => f.Age = 40));

        Assert.Same(remembered, explorer.Items[0]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_list_is_current_when_its_tab_is_opened_after_an_edit_elsewhere() => OnWindow(TestForks.A, (window, editor) =>
    {
        Assert.Equal(Appearance, window.ShownTab);
        Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        window.ShowTab(Markings);

        var scar = editor.MarkingName("Scar");
        Assert.Contains(AllItems(Find<TreeView>(window, "Explorer")), item => HeaderText(item) == scar);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Editing_the_selected_marking_keeps_it_selected() => OnWindow(TestForks.A, (window, editor) =>
    {
        Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        window.ShowPart("Torso/Chest/0");
        var explorer = Find<TreeView>(window, "Explorer");
        var key = SelectedKey(explorer);

        Apply(window, s => s.SetMarkingColor("Torso", "Chest", 0, 0, Rgba.Parse("#336699")));

        Assert.Equal(Rgba.Parse("#336699"), editor.Look!.Markings["Torso"]["Chest"][0].Colors[0]);
        Assert.Equal(Markings, window.ShownTab);
        Assert.Equal(key, SelectedKey(explorer));
        return Task.CompletedTask;
    });

    internal static IEnumerable<T> InPage<T>(MainWindow window) where T : Control =>
        Find<ScrollViewer>(window, "PageHost").GetVisualDescendants().OfType<T>();

    [Fact]
    public Task No_job_free_is_on_the_Jobs_tab_and_not_on_Appearance() => OnWindow(TestForks.C, (window, _) =>
    {
        window.ShowTab(Jobs);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "No job free");
        Assert.DoesNotContain(InPage<TextBlock>(window), t => t.Text == "Spawn priority");

        window.ShowTab(Appearance);
        Assert.DoesNotContain(InPage<TextBlock>(window), t => t.Text == "No job free");
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "Spawn priority");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Description_has_its_own_tab() => OnWindow(TestForks.A, (window, editor) =>
    {
        Assert.DoesNotContain(InPage<TextBox>(window), t => t.AcceptsReturn);

        window.ShowTab(Description);
        var box = Assert.Single(InPage<TextBox>(window), t => t.AcceptsReturn);
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        box.Text = "Tall.";
        Find<TabStripPanel>(window, "TabStrip").Children.OfType<RadioButton>().First().Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Tall.", editor.File!.FlavorText);
        return Task.CompletedTask;
    });

    // Types into a box as a user would: focus it, then set its text without leaving it.
    internal static void Type(TextBox box, string text)
    {
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        box.Text = text;
    }

    private static void CtrlKey(MainWindow window, Key key, PhysicalKey physical, string symbol)
    {
        window.KeyPress(key, RawInputModifiers.Control, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Switching_tabs_by_shortcut_keeps_the_description_being_typed() => OnWindow(TestForks.A, (window, editor) =>
    {
        window.ShowTab(Description);
        Type(Assert.Single(InPage<TextBox>(window), t => t.AcceptsReturn), "Tall.");

        CtrlKey(window, Key.D1, PhysicalKey.Digit1, "1");

        Assert.Equal(Appearance, window.ShownTab);
        Assert.Equal("Tall.", editor.File!.FlavorText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Switching_tabs_by_shortcut_keeps_the_name_being_typed() => OnWindow(TestForks.A, (window, editor) =>
    {
        Type(InPage<TextBox>(window).First(t => t.PlaceholderText!.StartsWith("Name")), "Vesper Quill");

        CtrlKey(window, Key.D2, PhysicalKey.Digit2, "2");

        Assert.Equal(Jobs, window.ShownTab);
        Assert.Equal("Vesper Quill", editor.File!.Name);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Changing_species_swaps_the_page_for_the_species_table_and_back() => OnWindow(TestForks.A, (window, editor) =>
    {
        var page = Find<ScrollViewer>(window, "PageHost");
        var grid = Find<DataGrid>(window, "SpeciesGrid");
        Assert.Equal("Human", editor.Look!.Species);
        Assert.False(window.PickingSpecies);

        window.ShowSpeciesPicker();
        Assert.True(window.PickingSpecies);
        Assert.True(grid.IsEffectivelyVisible);
        Assert.False(page.IsEffectivelyVisible);

        grid.SelectedItem = ((IEnumerable<SpeciesRow>)grid.ItemsSource!).First(r => r.Id == "Lizard");
        Find<Button>(window, "UseSpeciesButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Lizard", editor.Look!.Species);
        Assert.False(window.PickingSpecies);
        Assert.True(page.IsEffectivelyVisible);
        Assert.False(grid.IsEffectivelyVisible);

        window.ShowSpeciesPicker();
        Find<Button>(window, "SpeciesBackButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.PickingSpecies);
        Assert.True(page.IsEffectivelyVisible);
        Assert.Equal("Lizard", editor.Look!.Species);

        window.ShowSpeciesPicker();
        window.ShowTab(Jobs);
        window.ShowTab(Appearance);
        Assert.False(window.PickingSpecies);
        Assert.True(page.IsEffectivelyVisible);
        return Task.CompletedTask;
    });

    // Clicks a control with the mouse, as the strip's own test does.
    internal static void Click(MainWindow window, Control control)
    {
        var at = control.TranslatePoint(default, window)!.Value + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Clicking_the_open_Appearance_tab_leaves_the_species_table() => OnWindow(TestForks.A, (window, _) =>
    {
        window.ShowSpeciesPicker();
        Assert.True(window.PickingSpecies);

        var appearance = Find<TabStripPanel>(window, "TabStrip").Children.OfType<RadioButton>().First(b => (EditorTab)b.Tag! == Appearance);
        Click(window, appearance);

        Assert.False(window.PickingSpecies);
        Assert.Equal(Appearance, window.ShownTab);
        Assert.True(Find<ScrollViewer>(window, "PageHost").IsEffectivelyVisible);
        Assert.False(Find<DataGrid>(window, "SpeciesGrid").IsEffectivelyVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Clicking_the_species_in_the_breadcrumbs_leaves_the_species_table() => OnWindow(TestForks.A, (window, editor) =>
    {
        window.ShowSpeciesPicker();
        Assert.True(window.PickingSpecies);

        var species = Find<StackPanel>(window, "Breadcrumbs").Children.OfType<Button>().First(b => (string?)b.Content == editor.DisplayName(editor.Content!.Characters.Species["Human"]));
        species.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.PickingSpecies);
        Assert.True(Find<ScrollViewer>(window, "PageHost").IsEffectivelyVisible);
        return Task.CompletedTask;
    });

    // Starts a new character through the window, as the New button does, without asking to save.
    private static void NewThroughWindow(MainWindow window)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(MainWindow).GetField("_dirty", flags)!.SetValue(window, false);
        var data = Path.Combine(Path.GetTempPath(), "paperdoll-test-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        try
        {
            typeof(MainWindow).GetMethod("OnNew", flags)!.Invoke(window, [null, null]);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
            if (Directory.Exists(data))
                Directory.Delete(data, true);
        }
    }

    [Fact]
    public Task The_name_for_the_Appearance_page_and_the_status_after_New_do_not_say_inspector() => OnWindow(TestForks.A, (window, _) =>
    {
        Assert.Equal("Appearance", Find<TextBlock>(window, "InspectorTitle").Text);

        NewThroughWindow(window);

        Assert.DoesNotContain("inspector", Find<TextBlock>(window, "StatusText").Text!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Appearance", Find<TextBlock>(window, "StatusText").Text!);
        Assert.Equal("Appearance", Find<TextBlock>(window, "InspectorTitle").Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_new_character_forgets_the_marking_each_tab_remembered() => OnWindow(TestForks.A, (window, _) =>
    {
        Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        window.ShowPart("Torso/Chest/1");
        window.ShowTab(Appearance);

        NewThroughWindow(window);
        window.ShowTab(Markings);

        var explorer = Find<TreeView>(window, "Explorer");
        Assert.Same(explorer.Items[0], explorer.SelectedItem);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Loading_a_fork_forgets_the_marking_each_tab_remembered() => OnWindow(TestForks.A, async (window, _) =>
    {
        window.ShowPart("Torso/Chest");
        window.ShowTab(Appearance);

        var load = (Task<bool>)typeof(MainWindow).GetMethod("LoadForkAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(window, [TestForks.A, false])!;
        Assert.True(await load);
        window.ShowTab(Markings);

        var explorer = Find<TreeView>(window, "Explorer");
        Assert.Same(explorer.Items[0], explorer.SelectedItem);
    });

    [Fact]
    public Task Markings_for_a_species_that_takes_none_shows_a_note_not_the_Appearance_page() => OnWindow(TestForks.C, (window, editor) =>
    {
        Apply(window, s => { s.NewCharacter("Blob"); return s.LastFixes; });
        Assert.Equal("Blob", editor.Look!.Species);

        window.ShowTab(Markings);

        Assert.Equal(Markings, window.ShownTab);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "This species takes no markings.");
        Assert.DoesNotContain(InPage<TextBlock>(window), t => t.Text == "Spawn priority");
        Assert.Equal("Markings", Find<TextBlock>(window, "InspectorTitle").Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task View_menu_items_have_unique_access_keys() => OnWindow(TestForks.C, (window, _) =>
    {
        var headers = Find<MenuItem>(window, "ViewMenu").Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();

        Assert.Equal(window.TabsShown.Select(EditorTabs.Title), headers.Select(h => h.Replace("_", "")));
        Assert.All(headers, h => Assert.Equal(1, h.Count(c => c == '_')));
        var keys = headers.Select(h => char.ToUpperInvariant(h[h.IndexOf('_') + 1])).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        return Task.CompletedTask;
    });
}
