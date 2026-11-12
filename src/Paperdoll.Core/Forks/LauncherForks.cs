using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Paperdoll.Core.Forks;

/// <summary>A fork the SS14 launcher on this computer has run, and when it last ran it (UTC).</summary>
public sealed record LauncherFork(string ServerForkId, DateTime LastUsed);

/// <summary>
/// Which forks the SS14 launcher on this computer has run, and when. Only the launcher's
/// <c>content.db</c> is opened, read-only, and only the <c>ForkId</c> and <c>LastUsed</c> columns
/// of its <c>ContentVersion</c> table are read. Nothing is written, kept or sent anywhere.
/// </summary>
public static class LauncherForks
{
    // What the launcher records for content run without a fork (its Models/Updater.cs); not a fork.
    private const string NoFork = "AnonymousContentBundle";

    /// <summary>
    /// The latest use of each fork found in these launcher folders, most recent first. A folder
    /// without a readable <c>content.db</c> adds nothing.
    /// </summary>
    public static IReadOnlyList<LauncherFork> Read(IEnumerable<string> folders)
    {
        var latest = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            foreach (var (fork, used) in ReadFolder(folder))
            {
                if (!latest.TryGetValue(fork, out var seen) || used > seen)
                    latest[fork] = used;
            }
        }
        return latest.Select(p => new LauncherFork(p.Key, p.Value)).OrderByDescending(f => f.LastUsed).ToList();
    }

    private static List<(string Fork, DateTime Used)> ReadFolder(string folder)
    {
        var rows = new List<(string, DateTime)>();
        try
        {
            var path = Path.GetFullPath(Path.Combine(folder, "content.db"));
            if (!File.Exists(path))
                return rows;
            // immutable=1: SQLite takes no locks and makes no -wal, -shm or -journal file beside
            // it. The launcher keeps this file in WAL mode, where even a read-only connection
            // would otherwise create those files. While the launcher writes, a read may miss its
            // latest changes or fail, which only means nothing is found. No pooling, so the file
            // is let go as soon as it is read. Each part of the path is escaped by hand: SQLite
            // decodes %XX, so a folder name that holds such text would otherwise open another file.
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString);
            var source = new SqliteConnectionStringBuilder
            {
                DataSource = "file://" + (path.StartsWith('/') ? "" : "/") + string.Join('/', parts) + "?immutable=1",
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(source.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ForkId, LastUsed FROM ContentVersion";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // The launcher writes the time with SQLite's datetime('now'): UTC, as text.
                if (reader.GetValue(0) is string fork && fork != NoFork && reader.GetValue(1) is string text
                    && DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var used))
                    rows.Add((fork, used));
            }
            return rows;
        }
        catch (Exception)
        {
            // A missing table, a file that is not a database, one the launcher holds, a native
            // library that will not load: the launcher's file is only a hint, so nothing is found.
            return [];
        }
    }

    /// <summary>Where the launcher may keep <c>content.db</c> on this computer.</summary>
    public static IReadOnlyList<string> CandidateFolders() => CandidateFolders(
        OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Where the launcher may keep <c>content.db</c>, following its LauncherPaths.cs: the local
    /// application data folder on Windows, Application Support on macOS, and on Linux the XDG
    /// data folder, as installed natively, through Flatpak or through Steam's Proton.
    /// </summary>
    public static IReadOnlyList<string> CandidateFolders(OSPlatform system, string home, string? dataHome, string localAppData)
    {
        static string Launcher(params string[] root) => Path.Combine([.. root, "Space Station 14", "launcher"]);
        if (system == OSPlatform.Windows)
            return string.IsNullOrEmpty(localAppData) ? [] : [Launcher(localAppData)];
        if (string.IsNullOrEmpty(home))
            return [];
        if (system == OSPlatform.OSX)
            return [Launcher(home, "Library", "Application Support")];
        List<string> folders = [Launcher(home, ".local", "share")];
        if (!string.IsNullOrEmpty(dataHome))
            folders.Add(Launcher(dataHome));
        folders.Add(Launcher(home, ".var", "app", "com.spacestation14.Launcher", "data"));
        // 1482520 is Space Station 14 on Steam; under Proton it runs as the Windows launcher would.
        foreach (var steam in new[] { Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam") })
            folders.Add(Launcher(steam, "steamapps", "compatdata", "1482520", "pfx", "drive_c", "users", "steamuser", "AppData", "Local"));
        return folders.Distinct().ToList();
    }
}
