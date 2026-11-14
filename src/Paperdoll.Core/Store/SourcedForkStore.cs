using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>
/// Each fork's files from where the player chose: by default the download store, or a game
/// folder on this computer (<see cref="FolderForkStore"/>). Every call for a fork goes to its
/// source, except those given file ids: files from a fork's own source have ids that start with
/// the fork's id and a colon, and a git object id never has a colon, so each id goes to the
/// store it came from, even when the fork's source has changed since it was listed.
/// </summary>
public sealed class SourcedForkStore(IForkStore downloads) : IForkStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, FolderForkStore> _folders = new(StringComparer.Ordinal);

    /// <summary>Where downloaded forks are kept.</summary>
    public IForkStore Downloads { get; } = downloads;

    /// <summary>The download store's folder: game folders are the player's own, not Paperdoll's.</summary>
    public string Directory => Downloads.Directory;

    public ForkSource SourceOf(string forkId) =>
        FolderOf(forkId) is { } folder ? ForkSource.FromFolder(folder.Directory) : ForkSource.Download;

    /// <summary>Every fork that does not come from the download store, with its source.</summary>
    public IReadOnlyDictionary<string, ForkSource> Sources
    {
        get
        {
            lock (_lock)
                return _folders.ToDictionary(f => f.Key, f => ForkSource.FromFolder(f.Value.Directory), StringComparer.Ordinal);
        }
    }

    /// <summary>The game folder a fork is read from, or null when it is downloaded.</summary>
    public FolderForkStore? FolderOf(string forkId)
    {
        lock (_lock)
            return _folders.GetValueOrDefault(forkId);
    }

    /// <summary>
    /// Reads a fork from a game folder from now on. The folder is read first; when it cannot be
    /// used, <see cref="GameFolderException"/> says why and the fork keeps its source.
    /// </summary>
    public FolderForkStore UseFolder(string forkId, string folder)
    {
        var store = new FolderForkStore(forkId, folder);
        store.Read();
        SetFolder(forkId, store);
        return store;
    }

    /// <summary>Makes a game folder already read a fork's source, or the download store for null; returns the one before.</summary>
    public FolderForkStore? SetFolder(string forkId, FolderForkStore? folder)
    {
        lock (_lock)
        {
            var before = _folders.GetValueOrDefault(forkId);
            if (folder == null)
                _folders.Remove(forkId);
            else
                _folders[forkId] = folder;
            return before;
        }
    }

    // Whether an id names a file of a fork's own source rather than a git object.
    private static bool IsOwnSourceId(string objectId) => objectId.Contains(':', StringComparison.Ordinal);

    /// <summary>
    /// Takes a source kept from an earlier run. A game folder that cannot be read now is kept as
    /// the fork's source, with the reason in its <see cref="FolderForkStore.Problem"/>.
    /// </summary>
    public void Restore(string forkId, ForkSource source)
    {
        if (source.Kind != ForkSourceKind.Folder || string.IsNullOrWhiteSpace(source.Folder))
            return;
        var store = new FolderForkStore(forkId, source.Folder);
        try
        {
            store.Read();
        }
        catch (GameFolderException)
        {
            // Kept with its problem: the folder may be on a drive not plugged in.
        }
        lock (_lock)
            _folders[forkId] = store;
    }

    /// <summary>Downloads a fork again rather than reading it from a game folder; the folder is left as it is.</summary>
    public void UseDownload(string forkId) => SetFolder(forkId, null);

    private IForkStore For(string forkId) => FolderOf(forkId) ?? Downloads;

    public Task InitializeAsync(CancellationToken ct = default) => Downloads.InitializeAsync(ct);

    /// <summary>For a game folder, reads it again and picks up the commit it is on now.</summary>
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) => For(fork.Id).SyncAsync(fork, ct);

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) => For(fork.Id).SyncToCommitAsync(fork, commit, ct);

    public Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) => For(forkId).CommitOfAsync(forkId, ct);

    /// <summary>The download store's forks, with those read from game folders at their folder's version instead.</summary>
    public async Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default)
    {
        var commits = new Dictionary<string, string>(await Downloads.CommitsAsync(ct), StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var (forkId, folder) in _folders)
            {
                commits.Remove(forkId);
                if (folder.Version is { } version)
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
    /// left as it is; a download of it from before is removed too.
    /// </summary>
    public async Task RemoveAsync(string forkId, CancellationToken ct = default)
    {
        bool fromFolder;
        lock (_lock)
            fromFolder = _folders.Remove(forkId);
        if (!fromFolder || await Downloads.CommitOfAsync(forkId, ct) != null)
            await Downloads.RemoveAsync(forkId, ct);
    }

    /// <summary>Cleans up the download store; game folders are never touched.</summary>
    public Task<long> CleanUpAsync(CancellationToken ct = default) => Downloads.CleanUpAsync(ct);

    // Reads each file from its fork's source: a game folder's own ids name the fork.
    private sealed class Reader(SourcedForkStore store) : IBlobReader
    {
        private IBlobReader? _downloads;

        public async Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default)
        {
            var colon = objectId.IndexOf(':');
            if (colon < 0)
                return await (_downloads ??= store.Downloads.OpenReader()).ReadAsync(objectId, ct);
            return store.FolderOf(objectId[..colon]) is { } folder ? await folder.ReadFileAsync(objectId, ct) : null;
        }

        public ValueTask DisposeAsync() => _downloads?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
