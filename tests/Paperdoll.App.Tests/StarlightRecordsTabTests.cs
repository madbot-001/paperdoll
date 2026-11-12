using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Tests;
using static Paperdoll.App.EditorTab;
using static Paperdoll.App.Tests.TabTests;

namespace Paperdoll.App.Tests;

/// <summary>Starlight's records on made-up fork "e": the tab's place, admin entries and the height the game sets.</summary>
public class StarlightRecordsTabTests
{
    [Fact]
    public Task Records_come_after_the_info_tabs() => OnWindow(TestForks.E, (window, _) =>
    {
        Assert.Equal([Appearance, Jobs, Markings, IcInfo, OocInfo, Records, Credits, Messages, Source], window.TabsShown);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Height_and_weight_are_shown_but_not_edited() => OnWindow(TestForks.E, (window, _) =>
    {
        window.ShowTab(Records);

        Assert.Empty(InPage<NumericUpDown>(window));
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "164 cm");
        Assert.Contains(InPage<Button>(window), b => b.Content as string == "0 entries...");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Admin_entries_can_be_added_and_written() => OnWindow(TestForks.E, (window, editor) =>
    {
        window.ShowPart("Records/Admin");
        Assert.Equal(Records, window.ShownTab);

        var add = InPage<Button>(window).Single(b => b.Content as string == "Add an entry");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(CharacterRecords.Entries(editor.File!, "adminEntries", RecordFormat.Starlight));
        return Task.CompletedTask;
    });
}
