using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

public sealed class ForksWindowTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-launcher-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A made-up launcher folder whose content.db says these forks were run then (UTC).</summary>
    private string Launcher(params (string Fork, string LastUsed)[] rows)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "content.db"), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ContentVersion(Id INTEGER PRIMARY KEY AUTOINCREMENT, Hash BLOB NOT NULL, ForkId TEXT NULL, ForkVersion TEXT NULL, LastUsed DATE NOT NULL, ZipHash BLOB NULL)";
        command.ExecuteNonQuery();
        foreach (var (fork, used) in rows)
        {
            command.CommandText = $"INSERT INTO ContentVersion(Hash, ForkId, LastUsed) VALUES (zeroblob(32), '{fork}', '{used}')";
            command.ExecuteNonQuery();
        }
        return folder;
    }

    private static string Day(int month, int day) =>
        new DateTime(2026, month, day, 12, 0, 0, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Shown(List<ForkRow> Rows, List<string?> Cells, bool LineShown, string? Line);

    private static async Task<Shown> OpenAsync(IReadOnlyList<string>? folders, bool showAdult = true)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        return await HeadlessApp.Session.Dispatch(async () =>
        {
            var window = new ForksWindow(editor, launcherFolders: folders, showAdult: showAdult) { Width = 720, Height = 420 };
            window.Show();
            await (Task)typeof(ForksWindow).GetMethod("RefreshAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [])!;
            Dispatcher.UIThread.RunJobs();
            var grid = NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!;
            var line = NameScopeExtensions.Find<TextBlock>(window, "LauncherText")!;
            var shown = new Shown(grid.ItemsSource!.Cast<ForkRow>().ToList(),
                grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList(), line.IsVisible, line.Text);
            window.Close();
            return shown;
        }, ct);
    }

    [Fact]
    public async Task Forks_the_launcher_ran_show_when_and_come_first_most_recent_first()
    {
        var plain = await OpenAsync(null);
        var launcher = Launcher(("starlight", "2026-11-03 12:00:00"), ("delta-v", "2026-11-10 12:00:00"),
            ("wizards", "2026-10-01 12:00:00"), ("wizards-testing", "2026-10-05 12:00:00"));

        var shown = await OpenAsync([launcher]);

        Assert.Equal(["deltav", "starlight", "upstream"], shown.Rows.Take(3).Select(r => r.Fork.Id));
        Assert.Equal([Day(11, 10), Day(11, 3), Day(10, 5)], shown.Rows.Take(3).Select(r => r.LastPlayed));
        Assert.Contains(Day(11, 10), shown.Cells);
        Assert.Contains("Last played", shown.Cells);
        // The rest keep the usual order, with no date.
        Assert.Equal(plain.Rows.Select(r => r.Fork.Id).Where(id => id is not ("deltav" or "starlight" or "upstream")),
            shown.Rows.Skip(3).Select(r => r.Fork.Id));
        Assert.All(shown.Rows.Skip(3), r => Assert.Equal("", r.LastPlayed));
        Assert.False(shown.LineShown);
    }

    [Fact]
    public async Task Launcher_forks_Paperdoll_does_not_know_are_named_under_the_list()
    {
        var launcher = Launcher(("custom", "2026-11-01 12:00:00"), ("HardLight", "2026-11-08 12:00:00"), ("euphoria", "2026-11-02 12:00:00"));

        var shown = await OpenAsync([launcher]);

        Assert.True(shown.LineShown);
        Assert.Equal("Also in your SS14 launcher, not known to Paperdoll: HardLight, custom.", shown.Line);
        Assert.Equal("euphoria", shown.Rows[0].Fork.Id);
    }

    [Fact]
    public async Task Nothing_found_in_the_launcher_leaves_the_list_as_it_was()
    {
        var plain = await OpenAsync(null);
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        var shown = await OpenAsync([Path.Combine(_root, "absent"), empty]);

        Assert.Equal(plain.Rows, shown.Rows);
        Assert.All(shown.Rows, r => Assert.Equal("", r.LastPlayed));
        Assert.False(shown.LineShown);
        Assert.True(plain.Rows.Select(r => r.Editable).SequenceEqual(plain.Rows.Select(r => r.Editable).OrderByDescending(e => e == "Yes")));
    }

    [Fact]
    public async Task Forks_with_18_plus_servers_are_listed_only_when_asked_for_and_are_marked()
    {
        var launcher = Launcher(("euphoria", "2026-11-02 12:00:00"));

        var hidden = await OpenAsync([launcher], showAdult: false);
        var shown = await OpenAsync([launcher]);

        Assert.DoesNotContain(hidden.Rows, r => r.Fork.Adult);
        Assert.Contains(hidden.Rows, r => r.Fork.Id == "deltav");
        Assert.Equal(["euphoria", "floof", "lonestar", "triad", "wayfarer"], shown.Rows.Where(r => r.Fork.Adult).Select(r => r.Fork.Id).Order());
        Assert.Equal("Euphoria (18+)", shown.Rows.Single(r => r.Fork.Id == "euphoria").Name);
        Assert.Equal("Delta-V", shown.Rows.Single(r => r.Fork.Id == "deltav").Name);
        Assert.Equal(shown.Rows.Where(r => !r.Fork.Adult), hidden.Rows);
    }

    [Fact]
    public async Task Ticking_the_18_plus_box_lists_them_and_reports_the_choice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        var (before, after, reported) = await HeadlessApp.Session.Dispatch(async () =>
        {
            var choices = new List<bool>();
            var window = new ForksWindow(editor, adultChanged: choices.Add) { Width = 720, Height = 420 };
            window.Show();
            var refresh = typeof(ForksWindow).GetMethod("RefreshAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)refresh.Invoke(window, [])!;
            var grid = NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!;
            var first = grid.ItemsSource!.Cast<ForkRow>().Count(r => r.Fork.Adult);

            NameScopeExtensions.Find<CheckBox>(window, "AdultBox")!.IsChecked = true;
            await (Task)refresh.Invoke(window, [])!;
            Dispatcher.UIThread.RunJobs();
            var second = grid.ItemsSource!.Cast<ForkRow>().Count(r => r.Fork.Adult);
            window.Close();
            return (first, second, choices);
        }, ct);

        Assert.Equal(0, before);
        Assert.Equal(5, after);
        Assert.Equal([true], reported);
    }

    // A made-up clone of a fork's repository, with its remote.
    private string Clone(string origin)
    {
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName, "b");
        TestGameFolders.Git(folder, origin);
        return folder;
    }

    private static ForkRow Row(ForksWindow window, string forkId)
    {
        Dispatcher.UIThread.RunJobs();
        return NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.ItemsSource!.Cast<ForkRow>().Single(r => r.Fork.Id == forkId);
    }

    private static void Click(ForksWindow window, string button) =>
        NameScopeExtensions.Find<Button>(window, button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string? Status(ForksWindow window) => NameScopeExtensions.Find<TextBlock>(window, "StatusText")!.Text;

    [Fact]
    public async Task Each_row_shows_its_source_and_a_fork_can_be_read_from_a_folder_and_downloaded_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var upstream = Clone("https://github.com/space-wizards/space-station-14");
        var deltav = Clone("git@github.com:DeltaV-Station/Delta-v.git");
        var before = TestGameFolders.Snapshot(deltav);
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await editor.UseFolderAsync(KnownForks.Find("upstream")!, upstream, ct);
        var saves = 0;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new ForksWindow(editor, pickFolder: _ => Task.FromResult<string?>(deltav), sourcesChanged: () => saves++) { Width = 800, Height = 440 };
            window.Show();
            DemoRoomTests.WaitFor(() => NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.ItemsSource != null);
            var grid = NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!;

            Assert.Equal(("Folder", "01234567 (folder)", upstream), (Row(window, "upstream").Source, Row(window, "upstream").Commit, Row(window, "upstream").Folder));
            Assert.Equal(("Download", "", null), (Row(window, "deltav").Source, Row(window, "deltav").Commit, Row(window, "deltav").Folder));
            Assert.Contains("Source", grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("01234567 (folder)", grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

            grid.SelectedItem = Row(window, "upstream");
            Assert.Equal($"Wizard's Den (upstream): read from {upstream}", Status(window));
            Assert.True(NameScopeExtensions.Find<Button>(window, "DownloadButton")!.IsEnabled);
            Assert.Equal("Open", NameScopeExtensions.Find<Button>(window, "OpenButton")!.Content);

            // From folder... reads the fork the folder's remote names, whichever row is chosen.
            Click(window, "FolderButton");
            DemoRoomTests.WaitFor(() => Status(window)?.StartsWith("Delta-V is now", StringComparison.Ordinal) == true);
            Assert.Equal(1, saves);
            Assert.Equal(deltav, Row(window, "deltav").Folder);
            Assert.Equal("deltav", ((ForkRow)grid.SelectedItem!).Fork.Id);
            Assert.Equal($"Delta-V is now read from {deltav}, as it is on disk. Open loads it.", Status(window));

            // Use download goes back, leaving the folder as it is.
            Click(window, "DownloadButton");
            DemoRoomTests.WaitFor(() => Status(window)?.StartsWith("Delta-V is downloaded again", StringComparison.Ordinal) == true);
            Assert.Equal(2, saves);
            Assert.Equal("", Row(window, "deltav").Commit);
            Assert.Equal("Download", NameScopeExtensions.Find<Button>(window, "OpenButton")!.Content);
            Assert.False(NameScopeExtensions.Find<Button>(window, "DownloadButton")!.IsEnabled);
            window.Close();
            return Task.CompletedTask;
        }, ct);
        Assert.Equal(ForkSource.Download, editor.SourceOf("deltav"));
        Assert.Equal(before, TestGameFolders.Snapshot(deltav));
    }

    [Fact]
    public async Task Removing_a_folder_fork_forgets_its_source_and_leaves_the_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        var upstream = Clone("https://github.com/space-wizards/space-station-14.git");
        var before = TestGameFolders.Snapshot(upstream);
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await editor.UseFolderAsync(KnownForks.Find("upstream")!, upstream, ct);
        var saves = 0;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new ForksWindow(editor, sourcesChanged: () => saves++) { Width = 800, Height = 440 };
            window.Show();
            DemoRoomTests.WaitFor(() => NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.ItemsSource != null);
            NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.SelectedItem = Row(window, "upstream");
            Assert.True(NameScopeExtensions.Find<Button>(window, "RemoveButton")!.IsEnabled);

            Click(window, "RemoveButton");
            DemoRoomTests.WaitFor(() => Status(window)?.StartsWith("Removed", StringComparison.Ordinal) == true);
            Assert.Equal("Download", Row(window, "upstream").Source);
            Assert.Equal($"Removed Wizard's Den (upstream); {upstream} was left as it is.", Status(window));
            Assert.Equal(1, saves);
            window.Close();
            return Task.CompletedTask;
        }, ct);
        Assert.Empty(editor.Sources());
        Assert.Equal(before, TestGameFolders.Snapshot(upstream));
    }

    [Fact]
    public async Task A_folder_that_will_not_do_is_explained_and_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        var saves = 0;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new ForksWindow(editor, pickFolder: _ => Task.FromResult<string?>(empty), sourcesChanged: () => saves++) { Width = 800, Height = 440 };
            window.Show();
            DemoRoomTests.WaitFor(() => NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.ItemsSource != null);
            Click(window, "FolderButton");
            DemoRoomTests.WaitFor(() => Status(window)?.Contains("has no Resources", StringComparison.Ordinal) == true);
            Assert.True(window.IsEnabled);
            window.Close();
            return Task.CompletedTask;
        }, ct);
        Assert.Equal(0, saves);
        Assert.Empty(editor.Sources());
    }

    [Fact]
    public async Task Switching_the_open_forks_source_is_left_to_the_load_that_follows()
    {
        var ct = TestContext.Current.CancellationToken;
        var upstream = KnownForks.Find("upstream")!;
        var clone = Clone("https://github.com/space-wizards/space-station-14.git");
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore().Add("upstream", "Resources/Prototypes/body.yml", TestForks.Body)));
        await editor.LoadForkAsync(upstream, update: false, ct: ct);
        var saves = 0;
        var choice = await HeadlessApp.Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            var window = new ForksWindow(editor, pickFolder: _ => Task.FromResult<string?>(clone), sourcesChanged: () => saves++) { Width = 800, Height = 440 };
            var shown = window.ShowDialog<ForkChoice?>(owner);
            DemoRoomTests.WaitFor(() => NameScopeExtensions.Find<DataGrid>(window, "ForkGrid")!.ItemsSource != null);
            Click(window, "FolderButton");
            DemoRoomTests.WaitFor(() => shown.IsCompleted);
            owner.Close();
            return await shown;
        }, ct);

        Assert.Equal(new ForkChoice(upstream, false, ForkSource.FromFolder(clone)), choice);
        // Not yet: only once it has loaded from there.
        Assert.Equal(ForkSource.Download, editor.SourceOf("upstream"));
        Assert.Equal(0, saves);
    }
}
