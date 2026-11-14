using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>A launcher build that cannot be listed or copied, or a copy of one that cannot be read.</summary>
public sealed class LauncherException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A build of a known fork that the SS14 launcher on this computer holds.</summary>
/// <param name="Launcher">The launcher's folder, holding <c>content.db</c>.</param>
/// <param name="VersionId">The build's row in the launcher's <c>ContentVersion</c> table.</param>
/// <param name="ServerForkId">The fork id the server gave the launcher.</param>
/// <param name="ForkVersion">The version the server gave, usually the commit the build was made from.</param>
/// <param name="Hash">The build's own hash, in lower-case hex.</param>
/// <param name="LastUsed">When the launcher last ran the build (UTC).</param>
public sealed record LauncherBuild(string Launcher, long VersionId, ForkInfo Fork, string ServerForkId, string? ForkVersion, string Hash, DateTime LastUsed)
{
    /// <summary>The commit the build was made from, when the server gave one.</summary>
    public string? Commit => LauncherCache.CommitOf(ForkVersion);

    /// <summary>The version as shown.</summary>
    public string ShortVersion => ForkVersions.Short(ForkVersions.OfLauncher(Commit, Hash, 0));
}

/// <summary>The builds found in the launcher folders, and why a folder could not be read.</summary>
public sealed record LauncherScan(IReadOnlyList<LauncherBuild> Builds, IReadOnlyList<string> Problems);

/// <summary>One verified file of a launcher build: its path below Resources and its bytes.</summary>
public readonly record struct LauncherFile(string Path, byte[] Data);

/// <summary>
/// Reads the builds the SS14 launcher keeps in its <c>content.db</c>, read-only, as
/// <see cref="LauncherForks.OpenReadOnly"/> opens it. Only the tables and columns named below are
/// read, after checking they are there in the shape expected; every file is checked against its
/// BLAKE2b-256 hash once decoded. Everything in the file is taken as untrusted: sizes are capped
/// and paths are checked by <see cref="LauncherPaths"/> before use.
/// </summary>
public static class LauncherCache
{
    public const string VersionTable = "ContentVersion";
    public const string ManifestTable = "ContentManifest";
    public const string ContentTable = "Content";

    /// <summary>Every column read, by table, with the type the launcher declares for it.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string Type)>> Columns =
        new Dictionary<string, IReadOnlyList<(string, string)>>
        {
            [VersionTable] = [("Id", "INTEGER"), ("Hash", "BLOB"), ("ForkId", "TEXT"), ("ForkVersion", "TEXT"), ("LastUsed", "DATE")],
            [ManifestTable] = [("Id", "INTEGER"), ("VersionId", "INTEGER"), ("Path", "TEXT"), ("ContentId", "INTEGER")],
            [ContentTable] = [("Id", "INTEGER"), ("Hash", "BLOB"), ("Size", "INTEGER"), ("Compression", "INTEGER"), ("Data", "BLOB")],
        };

    // Fork ids the launcher records for content that is not a fork's own build.
    private static readonly string[] NotForks = ["AnonymousContentBundle", "custom"];

    /// <summary>The folders under Resources a copy takes: what Paperdoll reads, and nothing else.</summary>
    public static readonly string[] Taken = ["Prototypes/", "Locale/en-US/", "Textures/"];

    /// <summary>The largest file taken, decoded or not; no sprite, prototype or string file comes near it.</summary>
    public const long MaxFileSize = GameFolder.MaxFileSize;

    /// <summary>The most files one build may list in the folders taken.</summary>
    public const int MaxFiles = 500_000;

    /// <summary>The most bytes, decoded, one copy may read.</summary>
    public const long MaxTotalSize = 2L * 1024 * 1024 * 1024;

    /// <summary>What to do when a file does not check out: the launcher may be writing it.</summary>
    public const string TryAgain = "close the SS14 launcher and the game, then try again";

    /// <summary>
    /// The newest build of each fork among <paramref name="forks"/> (the known forks unless given)
    /// in these launcher folders. A folder without <c>content.db</c> adds nothing; one that
    /// cannot be read, or holds tables of another shape, adds a problem. Builds of no fork, of a
    /// fork not among them, still downloading (no hash yet) or listing no files are left out.
    /// </summary>
    public static LauncherScan Scan(IEnumerable<string> folders, IReadOnlyList<ForkInfo>? forks = null)
    {
        forks ??= KnownForks.All;
        var newest = new Dictionary<string, LauncherBuild>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var folder in folders)
        {
            string db;
            try
            {
                db = Path.GetFullPath(Path.Combine(folder, LauncherForks.ContentDb));
                if (!File.Exists(db))
                    continue;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
            try
            {
                foreach (var build in ReadBuilds(folder, db, forks))
                {
                    if (!newest.TryGetValue(build.Fork.Id, out var seen) || (build.LastUsed, build.VersionId).CompareTo((seen.LastUsed, seen.VersionId)) > 0)
                        newest[build.Fork.Id] = build;
                }
            }
            catch (LauncherException e)
            {
                problems.Add(e.Message);
            }
            catch (Exception e) when (e is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"Could not read the SS14 launcher's {db}: {e.Message}");
            }
        }
        return new LauncherScan(newest.Values.OrderByDescending(b => b.LastUsed).ToList(), problems);
    }

    /// <summary>The newest build of one fork, by its server fork ids, in one launcher folder; null when there is none.</summary>
    public static LauncherBuild? Newest(string launcher, ForkInfo fork)
    {
        var scan = Scan([launcher], [fork]);
        if (scan.Builds.Count == 0 && scan.Problems.Count > 0)
            throw new LauncherException(scan.Problems[0]);
        return scan.Builds.FirstOrDefault();
    }

    private static List<LauncherBuild> ReadBuilds(string folder, string db, IReadOnlyList<ForkInfo> forks)
    {
        using var connection = LauncherForks.OpenReadOnly(db);
        CheckSchema(connection, db);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT v.Id, v.Hash, v.ForkId, v.ForkVersion, v.LastUsed FROM {VersionTable} v
            WHERE EXISTS (SELECT 1 FROM {ManifestTable} m WHERE m.VersionId = v.Id)
            """;
        using var reader = command.ExecuteReader();
        var builds = new List<LauncherBuild>();
        while (reader.Read())
        {
            if (reader.GetValue(0) is not long id || reader.GetValue(1) is not byte[] hash || reader.GetValue(2) is not string forkId
                || reader.GetValue(4) is not string used || NotForks.Contains(forkId, StringComparer.Ordinal))
                continue;
            if (hash.Length != 32 || hash.All(b => b == 0) || forkId.Length > 200)
                continue;
            var fork = forks.FirstOrDefault(f => f.ServerForkIds.Contains(forkId, StringComparer.Ordinal));
            if (fork == null || !TryParseTime(used, out var lastUsed))
                continue;
            var version = reader.GetValue(3) as string;
            builds.Add(new LauncherBuild(folder, id, fork, forkId, version is { Length: <= 200 } ? version : null, Convert.ToHexStringLower(hash), lastUsed));
        }
        return builds;
    }

    /// <summary>The launcher writes times with SQLite's datetime('now'): UTC, as text.</summary>
    internal static bool TryParseTime(string text, out DateTime time) =>
        DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);

    /// <summary>A server's version, when it is a 40-character commit id, in lower case.</summary>
    internal static string? CommitOf(string? version) =>
        version is { Length: 40 } && version.All(char.IsAsciiHexDigit) ? version.ToLowerInvariant() : null;

    /// <summary>
    /// Checks the three tables are there with every column read, each declared as the launcher
    /// declares it; throws <see cref="LauncherException"/> otherwise. Other columns are allowed.
    /// </summary>
    internal static void CheckSchema(SqliteConnection connection, string db)
    {
        foreach (var (table, columns) in Columns)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name, type FROM pragma_table_info($table)";
                command.Parameters.AddWithValue("$table", table);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.GetValue(0) is string name && reader.GetValue(1) is string type)
                        found[name] = type;
                }
            }
            var wrong = columns.Where(c => !found.TryGetValue(c.Column, out var type) || !string.Equals(type, c.Type, StringComparison.OrdinalIgnoreCase)).ToList();
            if (wrong.Count > 0)
            {
                throw new LauncherException(found.Count == 0
                    ? $"The SS14 launcher's {db} has no {table} table, so Paperdoll does not know how it keeps its builds. An update to Paperdoll may be needed."
                    : $"The SS14 launcher's {db} keeps its builds in a way Paperdoll does not know ({table} has no {string.Join(", ", wrong.Select(c => $"{c.Column} {c.Type}"))}). An update to Paperdoll may be needed.");
            }
        }
    }

    /// <summary>Opens a launcher's <c>content.db</c> for a copy, checking its tables; throws <see cref="LauncherException"/> when it cannot.</summary>
    internal static SqliteConnection Open(string launcher)
    {
        string db;
        try
        {
            db = Path.GetFullPath(Path.Combine(launcher, LauncherForks.ContentDb));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            throw new LauncherException($"{launcher} is not a folder that can be read: {e.Message}", e);
        }
        if (!File.Exists(db))
            throw new LauncherException($"The SS14 launcher's {db} is not there any more.");
        SqliteConnection? connection = null;
        try
        {
            connection = LauncherForks.OpenReadOnly(db);
            CheckSchema(connection, db);
            return connection;
        }
        catch (SqliteException e)
        {
            connection?.Dispose();
            throw new LauncherException($"Could not read the SS14 launcher's {db}: {e.Message}; {TryAgain}.", e);
        }
        catch
        {
            connection?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether the build is still in the launcher as it was listed: the launcher drops old builds,
    /// and reuses nothing, but a build listed while still downloading has no hash yet.
    /// </summary>
    internal static void CheckStillThere(SqliteConnection connection, LauncherBuild build)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Hash FROM {VersionTable} WHERE Id = $id";
        command.Parameters.AddWithValue("$id", build.VersionId);
        if (command.ExecuteScalar() is not byte[] hash || Convert.ToHexStringLower(hash) != build.Hash)
            throw new LauncherException($"The SS14 launcher no longer holds that build of {build.Fork.Name}; it drops old builds. Choose again.");
    }

    // The files under the folders taken, compared byte by byte so case counts.
    private static readonly string TakenFilter = string.Join(" OR ",
        Taken.Select(t => $"substr(m.Path, 1, {t.Length}) = '{t}'"));

    /// <summary>How many files of the build are in the folders taken.</summary>
    internal static long CountFiles(SqliteConnection connection, LauncherBuild build)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {ManifestTable} m WHERE m.VersionId = $id AND ({TakenFilter})";
        command.Parameters.AddWithValue("$id", build.VersionId);
        return command.ExecuteScalar() is long count ? count : 0;
    }

    /// <summary>
    /// The build's files in the folders taken, each decoded and checked against its hash. Throws
    /// <see cref="LauncherException"/> for a file that is larger than allowed, does not decode to
    /// its size or does not match its hash, or when the build lists too much.
    /// </summary>
    internal static IEnumerable<LauncherFile> ReadFiles(SqliteConnection connection, LauncherBuild build, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.Path, c.Hash, c.Size, c.Compression, length(c.Data), c.Data
            FROM {ManifestTable} m JOIN {ContentTable} c ON c.Id = m.ContentId
            WHERE m.VersionId = $id AND ({TakenFilter})
            """;
        command.Parameters.AddWithValue("$id", build.VersionId);
        using var reader = command.ExecuteReader();
        var count = 0;
        long total = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (++count > MaxFiles)
                throw new LauncherException($"The launcher's build of {build.Fork.Name} lists more than {MaxFiles} files, far more than any fork has.");
            if (reader.GetValue(0) is not string path || reader.GetValue(1) is not byte[] hash || reader.GetValue(2) is not long size
                || reader.GetValue(3) is not long compression || reader.GetValue(4) is not long stored)
                throw new LauncherException($"A file in the launcher's build of {build.Fork.Name} is not recorded as expected; {TryAgain}.");
            if (size < 0 || size > MaxFileSize || stored < 0 || stored > MaxFileSize + 1024 * 1024)
                throw new LauncherException($"{Shown(path)} in the launcher's build of {build.Fork.Name} is larger than {MaxFileSize / (1024 * 1024)} MB, far larger than any game file Paperdoll reads.");
            total += size;
            if (total > MaxTotalSize)
                throw new LauncherException($"The launcher's build of {build.Fork.Name} holds more than {MaxTotalSize / (1024 * 1024 * 1024)} GB in the folders Paperdoll reads, far more than any fork.");
            var data = reader.GetValue(5) as byte[] ?? [];
            yield return new LauncherFile(path, Decode(path, hash, size, compression, data));
        }
    }

    /// <summary>
    /// A file's bytes, decompressed as the launcher stored them (0 none, 1 deflate, 2 zstd) and
    /// checked against its size and BLAKE2b-256 hash. Decoding stops one byte past the size, so a
    /// file that would decode to more is never held in full.
    /// </summary>
    public static byte[] Decode(string path, byte[] hash, long size, long compression, byte[] data)
    {
        if (size < 0 || size > MaxFileSize)
            throw new LauncherException($"{Shown(path)} in the SS14 launcher's files is larger than {MaxFileSize / (1024 * 1024)} MB, far larger than any game file Paperdoll reads.");
        byte[] bytes;
        try
        {
            bytes = compression switch
            {
                0 => data,
                1 => ReadExactly(new DeflateStream(new MemoryStream(data), CompressionMode.Decompress), size),
                2 => ReadExactly(Zstd(data), size),
                _ => throw new LauncherException($"{Shown(path)} in the SS14 launcher's files is stored in a way Paperdoll does not know (compression {compression}). An update to Paperdoll may be needed."),
            };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ZstdSharp.ZstdException)
        {
            throw new LauncherException($"{Shown(path)} in the SS14 launcher's files could not be decompressed: {e.Message}; {TryAgain}.", e);
        }
        if (bytes.LongLength != size)
            throw new LauncherException($"{Shown(path)} in the SS14 launcher's files is not the size recorded for it; {TryAgain}.");
        if (hash.Length != 32 || !Blake2Fast.Blake2b.ComputeHash(32, bytes).AsSpan().SequenceEqual(hash))
            throw new LauncherException($"{Shown(path)} in the SS14 launcher's files does not match its checksum; {TryAgain}.");
        return bytes;
    }

    private static Stream Zstd(byte[] data)
    {
        var stream = new ZstdSharp.DecompressionStream(new MemoryStream(data));
        // No window larger than the largest file allowed: a frame asking for more is refused
        // rather than given that much memory.
        stream.SetParameter(ZstdSharp.Unsafe.ZSTD_dParameter.ZSTD_d_windowLogMax, 26);
        return stream;
    }

    // Reads up to size + 1 bytes: one more than the size shows the data decodes to more.
    private static byte[] ReadExactly(Stream stream, long size)
    {
        using (stream)
        {
            var buffer = new byte[size + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read == buffer.Length ? throw new InvalidDataException("it decodes to more than its recorded size") : buffer[..read];
        }
    }

    // A path from the launcher as shown in a message: printable, and not too long.
    internal static string Shown(string path)
    {
        var printable = new string(path.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return printable.Length > 200 ? printable[..200] + "..." : printable;
    }
}
