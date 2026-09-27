using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.App.Tests;

/// <summary>Driving the real window with the mouse. Needs a store with Delta-V (PAPERDOLL_STORE).</summary>
public class InteractionTests
{
    [Fact]
    public async Task Dragging_a_marking_onto_another_in_its_layer_moves_it_there()
    {
        var store = Environment.GetEnvironmentVariable("PAPERDOLL_STORE");
        Assert.SkipWhen(store == null, "Set PAPERDOLL_STORE to a store with Delta-V to drive the window.");
        var ct = TestContext.Current.CancellationToken;
        await using var editor = await EditorSession.OpenAsync(store, ct);
        await editor.LoadForkAsync(KnownForks.Find("deltav")!, update: false, ct: ct);
        editor.NewCharacter("Human");
        var torso = editor.Content!.Characters.Species["Human"].Organs.First(o => o.MarkingLayers.Contains("Chest"));
        // Two markings with different names, so each row can be found by its text.
        var two = editor.AvailableMarkings(torso, "Chest").DistinctBy(m => editor.MarkingName(m.Id)).Take(2).Select(m => m.Id).ToList();
        var (first, second) = (two[0], two[1]);
        editor.AddMarking(torso.Category, "Chest", first);
        editor.AddMarking(torso.Category, "Chest", second);

        var session = HeadlessApp.Session;
        await session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Point Where(string id) => window.GetVisualDescendants().OfType<TreeViewItem>()
                .First(i => i.Header is StackPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == editor.MarkingName(id)))
                .TranslatePoint(new Point(40, 8), window)!.Value;

            var from = Where(second);
            var to = Where(first);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Point(0, -3), RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([second, first], editor.Look!.Markings[torso.Category]["Chest"].Select(m => m.Id));
            window.Close();
        }, ct);
    }

    [Fact]
    public async Task Choosing_nothing_empties_an_optional_one_item_loadout_group()
    {
        var store = Environment.GetEnvironmentVariable("PAPERDOLL_STORE");
        Assert.SkipWhen(store == null, "Set PAPERDOLL_STORE to a store with Delta-V to drive the window.");
        var ct = TestContext.Current.CancellationToken;
        await using var editor = await EditorSession.OpenAsync(store, ct);
        await editor.LoadForkAsync(KnownForks.Find("deltav")!, update: false, ct: ct);
        editor.NewCharacter("Human");
        editor.PreviewJob = "Janitor";
        var outfits = editor.Content!.Outfits;
        var group = outfits.RoleLoadouts[Core.Outfits.OutfitCatalog.RoleFor("Janitor")]
            .Select(id => outfits.Groups[id]).First(g => g.MinLimit == 0 && g.MaxLimit == 1 && g.Loadouts.Count > 0);
        editor.ToggleLoadout("Janitor", group.Id, group.Loadouts[0]);
        Assert.NotEmpty(editor.LoadoutFor("Janitor").Groups.First(g => g.Group == group.Id).Loadouts);

        var session = HeadlessApp.Session;
        await session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            window.ShowPart($"Outfit/{group.Id}", 0);
            Dispatcher.UIThread.RunJobs();
            var nothing = window.GetVisualDescendants().OfType<RadioButton>().First(r => r.Content as string == "Nothing");
            nothing.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(editor.LoadoutFor("Janitor").Groups.First(g => g.Group == group.Id).Loadouts);
            window.Close();
        }, ct);
    }
}
