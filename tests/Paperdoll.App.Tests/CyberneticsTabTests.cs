using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperdoll.Core.Tests;
using static Paperdoll.App.EditorTab;
using static Paperdoll.App.Tests.TabTests;

namespace Paperdoll.App.Tests;

/// <summary>Starlight-style cybernetics on made-up fork "e": the tab's place, points, and installing and removing parts.</summary>
public class CyberneticsTabTests
{
    // The button in the page row labelled with a part's name.
    private static Button RowButton(MainWindow window, string label)
    {
        var row = Grid.GetRow((Control)InPage<TextBlock>(window).First(t => t.Text == label).Parent!);
        return InPage<Border>(window).Where(b => Grid.GetRow(b) == row && Grid.GetColumn(b) == 1)
            .SelectMany(b => b.GetVisualDescendants().OfType<Button>()).Single();
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task The_tab_comes_after_Markings_and_before_IC_Info() => OnWindow(TestForks.E, (window, _) =>
    {
        var tabs = window.TabsShown.ToList();
        Assert.Equal(tabs.IndexOf(Markings) + 1, tabs.IndexOf(Cybernetics));
        Assert.Equal(tabs.IndexOf(IcInfo) - 1, tabs.IndexOf(Cybernetics));
        Assert.False(EditorTabs.HasSideList(Cybernetics));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Forks_without_cybernetics_have_no_tab() => OnWindow(TestForks.D, (window, _) =>
    {
        Assert.DoesNotContain(Cybernetics, window.TabsShown);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Installing_a_part_brings_its_hand_and_fills_the_points() => OnWindow(TestForks.E, (window, editor) =>
    {
        window.ShowTab(Cybernetics);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "0 of 3 points used");
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "2 points, with the grip hand");
        Assert.DoesNotContain(InPage<TextBlock>(window), t => t.Text == "glow eye");

        Press(RowButton(window, "grip arm"));

        Assert.Equal(["GripArm", "GripHand"], editor.File!.Cybernetics);
        Assert.Contains(InPage<TextBlock>(window), t => t.Text == "3 of 3 points used");
        return Task.CompletedTask;
    });

    [Fact]
    public Task An_install_over_the_points_is_refused_with_the_reason() => OnWindow(TestForks.E, (window, editor) =>
    {
        window.ShowTab(Cybernetics);
        Press(RowButton(window, "grip arm"));

        var leg = RowButton(window, "steel leg");
        Assert.False(leg.IsEnabled);
        Assert.Equal("Needs 3 points with the parts it brings; 0 of 3 left.", ToolTip.GetTip(leg));
        Press(leg);
        Assert.Equal(["GripArm", "GripHand"], editor.File!.Cybernetics);
        return Task.CompletedTask;
    });

    [Fact]
    public Task An_attached_part_cannot_be_removed_on_its_own() => OnWindow(TestForks.E, (window, editor) =>
    {
        window.ShowTab(Cybernetics);
        Press(RowButton(window, "grip arm"));

        var hand = RowButton(window, "grip hand");
        Assert.Equal("Remove", hand.Content);
        Assert.False(hand.IsEnabled);
        Assert.Equal("Comes attached to the grip arm; remove that instead.", ToolTip.GetTip(hand));

        Press(RowButton(window, "grip arm"));
        Assert.Empty(editor.File!.Cybernetics);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_species_without_points_says_so() => OnWindow(TestForks.E, (window, editor) =>
    {
        Apply(window, s => s.ChangeSpecies("Slime"));
        window.ShowTab(Cybernetics);

        Assert.Contains(InPage<TextBlock>(window), t => t.Text?.EndsWith("cannot have roundstart cybernetics.") == true);
        Assert.Empty(InPage<Button>(window));
        return Task.CompletedTask;
    });
}
