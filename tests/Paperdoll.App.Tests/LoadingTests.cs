using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

public class LoadingTests
{
    [Fact]
    public async Task Shortcut_keys_change_nothing_while_a_fork_loads()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        editor.Edit(f => f.Name = "Kept Person");

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var file = editor.File;
            // As during a load: set by the window itself while one runs.
            typeof(MainWindow).GetField("_loading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);

            window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
            window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(file, editor.File);
            Assert.Equal("Kept Person", editor.File!.Name);
            window.Close();
        }, ct);
    }

    [Fact]
    public async Task A_download_that_timed_out_is_reported_as_failed_not_as_stopped()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = TestForks.Store();
        await using var editor = new Core.Editing.EditorSession(store);
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        // What HttpClient throws when a request runs out of time.
        store.SyncFails = new TaskCanceledException("The request timed out.", new TimeoutException());

        var status = await HeadlessApp.Session.Dispatch(async () =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var load = (Task<bool>)typeof(MainWindow).GetMethod("LoadForkAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [TestForks.B, true])!;
            Assert.False(await load);
            var text = Avalonia.Controls.NameScopeExtensions.Find<Avalonia.Controls.TextBlock>(window, "StatusText")!.Text;
            window.Close();
            return text;
        }, ct);

        Assert.StartsWith("Could not load Fork B", status);
        Assert.Contains("timed out", status);
    }

    [Fact]
    public async Task The_species_table_highlights_the_characters_species_after_it_changes_elsewhere()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        editor.NewCharacter("Human");

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            T Find<T>(string name) where T : class => Avalonia.Controls.NameScopeExtensions.Find<T>(window, name)!;
            var tabs = Find<Avalonia.Controls.TabControl>("BottomTabs");
            var grid = Find<Avalonia.Controls.DataGrid>("SpeciesGrid");
            tabs.SelectedItem = Find<Avalonia.Controls.TabItem>("SpeciesTab");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Human", ((SpeciesRow)grid.SelectedItem!).Id);

            // Changed from another tab, as the inspector or Random would.
            tabs.SelectedItem = Find<Avalonia.Controls.TabItem>("MessagesTab");
            Dispatcher.UIThread.RunJobs();
            Func<Core.Editing.EditorSession, IReadOnlyList<Core.Profiles.RuleFix>> edit = s => s.ChangeSpecies("Lizard");
            typeof(MainWindow).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [edit, false]);
            Assert.Equal("Lizard", editor.Look!.Species);
            tabs.SelectedItem = Find<Avalonia.Controls.TabItem>("SpeciesTab");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Lizard", ((SpeciesRow)grid.SelectedItem!).Id);
            window.Close();
        }, ct);
    }
}
