using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>
/// One fork's files read from a game folder on this computer, as they are on disk. Nothing is
/// downloaded, and nothing in the folder is ever written, created or deleted. No link in it is
/// followed: each one is left out with a note, so every file is listed under its own path and
/// nothing outside the folder is listed or opened. A file's id is the fork's id, its size, when
/// it was last changed and its path, so an edited file gets a new one.
/// </summary>
public sealed class FolderForkStore : IForkStore
{
    private readonly string _forkId;
    private GameFolder? _folder;
    private OpenGate _gate = new();
    private readonly List<string> _skipped = [];
    private readonly Lock _lock = new();

    // Paperdoll's own folders, which a game folder may not be in or hold.
    private readonly IReadOnlyList<string> _ownFolders;

    /// <param name="folder">The folder as chosen: the game's top folder or its Resources folder.</param>
    /// <param name="ownFolders">Paperdoll's own folders (its store), which Paperdoll writes and
    /// deletes in: a game folder inside one, or holding one, is refused, as Paperdoll never writes
    /// in a game folder.</param>
    public FolderForkStore(string forkId, string folder, IReadOnlyList<string>? ownFolders = null)
    {
        _forkId = forkId;
        Directory = folder;
        _ownFolders = ownFolders ?? [];
    }

    /// <summary>The folder as chosen.</summary>
    public string Directory { get; }

    /// <summary>The version last read (see <see cref="ForkVersions"/>), or null when the folder could not be read.</summary>
    public string? Version { get; private set; }

    /// <summary>Why the folder could not be read when last tried, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Files and folders left out of listings since the folder was last read, each with why.</summary>
    public IReadOnlyList<string> Skipped
    {
        get
        {
            lock (_lock)
                return _skipped.ToList();
        }
    }

    // When the folder was last read, kept rising so each reading has a version of its own.
    private long _readAt;

    /// <summary>
    /// Reads the folder again: checks it is still a game folder and finds the commit its checkout
    /// is on. Returns the new version; throws <see cref="GameFolderException"/> when it cannot be used.
    /// </summary>
    public string Read()
    {
        try
        {
            var folder = GameFolder.Open(Directory);
            CheckNotOwn(folder);
            // Reading the folder again gives its files a new count of opens given up on.
            var gate = new OpenGate();
            var git = folder.Top is { } top ? GameFolder.ReadGit(top, gate) : GitCheckout.None;
            var commit = git.Commit;
            _readAt = Math.Max(DateTime.UtcNow.Ticks, _readAt + 1);
            lock (_lock)
                _skipped.Clear();
            if (git.Problem != null)
                Skip($".git: {git.Problem}");
            _gate = gate;
            (_folder, Problem) = (folder, null);
            return Version = ForkVersions.OfFolder(commit, _readAt);
        }
        catch (GameFolderException e)
        {
            Problem = e.Message;
            throw;
        }
        catch (IOException e)
        {
            Problem = $"Could not read {Directory}: {e.Message}";
            throw new GameFolderException(Problem, e);
        }
    }

    // Refuses a game folder inside Paperdoll's own folders, or holding one, compared with every
    // link followed as the file system compares names.
    private void CheckNotOwn(GameFolder folder)
    {
        foreach (var own in _ownFolders)
        {
            var real = GameFolder.RealPath(own);
            string?[] game = [folder.Root, folder.Resources, folder.Top];
            if (game.OfType<string>().Any(g => GameFolder.IsWithin(g, real, GameFolder.IgnoresCase) || GameFolder.IsWithin(real, g, GameFolder.IgnoresCase)))
                throw new GameFolderException($"{Directory} is in Paperdoll's own folder {real}, or holds it, where Paperdoll keeps, replaces and deletes its own files. Choose the game's own folder: a copy or git clone of its repository.");
        }
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) => Task.Run(Read, ct);

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) =>
        Task.FromException<string>(new GameFolderException(
            $"{fork.Name} is read from a game folder as it is on disk, so it cannot be moved to another version. To match a server, switch {fork.Name} back to Download in Fork > Forks...."));

    public Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) =>
        Task.FromResult(forkId == _forkId ? Version : null);

    public Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(Version is { } version
            ? new Dictionary<string, string> { [_forkId] = version }
            : new Dictionary<string, string>());

    /// <summary>Goes back to the version before a reading whose files could not be loaded.</summary>
    public Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default)
    {
        if (forkId == _forkId && previousCommit != null)
            Version = previousCommit;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every file under the given folders ("Resources/Textures/Mobs"), by walking them. A link,
    /// and a folder that cannot be listed, are left out and noted in <see cref="Skipped"/>.
    /// </summary>
    public Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default)
    {
        var folder = _folder;
        if (forkId != _forkId || folder == null)
            return Task.FromResult<IReadOnlyList<StoreEntry>>([]);
        var wanted = folders.ToList();
        return Task.Run<IReadOnlyList<StoreEntry>>(() =>
        {
            var entries = new List<StoreEntry>();
            foreach (var top in wanted)
            {
                if (!TryDiskPath(folder, top, out var disk, out var why))
                {
                    Skip($"{top}: {why}");
                    continue;
                }
                if (System.IO.Directory.Exists(disk))
                    Walk(disk, top.TrimEnd('/'), entries, ct);
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return entries.DistinctBy(e => e.Path).ToList();
        }, ct);
    }

    // With no link followed, a folder is never reached twice and a walk always ends.
    private void Walk(string disk, string path, List<StoreEntry> entries, CancellationToken ct)
    {
        var pending = new Stack<(string Disk, string Path)>();
        pending.Push((disk, path));
        while (pending.TryPop(out var at))
        {
            ct.ThrowIfCancellationRequested();
            List<FileSystemInfo> items;
            try
            {
                items = new DirectoryInfo(at.Disk).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Skip($"{at.Path}: {e.Message}");
                continue;
            }
            foreach (var item in items)
            {
                var itemPath = at.Path + "/" + item.Name;
                // Left out quietly: the operating system's, not the game's.
                if (GameFolder.IsSystemFile(item.Name) && item is FileInfo)
                    continue;
                if (!IsPlainName(item.Name))
                {
                    Skip($"{itemPath}: a name Paperdoll does not read");
                    continue;
                }
                try
                {
                    if (item.LinkTarget != null)
                        Skip($"{itemPath}: a link, and links in a game folder are not followed");
                    else if (item is DirectoryInfo)
                        pending.Push((item.FullName, itemPath));
                    else
                        entries.Add(new StoreEntry($"{_forkId}:{((FileInfo)item).Length}:{item.LastWriteTimeUtc.Ticks}:{itemPath}", itemPath));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Skip($"{itemPath}: {e.Message}");
                }
            }
        }
    }

    private void Skip(string note)
    {
        lock (_lock)
        {
            if (_skipped.Count < 1000)
                _skipped.Add(note);
        }
    }

    // A file or folder name that maps to one path, whatever the system.
    private static bool IsPlainName(string name) =>
        name.Length > 0 && name is not ("." or "..") && name.All(c => c >= ' ' && c is not ('\\' or ':' or '/'));

    /// <summary>
    /// Where a "Resources/..." path is on disk, only if it stays within the Resources folder: no
    /// "..", no rooted path, and no part of it a link.
    /// </summary>
    private static bool TryDiskPath(GameFolder folder, string path, out string disk, out string why)
    {
        disk = "";
        if (!TryParts(path, out var parts))
        {
            why = "not a path within the game folder";
            return false;
        }
        // From the folder as chosen, so Resources and every folder in the path are checked again.
        var fromRoot = folder.FromRoot(parts);
        disk = Path.Combine([folder.Root, .. fromRoot]);
        var at = folder.Root;
        try
        {
            foreach (var part in fromRoot)
            {
                at = Path.Combine(at, part);
                if (GameFolder.IsLink(at))
                {
                    why = "a link, and links in a game folder are not followed";
                    return false;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            why = e.Message;
            return false;
        }
        why = "";
        return true;
    }

    // "Resources/Textures/Mobs" -> ["Textures", "Mobs"], or false for anything else.
    private static bool TryParts(string path, out string[] parts)
    {
        var all = path.TrimEnd('/').Split('/');
        parts = all[1..];
        return all[0] == "Resources" && parts.All(IsPlainName);
    }

    /// <summary>
    /// A listed file's bytes as they are on disk now, or null for an id that is not this fork's.
    /// Throws <see cref="GameFolderException"/> when the file cannot be read: reached through a
    /// link, not a plain file, larger than <see cref="GameFolder.MaxFileSize"/>, or gone.
    /// </summary>
    public Task<byte[]?> ReadFileAsync(string objectId, CancellationToken ct = default)
    {
        var fields = objectId.Split(':', 4);
        if (fields.Length != 4 || fields[0] != _forkId || _folder is not { } folder)
            return Task.FromResult<byte[]?>(null);
        ct.ThrowIfCancellationRequested();
        var path = fields[3];
        if (!TryParts(path, out var parts))
            throw new GameFolderException($"Could not read {path} from the game folder: not a path within it.");
        return GameFolder.ReadPlainFile(folder.Root, folder.FromRoot(parts), GameFolder.MaxFileSize, out var why, _gate, ct) is { } data
            ? Task.FromResult<byte[]?>(data)
            : throw new GameFolderException(why == GameFolder.NotThere
                ? $"Could not read {path} from the game folder: it is not there any more. Update the fork to read the folder again."
                : $"Could not read {path} from the game folder: {why}.");
    }

    /// <summary>The folder holds every file it lists.</summary>
    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default) => Task.FromResult(0);

    public IBlobReader OpenReader() => new Reader(this);

    /// <summary>Nothing to free: the folder is the player's own and is left as it is.</summary>
    public Task RemoveAsync(string forkId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> CleanUpAsync(CancellationToken ct = default) => Task.FromResult(0L);

    private sealed class Reader(FolderForkStore store) : IBlobReader
    {
        public Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default) => store.ReadFileAsync(objectId, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
