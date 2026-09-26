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

        // Not disposed, as in the screenshot test: disposing it never returns once a window has run.
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
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
}
