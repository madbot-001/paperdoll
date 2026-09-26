using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>A file in a fork's commit: its git object id and its path.</summary>
public readonly record struct StoreEntry(string ObjectId, string Path);

/// <summary>
/// Where Paperdoll keeps the forks' files. Files are named by their git object id, so a file two
/// forks share is kept once, whichever way it was downloaded.
/// </summary>
public interface IForkStore
{
    string Directory { get; }

    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Finds the newest commit of the fork's branch and returns its id.</summary>
    Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default);

    /// <summary>The commit a fork was last synced to, or null if it never was.</summary>
    Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default);

    /// <summary>Every file under the given folders in a synced fork's commit.</summary>
    Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default);

    /// <summary>The object ids from the list that the store does not hold yet.</summary>
    Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default);

    /// <summary>Downloads the listed files the store does not hold yet. Returns how many.</summary>
    Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default);

    /// <summary>Starts a reader for file contents. Dispose it when done.</summary>
    IBlobReader OpenReader();

    /// <summary>Forgets a fork and frees the space of files no other fork uses.</summary>
    Task RemoveAsync(string forkId, CancellationToken ct = default);

    /// <summary>Deletes what older versions of the forks left behind. Returns the bytes freed.</summary>
    Task<long> CleanUpAsync(CancellationToken ct = default);
}

public static class StoreSize
{
    /// <summary>Total size of the files under a folder, 0 if it does not exist.</summary>
    public static long Of(string directory) =>
        System.IO.Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
}

public interface IBlobReader : IAsyncDisposable
{
    /// <summary>The file's bytes, or null when the store does not have it.</summary>
    Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default);
}
