using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>Checks paths and names taken from the SS14 launcher's files before they are used.</summary>
public static class LauncherPaths
{
    private const int MaxPath = 1024;
    private const int MaxPart = 255;

    // Names Windows keeps for devices, with any extension.
    private static readonly string[] Reserved = ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(0, 10).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" }),
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"];

    /// <summary>
    /// Whether a file or folder name means the same, harmless, thing on every system: not empty,
    /// "." or "..", no separator, colon or character Windows refuses, no control character, not
    /// ending in a dot or space, and not a name Windows keeps for a device.
    /// </summary>
    public static bool IsPlainPart(string part) =>
        part.Length is > 0 and <= MaxPart && part is not ("." or "..")
        && !part.Any(c => c < ' ' || c == 0x7F || c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')
        && !part.EndsWith('.') && !part.EndsWith(' ')
        && !Reserved.Contains(part.Split('.')[0].TrimEnd(' '), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A path below Resources as the launcher records it ("Textures/Mobs/x.rsic"), split into its
    /// parts; throws <see cref="LauncherException"/> for one that is rooted, holds "..", a
    /// backslash, a drive letter or a control character, or names anything not plain.
    /// </summary>
    public static string[] Parts(string path)
    {
        if (path.Length is 0 or > MaxPath || path.StartsWith('/') || path.EndsWith('/'))
            throw new LauncherException($"The launcher's build lists a file named {LauncherCache.Shown(path)}, which is not a path within the game's files; Paperdoll copies nothing from it.");
        var parts = path.Split('/');
        if (!parts.All(IsPlainPart))
            throw new LauncherException($"The launcher's build lists a file named {LauncherCache.Shown(path)}, which is not a path within the game's files; Paperdoll copies nothing from it.");
        return parts;
    }
}

/// <summary>
/// What Paperdoll keeps beside a copy of a launcher build: where it came from, and how many files
/// and bytes the copy holds under Resources. It is written last, so a copy without it, or whose
/// files no longer add up to it, is not whole and is never read.
/// </summary>
/// <param name="Build">The build's own hash, in hex.</param>
/// <param name="CopiedAt">When it was copied, in ticks (UTC).</param>
public sealed record LauncherRecord(string ForkId, string ServerForkId, string? ForkVersion, string Build, DateTime LastUsed, long CopiedAt, int Files, long Bytes)
{
    /// <summary>The file it is kept in, beside Resources.</summary>
    public const string FileName = "launcher-build.json";

    /// <summary>
    /// The file, beside Resources, that says the fork has loaded from the copy; written then,
    /// and outside Resources, so the copy's count and bytes are as recorded either way.
    /// </summary>
    public const string LoadedName = "launcher-loaded";

    public string? Commit => LauncherCache.CommitOf(ForkVersion);
}

/// <summary>
/// The folder in Paperdoll's store holding copies of launcher builds, one folder per fork
/// ("upstream/Resources/..."). A copy is made in a folder of its own beside them, then put in
/// place by renaming, so a copy stopped part way never takes the place of a whole one. A copy is
/// taken down the same way: renamed out of place first, then deleted, so one half deleted is
/// never under a fork's name. Whether a copy is whole is told by its <see cref="LauncherRecord"/>.
/// Nothing is written, moved or deleted outside this folder: every path is checked to be inside
/// it first.
/// </summary>
public sealed class LauncherImports
{
    private const string Staging = ".copying-";
    private const string Previous = ".previous-";
    private const string Deleting = ".deleting-";

    public LauncherImports(string directory) => Directory = Path.GetFullPath(directory);

    /// <summary>The folder, in Paperdoll's own store.</summary>
    public string Directory { get; }

    /// <summary>
    /// The full path of a path given inside the folder; throws <see cref="InvalidOperationException"/>
    /// when it is anywhere else, so nothing outside Paperdoll's store is ever written or deleted.
    /// </summary>
    public string Inside(string path)
    {
        var full = Path.GetFullPath(path);
        if (!GameFolder.IsWithin(full, Directory, GameFolder.IgnoresCase) || Path.TrimEndingDirectorySeparator(full).Length == Path.TrimEndingDirectorySeparator(Directory).Length)
            throw new InvalidOperationException($"{full} is not inside Paperdoll's folder for launcher copies, {Directory}; nothing is written there.");
        return full;
    }

    /// <summary>Where a fork's copy is.</summary>
    public string FolderOf(string forkId) => Inside(Path.Combine(Directory, CheckedId(forkId)));

    private static string CheckedId(string forkId) =>
        LauncherPaths.IsPlainPart(forkId) && !forkId.StartsWith('.') ? forkId : throw new ArgumentException($"{forkId} is not a fork id.", nameof(forkId));

    /// <summary>A new, empty folder to copy a fork's build into, beside the copies.</summary>
    public string NewStaging(string forkId)
    {
        var staging = Inside(Path.Combine(Directory, $"{Staging}{CheckedId(forkId)}-{Guid.NewGuid():N}"));
        System.IO.Directory.CreateDirectory(staging);
        return staging;
    }

    /// <summary>
    /// Writes one new file inside a folder made by <see cref="NewStaging"/>, creating the folders
    /// on its way; a file already there is never overwritten.
    /// </summary>
    public void Write(string staging, IReadOnlyList<string> parts, byte[] data)
    {
        var file = Inside(Path.Combine([staging, .. parts]));
        if (!GameFolder.IsWithin(file, Path.GetFullPath(staging), GameFolder.IgnoresCase))
            throw new InvalidOperationException($"{file} is not inside {staging}.");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(data);
    }

    /// <summary>
    /// Puts a finished copy in place of the fork's copy. Returns where the copy it replaced was
    /// set aside, for <see cref="Undo"/> or <see cref="Discard"/>; null when there was none.
    /// </summary>
    public string? SwapIn(string forkId, string staging)
    {
        var current = FolderOf(forkId);
        staging = Inside(staging);
        string? previous = null;
        if (System.IO.Directory.Exists(current))
        {
            previous = Inside(Path.Combine(Directory, $"{Previous}{forkId}-{Guid.NewGuid():N}"));
            System.IO.Directory.Move(current, previous);
        }
        try
        {
            System.IO.Directory.Move(staging, current);
        }
        catch
        {
            if (previous != null)
                System.IO.Directory.Move(previous, current);
            throw;
        }
        return previous;
    }

    /// <summary>
    /// Takes the copy just put in place out again, and puts back the one it replaced, if any: the
    /// new one is renamed out of place first, then the old one renamed back, then the new one deleted.
    /// </summary>
    public void Undo(string forkId, string? previous)
    {
        var current = FolderOf(forkId);
        var gone = System.IO.Directory.Exists(current) ? MoveAside(current, forkId) : null;
        if (previous != null)
            System.IO.Directory.Move(Inside(previous), current);
        if (gone != null)
            DeleteQuietly(gone);
    }

    /// <summary>Deletes a copy set aside, now that the one replacing it is in use.</summary>
    public void Discard(string? previous)
    {
        if (previous != null)
            TakeDown(Inside(previous), ForkOfSetAside(previous));
    }

    /// <summary>
    /// Deletes a fork's copy, if there is one: renamed out of place first, so it is gone from the
    /// fork's name at once, then deleted. What cannot be deleted now is deleted by <see cref="Recover"/>.
    /// </summary>
    public void Delete(string forkId)
    {
        var current = FolderOf(forkId);
        if (System.IO.Directory.Exists(current))
            TakeDown(current, forkId);
    }

    /// <summary>Deletes a folder made by <see cref="NewStaging"/>; it is never read, so it is deleted where it is.</summary>
    public void DeleteStaging(string staging) => DeleteFolder(Inside(staging));

    // Renames a folder to one being deleted, then deletes it.
    private void TakeDown(string folder, string forkId) => DeleteQuietly(MoveAside(folder, forkId));

    // Renames a folder inside this one out of every fork's way; a rename within one folder happens all at once.
    private string MoveAside(string folder, string forkId)
    {
        var gone = Inside(Path.Combine(Directory, $"{Deleting}{CheckedId(forkId)}-{Guid.NewGuid():N}"));
        System.IO.Directory.Move(Inside(folder), gone);
        return gone;
    }

    // ".previous-upstream-0123..." -> "upstream"
    private string ForkOfSetAside(string previous)
    {
        var name = Path.GetFileName(Inside(previous));
        return name.StartsWith(Previous, StringComparison.Ordinal) && name.Length > Previous.Length + 33
            ? name[Previous.Length..^33]
            : throw new ArgumentException($"{previous} is not a copy set aside.", nameof(previous));
    }

    // Deletes a folder already renamed out of place; what is left is deleted by Recover.
    private void DeleteQuietly(string folder)
    {
        try
        {
            DeleteFolder(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Deletes a folder inside this one; a link in it is removed, never followed.
    private void DeleteFolder(string folder)
    {
        folder = Inside(folder);
        if (System.IO.Directory.Exists(folder))
            System.IO.Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// Tidies up after Paperdoll stopped part way through changing a fork's copy: unfinished
    /// copies and copies being deleted are deleted. When copies set aside are left, the fork's
    /// copy stays if it is whole and has loaded, or if it is whole and none set aside is both;
    /// otherwise the newest whole one set aside takes its place, one that has loaded first, and
    /// the rest are deleted. A copy that is not whole is never put in place.
    /// </summary>
    public void Recover(string forkId)
    {
        if (!System.IO.Directory.Exists(Directory))
            return;
        foreach (var folder in Leftovers(Staging, forkId).Concat(Leftovers(Deleting, forkId)))
            DeleteQuietly(folder);
        var setAside = Leftovers(Previous, forkId).ToList();
        if (setAside.Count == 0)
            return;
        var current = FolderOf(forkId);
        var currentWhole = WholeRecord(current, forkId) != null;
        if (!currentWhole || !HasLoaded(current))
        {
            var best = setAside.Select(f => (Folder: f, Record: WholeRecord(f, forkId)))
                .Where(c => c.Record != null && (!currentWhole || HasLoaded(c.Folder)))
                .OrderByDescending(c => HasLoaded(c.Folder))
                .ThenByDescending(c => c.Record!.CopiedAt)
                .FirstOrDefault();
            if (best.Folder != null)
            {
                if (System.IO.Directory.Exists(current))
                    TakeDown(current, forkId);
                System.IO.Directory.Move(best.Folder, current);
                setAside.Remove(best.Folder);
            }
        }
        foreach (var folder in setAside)
            TakeDown(folder, forkId);
    }

    private static bool HasLoaded(string folder) => File.Exists(Path.Combine(folder, LauncherRecord.LoadedName));

    /// <summary>Notes that the fork has loaded from its copy, once; a note that cannot be written is left out.</summary>
    public void MarkLoaded(string forkId)
    {
        var file = Path.Combine(FolderOf(forkId), LauncherRecord.LoadedName);
        try
        {
            if (!File.Exists(file))
                new FileStream(Inside(file), FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // A folder's record when the folder is a whole copy of the fork's, or null.
    private static LauncherRecord? WholeRecord(string folder, string forkId)
    {
        try
        {
            var record = RecordIn(folder, forkId);
            return record != null && Measure(folder) == (record.Files, record.Bytes) ? record : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How many files a copy holds under Resources, and their bytes in all; null when Resources is
    /// not there or holds a link, which Paperdoll never writes.
    /// </summary>
    internal static (int Files, long Bytes)? Measure(string folder)
    {
        var resources = new DirectoryInfo(Path.Combine(folder, "Resources"));
        if (!resources.Exists || resources.LinkTarget != null)
            return null;
        var (files, bytes) = (0, 0L);
        var pending = new Stack<DirectoryInfo>([resources]);
        while (pending.TryPop(out var at))
        {
            foreach (var item in at.EnumerateFileSystemInfos())
            {
                if (item.LinkTarget != null)
                    return null;
                if (item is DirectoryInfo directory)
                    pending.Push(directory);
                else if (!GameFolder.IsSystemFile(item.Name))
                    (files, bytes) = (files + 1, bytes + ((FileInfo)item).Length);
            }
        }
        return (files, bytes);
    }

    // The record in a folder, or null when there is none or it is not one Paperdoll wrote for the fork.
    private static LauncherRecord? RecordIn(string folder, string forkId)
    {
        var info = new FileInfo(Path.Combine(folder, LauncherRecord.FileName));
        if (!info.Exists || info.Length > 64 * 1024)
            return null;
        return JsonSerializer.Deserialize<LauncherRecord>(File.ReadAllBytes(info.FullName)) is { Build.Length: > 0 } record && record.ForkId == forkId
            ? record
            : null;
    }

    // This fork's folders of one kind, named as NewStaging and SwapIn name them: the kind, the
    // fork's id, "-" and 32 hex digits, so no other fork's are taken.
    private IEnumerable<string> Leftovers(string kind, string forkId) =>
        System.IO.Directory.EnumerateDirectories(Directory, $"{kind}{CheckedId(forkId)}-*")
            .Where(d => Path.GetFileName(d) is var name && name.Length == kind.Length + forkId.Length + 33 && name[^32..].All(char.IsAsciiHexDigit))
            .Select(Inside)
            .ToList();

    /// <summary>
    /// The record kept with a fork's copy, once the copy is found whole: its files under Resources
    /// add up to the record's count and bytes. Throws <see cref="LauncherException"/> saying the
    /// fork must be copied again when the copy is not there or not whole.
    /// </summary>
    public LauncherRecord ReadRecord(string forkId)
    {
        var folder = FolderOf(forkId);
        const string again = "Choose From launcher... in Fork > Forks... to copy it again.";
        try
        {
            if (!System.IO.Directory.Exists(folder))
                throw new LauncherException($"Paperdoll's copy of {forkId} from the SS14 launcher is not there any more. {again}");
            var record = RecordIn(folder, forkId);
            return record != null && Measure(folder) == (record.Files, record.Bytes)
                ? record
                : throw new LauncherException($"Paperdoll's copy of {forkId} from the SS14 launcher is not whole: Paperdoll stopped while changing it, or files in it were changed. {again}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new LauncherException($"Could not read Paperdoll's copy of {forkId} from the SS14 launcher: {e.Message} {again}", e);
        }
    }

    /// <summary>Writes a copy's record, last of all its files, with what the copy holds under Resources.</summary>
    internal void WriteRecord(string staging, LauncherRecord record)
    {
        var (files, bytes) = Measure(Inside(staging)) ?? throw new LauncherException($"The copy in {staging} holds a link, which Paperdoll never writes.");
        Write(staging, [LauncherRecord.FileName], JsonSerializer.SerializeToUtf8Bytes(record with { Files = files, Bytes = bytes }));
    }
}

/// <summary>
/// Copies what Paperdoll reads of one launcher build (Prototypes, Locale/en-US and Textures)
/// out of the launcher's <c>content.db</c> into Paperdoll's store, laid out as a game folder is:
/// each merged prototype and string file split back into the files it was made of, and each
/// packed RSI unpacked into its folder. Every file is checked against its hash first; any
/// problem stops the copy, and nothing of it is kept.
/// </summary>
public static class LauncherImport
{
    /// <summary>
    /// Copies the build into a new folder beside the copies and returns it, to be put in place
    /// with <see cref="LauncherImports.SwapIn"/>. Throws <see cref="LauncherException"/> saying
    /// what is wrong, or <see cref="OperationCanceledException"/> when stopped; either way the
    /// folder is deleted.
    /// </summary>
    public static string Copy(LauncherBuild build, LauncherImports imports, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report($"Opening the SS14 launcher's build of {build.Fork.Name}");
        string staging;
        try
        {
            System.IO.Directory.CreateDirectory(imports.Directory);
            staging = imports.NewStaging(build.Fork.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException($"Could not make a folder in {imports.Directory} for the copy: {e.Message}", e);
        }
        try
        {
            // The launcher's file as it was before the copy, to tell whether it changed during it:
            // read without locks, a file being written can give rows that are short or wrong. A
            // write in place that keeps the file's size, lands within the same tick of its
            // last-change time and changes neither the rows read nor their count goes unseen;
            // the files it could change are still checked against their hashes.
            var db = Path.GetFullPath(Path.Combine(build.Launcher, LauncherForks.ContentDb));
            var before = Stamp(db);
            long total;
            using (var connection = LauncherCache.Open(build.Launcher))
            {
                LauncherCache.CheckStillThere(connection, build);
                total = LauncherCache.CountFiles(connection, build);
                if (total == 0)
                    throw new LauncherException($"The launcher's build of {build.Fork.Name} holds none of the files Paperdoll reads (Prototypes, Locale/en-US and Textures).");
                progress?.Report($"Copying {build.Fork.Name} from the SS14 launcher: 0 of {total.ToString("N0", CultureInfo.CurrentCulture)} files");
                var done = 0;
                long written = 0;
                foreach (var file in LauncherCache.ReadFiles(connection, build, ct))
                {
                    written += Place(file, staging, imports);
                    if (written > LauncherCache.MaxTotalSize)
                        throw new LauncherException($"The launcher's build of {build.Fork.Name} unpacks to more than {LauncherCache.MaxTotalSize / (1024 * 1024 * 1024)} GB, far more than any fork.");
                    if (++done % 200 == 0)
                        progress?.Report($"Copying {build.Fork.Name} from the SS14 launcher: {done.ToString("N0", CultureInfo.CurrentCulture)} of {total.ToString("N0", CultureInfo.CurrentCulture)} files");
                }
                if (done != total)
                    throw Changed(build);
            }
            // Read again from the start: the build and its list of files must be as they were,
            // and the file itself unchanged.
            using (var again = LauncherCache.Open(build.Launcher))
            {
                LauncherCache.CheckStillThere(again, build);
                if (LauncherCache.CountFiles(again, build) != total)
                    throw Changed(build);
            }
            if (Stamp(db) != before)
                throw Changed(build);
            // A game folder has both, whatever the build holds.
            System.IO.Directory.CreateDirectory(Path.Combine(staging, "Resources", "Prototypes"));
            System.IO.Directory.CreateDirectory(Path.Combine(staging, "Resources", "Textures"));
            imports.WriteRecord(staging, new LauncherRecord(build.Fork.Id, build.ServerForkId, build.ForkVersion, build.Hash, build.LastUsed, DateTime.UtcNow.Ticks, 0, 0));
            return staging;
        }
        catch (Exception failure)
        {
            try
            {
                imports.DeleteStaging(staging);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Deleted at the next copy or start, by LauncherImports.Recover.
            }
            if (failure is SqliteException)
                throw new LauncherException($"Could not read the SS14 launcher's build of {build.Fork.Name}: {failure.Message}; {LauncherCache.TryAgain}.", failure);
            if (failure is IOException or UnauthorizedAccessException)
                throw new LauncherException($"Could not write the copy of {build.Fork.Name} in {imports.Directory}: {failure.Message}", failure);
            throw;
        }
    }

    private static LauncherException Changed(LauncherBuild build) =>
        new($"The SS14 launcher's files changed while {build.Fork.Name} was being copied, so the copy may be short or wrong; {LauncherCache.TryAgain}.");

    // A file's size and when it last changed, or null when it is not there.
    private static (long Length, DateTime Changed)? Stamp(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
    }

    // Writes one file of the build where a game folder has it; returns the bytes written.
    private static long Place(LauncherFile file, string staging, LauncherImports imports)
    {
        var parts = LauncherPaths.Parts(file.Path);
        var name = parts[^1];
        var folder = string.Join('/', parts[..^1]);
        long written = 0;
        void Put(IReadOnlyList<string> at, byte[] data)
        {
            string[] inResources = ["Resources", .. at];
            var file = Path.Combine([staging, .. inResources]);
            // Also two names that differ only in case, where the system takes them for one.
            if (File.Exists(file) || System.IO.Directory.Exists(file))
                throw new LauncherException($"The launcher's build holds {LauncherCache.Shown(string.Join('/', at))} twice; Paperdoll copies nothing from it.");
            imports.Write(staging, inResources, data);
            written += data.LongLength;
        }

        if ((parts[0] == "Prototypes" && name == MergedText.MergedName("yml")) || (parts[0] == "Locale" && name == MergedText.MergedName("ftl")))
        {
            IReadOnlyList<(string Path, byte[] Data)> split;
            try
            {
                split = MergedText.Split(folder, name[^3..], file.Data);
            }
            catch (FormatException e)
            {
                throw new LauncherException($"{LauncherCache.Shown(file.Path)} in the launcher's build is not laid out as Paperdoll knows: {e.Message}.", e);
            }
            foreach (var (path, data) in split)
                Put(LauncherPaths.Parts(path), data);
        }
        else if (parts[0] == "Textures" && name.EndsWith(".rsic", StringComparison.Ordinal) && name.Length > ".rsic".Length)
        {
            Rsic.Unpacked rsi;
            try
            {
                rsi = Rsic.Unpack(file.Data);
            }
            catch (FormatException e)
            {
                throw new LauncherException($"{LauncherCache.Shown(file.Path)} in the launcher's build could not be unpacked: {e.Message}.", e);
            }
            string[] rsiFolder = [.. parts[..^1], name[..^1]];
            Put([.. rsiFolder, "meta.json"], rsi.MetaJson);
            foreach (var (state, png) in rsi.States)
            {
                var picture = state + ".png";
                if (!LauncherPaths.IsPlainPart(picture))
                    throw new LauncherException($"{LauncherCache.Shown(file.Path)} in the launcher's build has a state named {LauncherCache.Shown(state)}, which is not a plain file name; Paperdoll copies nothing from it.");
                Put([.. rsiFolder, picture], png);
            }
        }
        else
            Put(parts, file.Data);
        return written;
    }
}
