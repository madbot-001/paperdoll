using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>
/// Each fork's files from where the player chose: by default the download store, a game folder
/// on this computer (<see cref="FolderForkStore"/>), or Paperdoll's copy of a build the SS14
/// launcher held (<see cref="LauncherForkStore"/>). Every call for a fork goes to its source,
/// except those given file ids: files from a fork's own source have ids that start with the
/// fork's id and a colon, and a git object id never has a colon, so each id goes to the store it
/// came from, even when the fork's source has changed since it was listed.
/// </summary>
/// <param name="launcherCopies">Where copies of launcher builds are kept, in Paperdoll's store; null when there is no such place, and no fork can come from the launcher.</param>
public sealed class SourcedForkStore(IForkStore downloads, string? launcherCopies = null) : IForkStore
{
    private readonly Lock _lock = new();
    // Each fork not downloaded: a FolderForkStore or a LauncherForkStore.
    private readonly Dictionary<string, IForkStore> _own = new(StringComparer.Ordinal);

    /// <summary>Where downloaded forks are kept.</summary>
    public IForkStore Downloads { get; } = downloads;

    /// <summary>Where copies of launcher builds are kept, or null when no fork can come from the launcher.</summary>
    public LauncherImports? LauncherCopies { get; } = launcherCopies == null ? null : new LauncherImports(launcherCopies);

    /// <summary>The download store's folder: game folders are the player's own, not Paperdoll's.</summary>
    public string Directory => Downloads.Directory;

    /// <summary>
    /// Paperdoll's own folders, which no game folder may be in or hold: the store holding the
    /// launcher copies, and the download store's folder.
    /// </summary>
    public IReadOnlyList<string> OwnFolders =>
    [
        .. LauncherCopies is { } copies && Path.GetDirectoryName(copies.Directory) is { } store ? [store] : Array.Empty<string>(),
        .. Path.IsPathRooted(Downloads.Directory) ? [Downloads.Directory] : Array.Empty<string>(),
    ];

    public ForkSource SourceOf(string forkId)
    {
        lock (_lock)
            return SourceOf(_own.GetValueOrDefault(forkId));
    }

    private static ForkSource SourceOf(IForkStore? own) => own switch
    {
        FolderForkStore folder => ForkSource.FromFolder(folder.Directory),
        LauncherForkStore launcher => ForkSource.FromLauncher(launcher.Launcher),
        _ => ForkSource.Download,
    };

    /// <summary>Every fork that does not come from the download store, with its source.</summary>
    public IReadOnlyDictionary<string, ForkSource> Sources
    {
        get
        {
            lock (_lock)
                return _own.ToDictionary(f => f.Key, f => SourceOf(f.Value), StringComparer.Ordinal);
        }
    }

    /// <summary>The game folder a fork is read from, or null when it is not read from one.</summary>
    public FolderForkStore? FolderOf(string forkId)
    {
        lock (_lock)
            return _own.GetValueOrDefault(forkId) as FolderForkStore;
    }

    /// <summary>The copy of a launcher build a fork is read from, or null when it is not read from one.</summary>
    public LauncherForkStore? LauncherOf(string forkId)
    {
        lock (_lock)
            return _own.GetValueOrDefault(forkId) as LauncherForkStore;
    }

    // The folder store reading a fork's own files: its game folder, or its launcher copy.
    private FolderForkStore? FilesOf(string forkId)
    {
        lock (_lock)
            return _own.GetValueOrDefault(forkId) switch
            {
                FolderForkStore folder => folder,
                LauncherForkStore launcher => launcher.Files,
                _ => null,
            };
    }

    /// <summary>
    /// Reads a fork from a game folder from now on. The folder is read first; when it cannot be
    /// used, <see cref="GameFolderException"/> says why and the fork keeps its source.
    /// </summary>
    public FolderForkStore UseFolder(string forkId, string folder)
    {
        var store = new FolderForkStore(forkId, folder, OwnFolders);
        store.Read();
        SetOwn(forkId, store);
        return store;
    }

    /// <summary>
    /// Makes a game folder or launcher copy already read a fork's source, or the download store
    /// for null; returns the source before, null for the download store.
    /// </summary>
    public IForkStore? SetOwn(string forkId, IForkStore? own)
    {
        if (own is not (null or FolderForkStore or LauncherForkStore))
            throw new ArgumentException("A fork's own source is a game folder or a launcher copy.", nameof(own));
        lock (_lock)
        {
            var before = _own.GetValueOrDefault(forkId);
            if (own == null)
                _own.Remove(forkId);
            else
                _own[forkId] = own;
            return before;
        }
    }

    // Whether an id names a file of a fork's own source rather than a git object.
    private static bool IsOwnSourceId(string objectId) => objectId.Contains(':', StringComparison.Ordinal);

    /// <summary>
    /// Takes a source kept from an earlier run. A game folder or launcher copy that cannot be read
    /// now is kept as the fork's source, with the reason in its <c>Problem</c>. The launcher
    /// itself is not opened: only Paperdoll's copy is read.
    /// </summary>
    public void Restore(string forkId, ForkSource source)
    {
        if (source.Kind == ForkSourceKind.Launcher && LauncherCopies is { } copies && !string.IsNullOrWhiteSpace(source.Launcher))
        {
            LauncherForkStore launcher;
            try
            {
                // A copy left part way when Paperdoll last stopped is tidied up first.
                copies.Recover(forkId);
                launcher = new LauncherForkStore(forkId, copies, source.Launcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return;
            }
            try
            {
                launcher.Read();
            }
            catch (LauncherException)
            {
                // Kept with its problem, so the Forks window can say what to do.
            }
            lock (_lock)
                _own[forkId] = launcher;
            return;
        }
        if (source.Kind != ForkSourceKind.Folder || string.IsNullOrWhiteSpace(source.Folder))
            return;
        var store = new FolderForkStore(forkId, source.Folder, OwnFolders);
        try
        {
            store.Read();
        }
        catch (GameFolderException)
        {
            // Kept with its problem: the folder may be on a drive not plugged in.
        }
        lock (_lock)
            _own[forkId] = store;
    }

    /// <summary>Downloads a fork again rather than reading it from a game folder or launcher copy; a game folder is left as it is.</summary>
    public IForkStore? UseDownload(string forkId) => SetOwn(forkId, null);

    private IForkStore For(string forkId)
    {
        lock (_lock)
            return _own.GetValueOrDefault(forkId) ?? Downloads;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Downloads.InitializeAsync(ct);

    /// <summary>For a game folder, reads it again and picks up the commit it is on now.</summary>
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) => For(fork.Id).SyncAsync(fork, ct);

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) => For(fork.Id).SyncToCommitAsync(fork, commit, ct);

    public Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) => For(forkId).CommitOfAsync(forkId, ct);

    /// <summary>The download store's forks, with those read from game folders or launcher copies at their version instead.</summary>
    public async Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default)
    {
        var commits = new Dictionary<string, string>(await Downloads.CommitsAsync(ct), StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var (forkId, own) in _own)
            {
                commits.Remove(forkId);
                if (((own as FolderForkStore)?.Version ?? (own as LauncherForkStore)?.Version) is { } version)
                    commits[forkId] = version;
            }
        }
        return commits;
    }

    public Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default) =>
        For(forkId).RevertSyncAsync(forkId, previousCommit, ct);

    public Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default) =>
        For(forkId).ListAsync(forkId, folders, ct);

    /// <summary>The download store's files it does not hold; a game folder holds all of its own.</summary>
    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Downloads.MissingAsync(objectIds.Where(id => !IsOwnSourceId(id)), ct);

    /// <summary>Downloads the listed git objects; a game folder's files need no fetching.</summary>
    public async Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default)
    {
        var objects = entries.Where(e => !IsOwnSourceId(e.ObjectId)).ToList();
        return objects.Count == 0 ? 0 : await Downloads.FetchAsync(forkId, objects, ct);
    }

    public IBlobReader OpenReader() => new Reader(this);

    /// <summary>
    /// Forgets a fork. One read from a game folder goes back to the download store, the folder
    /// left as it is; one read from a launcher copy goes back too, the copy deleted and the
    /// launcher left as it is. A download of it from before is removed too.
    /// </summary>
    public async Task RemoveAsync(string forkId, CancellationToken ct = default)
    {
        IForkStore? own;
        lock (_lock)
            own = _own.GetValueOrDefault(forkId);
        // The copy first: one that cannot be deleted leaves the fork as it was.
        if (own is LauncherForkStore launcher)
            await launcher.RemoveAsync(forkId, ct);
        lock (_lock)
            _own.Remove(forkId);
        if (own == null || await Downloads.CommitOfAsync(forkId, ct) != null)
            await Downloads.RemoveAsync(forkId, ct);
    }

    /// <summary>Cleans up the download store; game folders and launcher copies are never touched.</summary>
    public Task<long> CleanUpAsync(CancellationToken ct = default) => Downloads.CleanUpAsync(ct);

    // Reads each file from its fork's source: the own ids of a game folder or launcher copy name the fork.
    private sealed class Reader(SourcedForkStore store) : IBlobReader
    {
        private IBlobReader? _downloads;

        public async Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default)
        {
            var colon = objectId.IndexOf(':');
            if (colon < 0)
                return await (_downloads ??= store.Downloads.OpenReader()).ReadAsync(objectId, ct);
            return store.FilesOf(objectId[..colon]) is { } folder ? await folder.ReadFileAsync(objectId, ct) : null;
        }

        public ValueTask DisposeAsync() => _downloads?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
