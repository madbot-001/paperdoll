using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>
/// One fork read from Paperdoll's own copy of a build the SS14 launcher held (see
/// <see cref="LauncherImport"/>). The copy is laid out as a game folder and read as one, through
/// <see cref="FolderForkStore"/>; the launcher itself is not opened here. Its version is the
/// build's (<see cref="ForkVersions.OfLauncher"/>), which changes only when the build is copied again.
/// </summary>
public sealed class LauncherForkStore : IForkStore
{
    private readonly string _forkId;
    private readonly LauncherImports _imports;

    /// <param name="launcher">The launcher's folder the copy came from, where Update looks for a newer build.</param>
    public LauncherForkStore(string forkId, LauncherImports imports, string launcher)
    {
        _forkId = forkId;
        _imports = imports;
        Launcher = launcher;
        Files = new FolderForkStore(forkId, imports.FolderOf(forkId));
    }

    /// <summary>The launcher's folder the copy came from.</summary>
    public string Launcher { get; }

    /// <summary>The copy, read as a game folder.</summary>
    public FolderForkStore Files { get; }

    /// <summary>The copy's folder, in Paperdoll's store.</summary>
    public string Directory => Files.Directory;

    /// <summary>The version last read, or null when the copy could not be read.</summary>
    public string? Version { get; private set; }

    /// <summary>Where the copy came from, once read.</summary>
    public LauncherRecord? Record { get; private set; }

    /// <summary>Why the copy could not be read when last tried, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Reads the copy; returns its version, or throws <see cref="LauncherException"/> when it cannot be used.</summary>
    public string Read()
    {
        try
        {
            var record = _imports.ReadRecord(_forkId);
            Files.Read();
            (Record, Problem) = (record, null);
            return Version = ForkVersions.OfLauncher(record.Commit, record.Build, record.CopiedAt);
        }
        catch (LauncherException e)
        {
            Problem = e.Message;
            throw;
        }
        catch (GameFolderException e)
        {
            Problem = $"Paperdoll's copy of {_forkId} from the SS14 launcher cannot be read: {e.Message} Choose From launcher... in Fork > Forks... to copy it again.";
            throw new LauncherException(Problem, e);
        }
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Reads the copy again. A newer build is copied by choosing the launcher again (<c>PrepareFromSourceAsync</c>), not here.</summary>
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) => Task.Run(Read, ct);

    /// <summary>
    /// Stays on the copy when it is of that commit, as the server's version and the build's are
    /// the same text; otherwise says why it cannot move.
    /// </summary>
    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default)
    {
        if (Version != null && Record?.ForkVersion is { } version && string.Equals(version, commit, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Version);
        var shown = Version != null ? ForkVersions.Short(Version) : "build";
        return Task.FromException<string>(new LauncherException(
            $"{fork.Name} is a copy of the SS14 launcher's {shown}, not of the server's version {commit[..Math.Min(8, commit.Length)]}. Join that server once with the launcher, then choose Update for {fork.Name} in Fork > Forks... to copy its build; or switch {fork.Name} to Download."));
    }

    public Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) =>
        Task.FromResult(forkId == _forkId ? Version : null);

    public Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(Version is { } version
            ? new Dictionary<string, string> { [_forkId] = version }
            : new Dictionary<string, string>());

    public Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default)
    {
        if (forkId == _forkId && previousCommit != null)
            Version = previousCommit;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default) =>
        Files.ListAsync(forkId, folders, ct);

    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default) => Task.FromResult(0);

    public IBlobReader OpenReader() => Files.OpenReader();

    /// <summary>Deletes Paperdoll's copy; the launcher is left as it is.</summary>
    public Task RemoveAsync(string forkId, CancellationToken ct = default) =>
        forkId == _forkId ? Task.Run(() => _imports.Delete(_forkId), ct) : Task.CompletedTask;

    public Task<long> CleanUpAsync(CancellationToken ct = default) => Task.FromResult(0L);
}
