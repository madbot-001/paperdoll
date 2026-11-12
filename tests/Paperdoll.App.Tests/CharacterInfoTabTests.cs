using Avalonia.Controls;
using Avalonia.Threading;
using Paperdoll.App.Controls;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Tests;
using static Paperdoll.App.EditorTab;
using static Paperdoll.App.Tests.TabTests;

namespace Paperdoll.App.Tests;

/// <summary>Starlight-style character info on made-up fork "d": its tabs, text boxes, glowing boxes and silicon voices.</summary>
public class CharacterInfoTabTests
{
    // Leaves the box being typed in, as clicking elsewhere would.
    private static void Leave(MainWindow window)
    {
        window.FindControl<TabStripPanel>("TabStrip")!.Children.OfType<RadioButton>().First().Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static List<TextBox> InfoBoxes(MainWindow window) => InPage<TextBox>(window).Where(t => t.AcceptsReturn).ToList();

    [Fact]
    public Task The_info_tabs_come_before_records_and_replace_Description() => OnWindow(TestForks.D, (window, _) =>
    {
        Assert.Equal([Appearance, Jobs, Markings, IcInfo, OocInfo, Credits, Messages, Source], window.TabsShown);
        Assert.Equal(["IC Info", "OOC Info"], window.TabsShown.Where(t => t is IcInfo or OocInfo).Select(EditorTabs.Title));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Forks_without_the_info_have_no_info_tabs_and_keep_Description() => OnWindow(TestForks.C, (window, _) =>
    {
        Assert.DoesNotContain(IcInfo, window.TabsShown);
        Assert.DoesNotContain(OocInfo, window.TabsShown);
        Assert.Contains(Description, window.TabsShown);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Typing_in_the_IC_boxes_changes_the_file() => OnWindow(TestForks.D, (window, editor) =>
    {
        window.ShowTab(IcInfo);
        var boxes = InfoBoxes(window);
        Assert.Equal(4, boxes.Count);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "Physical description");

        Type(boxes[0], "Tall.");
        Leave(window);
        window.ShowTab(IcInfo);
        Type(InfoBoxes(window)[3], "Hates the cold.");
        Leave(window);

        Assert.Equal("Tall.", editor.File!.FlavorText);
        Assert.Equal("Tall.", editor.File.GetValue("physicalDescription"));
        Assert.Equal("Hates the cold.", editor.File.GetValue("secrets"));
        Assert.Null(editor.File.GetValue("personalityDescription"));
        Assert.Null(editor.File.GetValue("exploitableInfo"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Typing_in_the_OOC_boxes_changes_the_file() => OnWindow(TestForks.D, (window, editor) =>
    {
        window.ShowTab(OocInfo);
        var boxes = InfoBoxes(window);
        Assert.Equal(2, boxes.Count);

        Type(boxes[1], "Ask before drama.");
        Leave(window);

        Assert.Equal("Ask before drama.", editor.File!.GetValue("oOCNotes"));
        Assert.Null(editor.File.GetValue("personalNotes"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task An_info_box_left_empty_writes_nothing() => OnWindow(TestForks.D, (window, editor) =>
    {
        var before = editor.File!.ToYaml();
        window.ShowTab(IcInfo);
        InfoBoxes(window)[1].Focus();
        Dispatcher.UIThread.RunJobs();
        Leave(window);

        Assert.Equal(before, editor.File.ToYaml());
        return Task.CompletedTask;
    });

    private static CheckBox Glowing(MainWindow window) =>
        Assert.Single(InPage<CheckBox>(window), b => (string?)b.Content == "Glowing");

    [Fact]
    public Task The_eyes_glowing_box_sets_the_flag() => OnWindow(TestForks.D, (window, editor) =>
    {
        var box = Glowing(window);
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.File!.IsGlowing(CharacterInfo.EyeGlowing));

        box.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("False", CharacterFile.Parse(editor.File.ToYaml()).Appearance.Children
            .First(kv => kv.Key.ToString() == "eyeGlowing").Value.ToString());
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_hair_glowing_box_is_beside_the_hair_colour() => OnWindow(TestForks.D, (window, editor) =>
    {
        editor.AddMarking("Hair", "Hair", "HairShort");
        editor.AddMarking("FacialHair", "FacialHair", "Beard");

        window.ShowPart("Hair/Hair/0");
        Glowing(window).IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.File!.IsGlowing(CharacterInfo.HairGlowing));
        Assert.False(editor.File.IsGlowing(CharacterInfo.FacialHairGlowing));

        // The layer's list has the box too, beside the colour of each marking in it.
        window.ShowPart("FacialHair/FacialHair");
        Glowing(window).IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.File.IsGlowing(CharacterInfo.FacialHairGlowing));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Forks_without_glowing_show_no_glowing_box() => OnWindow(TestForks.C, (window, _) =>
    {
        Assert.DoesNotContain(InPage<CheckBox>(window), b => (string?)b.Content == "Glowing");
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_silicon_voice_lists_the_forks_silicon_voices_by_name() => OnWindow(TestForks.D, (window, editor) =>
    {
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "Silicon voice");
        var voice = Assert.Single(InPage<ComboBox>(window), c => c.ItemsSource is IEnumerable<string> names && names.Contains("Low Hum"));
        Assert.Equal(["Low Hum", "Bright Chirp"], (IEnumerable<string>)voice.ItemsSource!);
        Assert.Equal(-1, voice.SelectedIndex);

        voice.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Chirp", editor.File!.GetValue("siliconVoice"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Forks_without_silicon_voices_have_no_silicon_voice_row() => OnWindow(TestForks.C, (window, _) =>
    {
        Assert.DoesNotContain(InPage<TextBlock>(window), t => t.Text == "Silicon voice");
        return Task.CompletedTask;
    });
}
