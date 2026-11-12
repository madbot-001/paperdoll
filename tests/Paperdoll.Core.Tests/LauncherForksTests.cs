using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Tests;

public sealed class LauncherForksTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-launcher-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>A made-up launcher folder whose content.db holds these rows, in WAL mode as the launcher keeps it.</summary>
    private string Launcher(string name, params (string? Fork, string LastUsed)[] rows)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        using (var connection = Connect(Path.Combine(folder, "content.db")))
        {
            Execute(connection, "PRAGMA journal_mode=WAL");
            Execute(connection, """
                CREATE TABLE ContentVersion(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Hash BLOB NOT NULL,
                    ForkId TEXT NULL,
                    ForkVersion TEXT NULL,
                    LastUsed DATE NOT NULL,
                    ZipHash BLOB NULL)
                """);
            foreach (var (fork, used) in rows)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO ContentVersion(Hash, ForkId, ForkVersion, LastUsed) VALUES (zeroblob(32), $fork, '1.0', $used)";
                command.Parameters.AddWithValue("$fork", (object?)fork ?? DBNull.Value);
                command.Parameters.AddWithValue("$used", used);
                command.ExecuteNonQuery();
            }
        }
        return folder;
    }

    private static DateTime Utc(int month, int day, int hour = 0) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Each_fork_gets_its_latest_use_most_recent_first()
    {
        var folder = Launcher("one",
            ("starlight", "2026-10-01 05:00:00"),
            ("delta-v", "2026-11-12 05:34:25"),
            ("starlight", "2026-11-03 10:00:00"),
            ("HardLight", "2026-09-01 00:00:00"));

        var forks = LauncherForks.Read([folder]);

        Assert.Equal(
            [new("delta-v", new DateTime(2026, 11, 12, 5, 34, 25, DateTimeKind.Utc)), new("starlight", Utc(11, 3, 10)), new("HardLight", Utc(9, 1))],
            forks);
        Assert.All(forks, f => Assert.Equal(DateTimeKind.Utc, f.LastUsed.Kind));
    }

    [Fact]
    public void A_folder_name_with_percent_signs_spaces_and_hashes_is_read_as_named()
    {
        // SQLite would read "%41" in an address as "A": the folder beside it must not be read instead.
        var named = Launcher("odd%41 #name%00", ("starlight", "2026-11-03 10:00:00"));
        Launcher("oddA #name", ("wizards", "2026-11-04 10:00:00"));

        Assert.Equal(["starlight"], LauncherForks.Read([named]).Select(f => f.ServerForkId));
    }

    [Fact]
    public void Two_launcher_folders_are_merged_keeping_the_latest_use()
    {
        var native = Launcher("native", ("wizards", "2026-11-01 00:00:00"), ("euphoria", "2026-10-01 00:00:00"));
        var flatpak = Launcher("flatpak", ("wizards", "2026-10-20 00:00:00"), ("euphoria", "2026-11-05 00:00:00"));

        var forks = LauncherForks.Read([native, flatpak]);

        Assert.Equal([new("euphoria", Utc(11, 5)), new("wizards", Utc(11, 1))], forks);
    }

    [Fact]
    public void Rows_without_a_fork_a_readable_date_or_a_real_fork_are_skipped()
    {
        var folder = Launcher("one",
            (null, "2026-11-10 00:00:00"),
            ("AnonymousContentBundle", "2026-11-11 00:00:00"),
            ("custom", "yesterday"),
            ("GoobLRP", "2026-11-02 00:00:00.1234567"),
            ("funkystation", "2026-11-01 00:00:00"));

        var forks = LauncherForks.Read([folder]);

        Assert.Equal(["GoobLRP", "funkystation"], forks.Select(f => f.ServerForkId));
    }

    [Fact]
    public void Missing_folders_and_files_find_nothing()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        Assert.Empty(LauncherForks.Read([Path.Combine(_root, "absent"), empty]));
        Assert.Empty(LauncherForks.Read([]));
    }

    [Fact]
    public void A_file_that_is_not_a_database_finds_nothing_and_the_other_folders_still_count()
    {
        var broken = Directory.CreateDirectory(Path.Combine(_root, "broken")).FullName;
        File.WriteAllText(Path.Combine(broken, "content.db"), "This is not a database, only some text that is long enough to have a header.");
        var good = Launcher("good", ("delta-v", "2026-11-01 00:00:00"));

        Assert.Empty(LauncherForks.Read([broken]));
        Assert.Equal(["delta-v"], LauncherForks.Read([broken, good]).Select(f => f.ServerForkId));
    }

    [Fact]
    public void A_database_without_the_table_finds_nothing()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        using (var connection = Connect(Path.Combine(folder, "content.db")))
            Execute(connection, "CREATE TABLE Other(Id INTEGER PRIMARY KEY)");

        Assert.Empty(LauncherForks.Read([folder]));
    }

    [Fact]
    public void Reading_changes_nothing_and_leaves_no_files_beside_it()
    {
        var folder = Launcher("one", ("delta-v", "2026-11-01 00:00:00"));
        var path = Path.Combine(folder, "content.db");
        // Closing the last connection folds the WAL back in and removes its files, as when the
        // launcher is not running.
        Assert.Equal([path], Directory.GetFiles(folder));
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var written = File.GetLastWriteTimeUtc(path);

        Assert.Single(LauncherForks.Read([folder]));

        Assert.Equal([path], Directory.GetFiles(folder));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_launcher_in_the_middle_of_writing_does_not_stop_the_read()
    {
        var folder = Launcher("one", ("delta-v", "2026-11-01 00:00:00"));
        using var launcher = Connect(Path.Combine(folder, "content.db"));
        Execute(launcher, "BEGIN EXCLUSIVE");
        Execute(launcher, "INSERT INTO ContentVersion(Hash, ForkId, LastUsed) VALUES (zeroblob(32), 'starlight', '2026-11-12 00:00:00')");

        var forks = LauncherForks.Read([folder]);

        Assert.Equal(["delta-v"], forks.Select(f => f.ServerForkId));
        Execute(launcher, "COMMIT");
    }

    [Fact]
    public void The_launchers_other_files_are_not_read()
    {
        var folder = Launcher("one", ("delta-v", "2026-11-01 00:00:00"));
        // Made up, shaped so that reading it by mistake would show.
        var settings = Path.Combine(folder, "settings.db");
        using (var connection = Connect(settings))
        {
            Execute(connection, "CREATE TABLE ContentVersion(ForkId TEXT, LastUsed DATE)");
            Execute(connection, "INSERT INTO ContentVersion VALUES ('from-settings', '2026-11-12 00:00:00')");
        }
        var before = File.ReadAllBytes(settings);

        var forks = LauncherForks.Read([folder]);

        Assert.Equal(["delta-v"], forks.Select(f => f.ServerForkId));
        Assert.Equal(before, File.ReadAllBytes(settings));
    }

    [Fact]
    public void Linux_looks_in_the_native_Flatpak_and_Proton_folders()
    {
        var home = Path.Combine("/home", "player");
        string Expected(params string[] parts) => Path.Combine([home, .. parts, "Space Station 14", "launcher"]);
        string Proton(params string[] steam) => Expected([.. steam, "steamapps", "compatdata", "1482520", "pfx", "drive_c", "users", "steamuser", "AppData", "Local"]);

        Assert.Equal(
            [Expected(".local", "share"), Expected(".var", "app", "com.spacestation14.Launcher", "data"), Proton(".steam", "steam"), Proton(".local", "share", "Steam")],
            LauncherForks.CandidateFolders(OSPlatform.Linux, home, null, ""));
        Assert.Equal(Path.Combine("/data", "Space Station 14", "launcher"),
            LauncherForks.CandidateFolders(OSPlatform.Linux, home, "/data", "")[1]);
        // The usual XDG data folder is listed once.
        Assert.Equal(4, LauncherForks.CandidateFolders(OSPlatform.Linux, home, Path.Combine(home, ".local", "share"), "").Count);
    }

    [Fact]
    public void Windows_and_macOS_look_where_their_launchers_keep_it()
    {
        var local = Path.Combine("C:", "Users", "player", "AppData", "Local");
        var home = Path.Combine("/Users", "player");

        Assert.Equal([Path.Combine(local, "Space Station 14", "launcher")], LauncherForks.CandidateFolders(OSPlatform.Windows, "", null, local));
        Assert.Equal([Path.Combine(home, "Library", "Application Support", "Space Station 14", "launcher")],
            LauncherForks.CandidateFolders(OSPlatform.OSX, home, null, ""));
        Assert.Empty(LauncherForks.CandidateFolders(OSPlatform.Windows, "", null, ""));
        Assert.Empty(LauncherForks.CandidateFolders(OSPlatform.Linux, "", null, ""));
    }
}
