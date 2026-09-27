using System.Security.Cryptography;
using System.Text;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

/// <summary>Forks held in memory, so sessions can be tested without downloading anything.</summary>
public sealed class MemoryForkStore : IForkStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _files = [];
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _commits = [];

    public string Directory => "memory";

    /// <summary>Puts a file in a fork, as if it were in the fork's newest commit.</summary>
    public MemoryForkStore Add(string forkId, string path, string text) => Add(forkId, path, Encoding.UTF8.GetBytes(text));

    public MemoryForkStore Add(string forkId, string path, byte[] data)
    {
        var id = Convert.ToHexStringLower(SHA1.HashData(data));
        _objects[id] = data;
        if (!_files.TryGetValue(forkId, out var files))
            _files[forkId] = files = new(StringComparer.Ordinal);
        files[path] = id;
        return this;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    // A commit id as long as a real one, made from the fork's id.
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) =>
        Task.FromResult(_commits[fork.Id] = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(fork.Id))));

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) => Task.FromResult(_commits[fork.Id] = commit);

    public Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) => Task.FromResult(_commits.GetValueOrDefault(forkId));

    public Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default)
    {
        if (previousCommit == null)
            _commits.Remove(forkId);
        else
            _commits[forkId] = previousCommit;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_commits));

    public Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default)
    {
        var prefixes = folders.Select(f => f.TrimEnd('/') + "/").ToList();
        IReadOnlyList<StoreEntry> entries = _files.GetValueOrDefault(forkId, [])
            .Where(f => prefixes.Any(p => f.Key.StartsWith(p, StringComparison.Ordinal)))
            .Select(f => new StoreEntry(f.Value, f.Key))
            .ToList();
        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(objectIds.Where(id => !_objects.ContainsKey(id)).ToList());

    public Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default) => Task.FromResult(0);

    private int _readersOpened;

    /// <summary>Which reader (counting from 0) to hand out already stopped, as a reader process that has died reads.</summary>
    public int? DeadReader { get; set; }

    public IBlobReader OpenReader() => _readersOpened++ == DeadReader ? new StoppedReader() : new Reader(_objects);

    private sealed class StoppedReader : IBlobReader
    {
        public Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default) => throw new GitException("git cat-file stopped unexpectedly.");

        public ValueTask DisposeAsync() => throw new IOException("Broken pipe");
    }

    public Task RemoveAsync(string forkId, CancellationToken ct = default)
    {
        _files.Remove(forkId);
        _commits.Remove(forkId);
        return Task.CompletedTask;
    }

    public Task<long> CleanUpAsync(CancellationToken ct = default) => Task.FromResult(0L);

    private sealed class Reader(Dictionary<string, byte[]> objects) : IBlobReader
    {
        public Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default) => Task.FromResult(objects.GetValueOrDefault(objectId));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
