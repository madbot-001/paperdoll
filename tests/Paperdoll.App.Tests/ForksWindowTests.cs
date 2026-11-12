using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
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

    private static async Task<Shown> OpenAsync(IReadOnlyList<string>? folders)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        return await HeadlessApp.Session.Dispatch(async () =>
        {
            var window = new ForksWindow(editor, launcherFolders: folders) { Width = 720, Height = 420 };
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
}
