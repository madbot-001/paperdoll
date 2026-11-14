using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperdoll.App.Controls;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

// Run alone, as saving the mode points Paperdoll's own folder elsewhere for a while.
[Collection(nameof(OwnFolder))]
public class ColorSlidersTests
{
    // The block for a field on the Appearance page, found again after each edit rebuilds the page.
    private static ColorSliders Block(MainWindow window, string key) =>
        TabTests.InPage<ColorSliders>(window).Single(c => c.Key == key);

    private static string[] Labels(ColorSliders block) => block.Rows.Select(r => r.Label.Text!).ToArray();

    private static string[] Numbers(ColorSliders block) => block.Rows.Select(r => r.Number.Text!).ToArray();

    // Types into a box and presses Enter, as a user would.
    private static void Enter(MainWindow window, TextBox box, string text)
    {
        TabTests.Type(box, text);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetEyes(MainWindow window, string hex) => TabTests.Apply(window, s => s.SetEyes(Rgba.Parse(hex)));

    // Every test starts in the default mode; the mode is one setting for the whole app.
    private static Task OnWindow(Core.Forks.ForkInfo fork, Action<MainWindow, Core.Editing.EditorSession> test, double width = 1100, double height = 720) =>
        TabTests.OnWindow(fork, (window, editor) =>
        {
            ColorSliders.Mode = ColorMode.Hsv;
            test(window, editor);
            return Task.CompletedTask;
        }, width, height);

    // Scrolls a control into view and lays the window out, for mouse input.
    private static void Show(MainWindow window, Control control)
    {
        window.UpdateLayout();
        control.BringIntoView();
        window.UpdateLayout();
    }

    // Drags a slider's thumb to the slider's left end with the mouse and lets go.
    private static void DragToStart(MainWindow window, Slider slider)
    {
        Show(window, slider);
        var thumb = slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
        var from = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var to = slider.TranslatePoint(new Point(4, slider.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void CommitTyping(MainWindow window) =>
        typeof(MainWindow).GetMethod("CommitTyping", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, []);

    [Fact]
    public Task The_Appearance_page_shows_the_eye_colour_sliders() => OnWindow(TestForks.A, (window, _) =>
    {
        var eyes = Block(window, "eyes");

        Assert.Equal(["H", "S", "V"], Labels(eyes));
        Assert.Equal(["HSV", "RGB"], eyes.ModeBox.Items.Cast<object>().Select(i => i.ToString()));
        Assert.Equal(0, eyes.ModeBox.SelectedIndex);
        Assert.NotNull(eyes.HexBox);
        // Free skin colour has the same block.
        Assert.Equal(["H", "S", "V"], Labels(Block(window, "skin")));
    });

    [Fact]
    public Task Choosing_RGB_shows_the_colours_0_to_255_values() => OnWindow(TestForks.A, (window, _) =>
    {
        SetEyes(window, "#336699");

        Block(window, "eyes").ModeBox.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        var eyes = Block(window, "eyes");
        Assert.Equal(ColorMode.Rgb, ColorSliders.Mode);
        Assert.Equal(["R", "G", "B"], Labels(eyes));
        Assert.Equal(["51", "102", "153"], Numbers(eyes));
        // Every block follows the one setting.
        Assert.Equal(["R", "G", "B"], Labels(Block(window, "skin")));
    });

    [Fact]
    public Task Plus_on_R_raises_the_red_by_one() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        ColorSliders.Mode = ColorMode.Rgb;

        Click(Block(window, "eyes").Rows[0].Plus);

        Assert.Equal(Rgba.Parse("#346699"), editor.Look!.EyeColor);
        Assert.Equal(["52", "102", "153"], Numbers(Block(window, "eyes")));

        Click(Block(window, "eyes").Rows[0].Minus);
        Assert.Equal(Rgba.Parse("#336699"), editor.Look!.EyeColor);
    });

    [Fact]
    public Task A_number_past_the_range_is_clamped_and_text_that_is_no_number_is_ignored() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        ColorSliders.Mode = ColorMode.Rgb;

        Enter(window, Block(window, "eyes").Rows[0].Number, "300");
        Assert.Equal(Rgba.Parse("#FF6699"), editor.Look!.EyeColor);
        Assert.Equal("255", Block(window, "eyes").Rows[0].Number.Text);

        Enter(window, Block(window, "eyes").Rows[1].Number, "x");
        Assert.Equal(Rgba.Parse("#FF6699"), editor.Look!.EyeColor);
        Assert.Equal("102", Block(window, "eyes").Rows[1].Number.Text);
    });

    [Fact]
    public Task A_hex_typed_in_the_Hex_box_changes_the_colour_and_the_sliders() => OnWindow(TestForks.A, (window, editor) =>
    {
        Enter(window, Block(window, "eyes").HexBox, "#336699");

        Assert.Equal(Rgba.Parse("#336699"), editor.Look!.EyeColor);
        var eyes = Block(window, "eyes");
        Assert.Equal(["210", "67", "60"], Numbers(eyes));
        Assert.Equal([210.0, 67, 60], eyes.Rows.Select(r => r.Slider.Value));
        Assert.Equal("#336699", eyes.HexBox.Text);
    });

    [Fact]
    public Task Dragging_a_slider_changes_the_preview_and_keeps_the_page() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        var eyes = Block(window, "eyes");

        eyes.Rows[0].Slider.Value = 120;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(eyes, Block(window, "eyes"));
        Assert.Equal(120, MathF.Round(editor.Look!.EyeColor.ToHsv().H * 360));
        Assert.Equal("120", eyes.Rows[0].Number.Text);
    });

    [Fact]
    public Task Dragging_the_thumb_with_the_mouse_moves_the_slider() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        var slider = Block(window, "eyes").Rows[0].Slider;

        DragToStart(window, slider);

        Assert.Equal(0, slider.Value);
        Assert.Equal(0, MathF.Round(editor.Look!.EyeColor.ToHsv().H * 360));
    });

    [Fact]
    public Task A_grey_keeps_its_hue_when_value_goes_to_0_and_back() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");

        // Typed: a normal edit, after which the page is built again.
        Enter(window, Block(window, "eyes").Rows[2].Number, "0");
        Assert.Equal(Rgba.Parse("#000000"), editor.Look!.EyeColor);
        Assert.Equal(["210", "67", "0"], Numbers(Block(window, "eyes")));

        Enter(window, Block(window, "eyes").Rows[2].Number, "60");
        Assert.Equal(210, MathF.Round(editor.Look!.EyeColor.ToHsv().H * 360));
        Assert.Equal(["210", "67", "60"], Numbers(Block(window, "eyes")));

        // Dragged, then the page built again by another edit.
        Block(window, "eyes").Rows[2].Slider.Value = 0;
        Dispatcher.UIThread.RunJobs();
        TabTests.Apply(window, s => s.Edit(f => f.Age = 40));
        Assert.Equal(210, Block(window, "eyes").Rows[0].Slider.Value);
        Block(window, "eyes").Rows[2].Slider.Value = 60;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(210, MathF.Round(editor.Look!.EyeColor.ToHsv().H * 360));
    });

    [Fact]
    public Task The_colour_is_named_in_the_forks_own_words_first() => OnWindow(TestForks.C, (window, _) =>
    {
        SetEyes(window, "#FF0000");
        Assert.Equal("vivid scarlet", Block(window, "eyes").NameText.Text);

        ColorSliders.Mode = ColorMode.Rgb;
        Enter(window, Block(window, "eyes").HexBox, "#000000");
        Assert.Equal("coal", Block(window, "eyes").NameText.Text);
    });

    [Fact]
    public Task A_fork_without_colour_names_uses_the_engines_English_ones() => OnWindow(TestForks.A, (window, _) =>
    {
        SetEyes(window, "#FF0000");
        Assert.Equal("strong red", Block(window, "eyes").NameText.Text);
    });

    [Fact]
    public Task A_species_with_one_skin_tone_slider_keeps_it() => OnWindow(TestForks.C, (window, editor) =>
    {
        TabTests.Apply(window, s => { s.NewCharacter("Toned"); return s.LastFixes; });
        Assert.Equal("Toned", editor.Look!.Species);

        Assert.Contains(TabTests.InPage<TextBlock>(window), t => t.Text == "Skin tone");
        Assert.DoesNotContain(TabTests.InPage<ColorSliders>(window), c => c.Key == "skin");
        Assert.Equal(["H", "S", "V"], Labels(Block(window, "eyes")));
    });

    [Fact]
    public Task The_marking_colour_pop_up_holds_the_same_sliders() => OnWindow(TestForks.A, (window, _) =>
    {
        TabTests.Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        window.ShowPart("Torso/Chest/0");
        Dispatcher.UIThread.RunJobs();

        var swatch = TabTests.InPage<Button>(window).First(b => b.Flyout != null);
        var flyout = (Flyout)swatch.Flyout!;
        flyout.ShowAt(swatch);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var sliders = ((Control)flyout.Content!).GetLogicalDescendants().OfType<ColorSliders>().Single();
            Assert.Equal(["H", "S", "V"], Labels(sliders));
            Assert.Equal(["HSV", "RGB"], sliders.ModeBox.Items.Cast<object>().Select(i => i.ToString()));
            Assert.NotNull(sliders.HexBox);
        }
        finally
        {
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task Typing_in_one_box_then_another_keeps_both_on_the_page_and_in_the_file() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        var eyes = Block(window, "eyes");
        var saturation = eyes.Rows[1].Number;

        TabTests.Type(eyes.Rows[0].Number, "120");
        saturation.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(120, MathF.Round(editor.Look!.EyeColor.ToHsv().H * 360));

        // The box being typed in is still the one on the page.
        Assert.Same(eyes, Block(window, "eyes"));
        Assert.True(((Avalonia.Visual)window.FocusManager!.GetFocusedElement()!).IsAttachedToVisualTree());
        TabTests.Type(saturation, "5");
        CommitTyping(window);

        var (h, s, v, _) = editor.Look!.EyeColor.ToHsv();
        Assert.Equal((120, 5, 60), ((int)MathF.Round(h * 360), (int)MathF.Round(s * 100), (int)MathF.Round(v * 100)));
        Assert.Equal(["120", "5", "60"], Numbers(Block(window, "eyes")));
    });

    [Fact]
    public Task A_typed_number_left_by_clicking_plus_on_another_row_applies_both() => OnWindow(TestForks.A, (window, editor) =>
    {
        SetEyes(window, "#336699");
        ColorSliders.Mode = ColorMode.Rgb;
        var eyes = Block(window, "eyes");

        TabTests.Type(eyes.Rows[0].Number, "200");
        Show(window, eyes.Rows[1].Plus);
        TabTests.Click(window, eyes.Rows[1].Plus);

        Assert.Equal(Rgba.Parse("#C86799"), editor.Look!.EyeColor);
        Assert.Equal(["200", "103", "153"], Numbers(Block(window, "eyes")));
    });

    [Fact]
    public Task Letting_go_of_a_slider_shows_the_colour_the_skin_rule_kept() => OnWindow(TestForks.C, (window, editor) =>
    {
        TabTests.Apply(window, s => { s.NewCharacter("Dim"); return s.LastFixes; });
        TabTests.Apply(window, s => s.SetSkin(Rgba.Parse("#FFFFFF")));

        DragToStart(window, Block(window, "skin").Rows[2].Slider);

        // The rule holds value to 50 and up; the block shows what the character got.
        var skin = Block(window, "skin");
        Assert.Equal("#808080FF", editor.Look!.SkinColor.ToHex());
        Assert.Equal("#808080", skin.HexBox.Text);
        Assert.Equal("50", skin.Rows[2].Number.Text);
    });

    [Fact]
    public Task Every_row_of_the_eye_block_fits_a_small_window() => OnWindow(TestForks.A, (window, _) =>
    {
        var page = TabTests.InPage<ColorSliders>(window).First().FindAncestorOfType<ScrollViewer>()!;
        var eyes = Block(window, "eyes");
        Show(window, eyes);
        var right = page.TranslatePoint(new Point(page.Viewport.Width, 0), window)!.Value.X;

        foreach (var control in eyes.Rows.SelectMany(r => new Control[] { r.Slider, r.Minus, r.Number, r.Plus }).Append(eyes.HexBox))
        {
            Assert.True(control.Bounds.Width > 0);
            Assert.True(control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X <= right + 0.5, $"{control} is cut off");
        }
        Assert.All(eyes.Rows, r => Assert.True(r.Number.Bounds.Width >= 36));
    }, 900, 600);

    [Fact]
    public Task A_grey_marking_colour_keeps_its_hue_after_the_pop_up_closes() => OnWindow(TestForks.A, (window, editor) =>
    {
        TabTests.Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        TabTests.Apply(window, s => s.SetMarkingColor("Torso", "Chest", 0, 0, Rgba.Parse("#336699")));
        window.ShowPart("Torso/Chest/0");
        Dispatcher.UIThread.RunJobs();

        ColorSliders Open(out Flyout flyout)
        {
            var swatch = TabTests.InPage<Button>(window).First(b => b.Flyout != null);
            flyout = (Flyout)swatch.Flyout!;
            flyout.ShowAt(swatch);
            Dispatcher.UIThread.RunJobs();
            return ((Control)flyout.Content!).GetLogicalDescendants().OfType<ColorSliders>().Single();
        }

        var sliders = Open(out var flyout);
        sliders.Rows[2].Slider.Value = 0;
        Dispatcher.UIThread.RunJobs();
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Rgba.Parse("#000000"), editor.Look!.Markings["Torso"]["Chest"][0].Colors[0]);

        sliders = Open(out flyout);
        Assert.Equal(["210", "67", "0"], Numbers(sliders));
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();
    });

    // Opens the first colour pop-up on the page, types 120 into its H box without Enter, then closes it.
    private static void TypeHueInPopUpAndClose(MainWindow window, Action<Flyout, Button> close)
    {
        var swatch = TabTests.InPage<Button>(window).First(b => b.Flyout != null);
        var flyout = (Flyout)swatch.Flyout!;
        flyout.ShowAt(swatch);
        Dispatcher.UIThread.RunJobs();
        var sliders = ((Control)flyout.Content!).GetLogicalDescendants().OfType<ColorSliders>().Single();
        TabTests.Type(sliders.Rows[0].Number, "120");
        close(flyout, swatch);
        Dispatcher.UIThread.RunJobs();
    }

    private static void OnMarkingColour(MainWindow window)
    {
        TabTests.Apply(window, s => s.AddMarking("Torso", "Chest", "Scar"));
        TabTests.Apply(window, s => s.SetMarkingColor("Torso", "Chest", 0, 0, Rgba.Parse("#336699")));
        window.ShowPart("Torso/Chest/0");
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task A_number_typed_in_the_pop_up_is_applied_when_it_is_hidden() => OnWindow(TestForks.A, (window, editor) =>
    {
        OnMarkingColour(window);

        TypeHueInPopUpAndClose(window, (flyout, _) => flyout.Hide());

        Assert.Equal(Rgba.Parse("#329932"), editor.Look!.Markings["Torso"]["Chest"][0].Colors[0]);
    });

    [Fact]
    public Task A_number_typed_in_the_pop_up_is_applied_when_a_click_elsewhere_closes_it() => OnWindow(TestForks.A, (window, editor) =>
    {
        OnMarkingColour(window);

        TypeHueInPopUpAndClose(window, (_, _) =>
        {
            var title = window.FindControl<TextBlock>("InspectorTitle")!;
            TabTests.Click(window, title);
        });

        Assert.Equal(Rgba.Parse("#329932"), editor.Look!.Markings["Torso"]["Chest"][0].Colors[0]);
    });

    [Fact]
    public async Task A_click_on_the_track_moves_the_slider_there_and_commits_once()
    {
        ColorSliders? sliders = null;
        var commits = new List<Rgba>();
        await HeadlessApp.Session.Dispatch(() =>
        {
            ColorSliders.Mode = ColorMode.Hsv;
            sliders = new ColorSliders(Rgba.Parse("#336699"), (color, live) =>
            {
                if (!live)
                    commits.Add(color);
            }, _ => null);
            var host = new Window { Content = sliders, Width = 400, Height = 200 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var slider = sliders.Rows[0].Slider;
                var at = slider.TranslatePoint(new Point(4 + (slider.Bounds.Width - 8) * 0.75, slider.Bounds.Height / 2), host)!.Value;
                host.MouseDown(at, MouseButton.Left);
                host.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.InRange(slider.Value, 266, 274);
                var commit = Assert.Single(commits);
                Assert.InRange(MathF.Round(commit.ToHsv().H * 360), 266, 274);
            }
            finally
            {
                host.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private static void LoadSettings(MainWindow window) =>
        typeof(MainWindow).GetMethod("LoadSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, []);

    [Fact]
    public async Task The_mode_is_saved_and_the_next_start_opens_in_it()
    {
        // A folder of its own under the test run's, so no other test's settings are touched.
        var data = Path.Combine(TestData.Directory, "colour-mode-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        var settings = Path.Combine(data, "settings.json");
        try
        {
            // As the app starts: settings read, then the page shown.
            await OnWindow(TestForks.A, (window, _) =>
            {
                LoadSettings(window);
                Block(window, "eyes").ModeBox.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
            });
            var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settings))!;
            Assert.Equal(ColorMode.Rgb, saved.ColorMode);

            // A new run starts in the default until the settings are read.
            await OnWindow(TestForks.A, (window, _) =>
            {
                LoadSettings(window);
                Assert.Equal(ColorMode.Rgb, ColorSliders.Mode);
                Assert.Equal(["R", "G", "B"], Labels(Block(window, "eyes")));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
            await HeadlessApp.Session.Dispatch(() => ColorSliders.Mode = ColorMode.Hsv, TestContext.Current.CancellationToken);
            if (Directory.Exists(data))
                Directory.Delete(data, true);
        }
    }
}
