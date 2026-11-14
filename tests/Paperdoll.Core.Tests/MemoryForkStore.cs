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

    // Files a fork lists that the store does not hold until they are fetched, by object id.
    private readonly Dictionary<string, byte[]> _remote = new(StringComparer.Ordinal);

    /// <summary>Puts a file in a fork that is listed but only held once fetched, as the clothing a fork is loaded without.</summary>
    public MemoryForkStore AddUnfetched(string forkId, string path, string text) => AddUnfetched(forkId, path, Encoding.UTF8.GetBytes(text));

    public MemoryForkStore AddUnfetched(string forkId, string path, byte[] data)
    {
        var id = Convert.ToHexStringLower(SHA1.HashData(data));
        _remote[id] = data;
        if (!_files.TryGetValue(forkId, out var files))
            _files[forkId] = files = new(StringComparer.Ordinal);
        files[path] = id;
        return this;
    }

    /// <summary>Every file of a fork, fetched or not, by path, to lay it out as a game folder.</summary>
    public IReadOnlyDictionary<string, byte[]> FilesOf(string forkId) =>
        _files.GetValueOrDefault(forkId, []).ToDictionary(f => f.Key, f => _objects.GetValueOrDefault(f.Value) ?? _remote[f.Value], StringComparer.Ordinal);

    /// <summary>Each fetch that had something to download, with the paths it downloaded.</summary>
    public List<IReadOnlyList<string>> Fetches { get; } = [];

    /// <summary>What fetching throws (once released, when held), as being offline would.</summary>
    public Exception? FetchFails { get; set; }

    /// <summary>
    /// Holds each fetch with something to download until released, whatever cancels it: as a
    /// download that is writing its files finishes before it stops.
    /// </summary>
    public TaskCompletionSource? FetchHold { get; set; }

    private int _fetching;

    /// <summary>Whether other store work started while a fetch was under way.</summary>
    public bool Overlapped { get; private set; }

    // Notes store work that starts while a fetch is under way.
    private void Starting()
    {
        if (Volatile.Read(ref _fetching) > 0)
            Overlapped = true;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>What downloading a fork throws, as a network failure or a timeout would.</summary>
    public Exception? SyncFails { get; set; }

    /// <summary>Whether a download that fails has already moved the fork, as one stopped part way can.</summary>
    public bool SyncFailsAfterMoving { get; set; }

    // A commit id as long as a real one, made from the fork's id.
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default)
    {
        Starting();
        var commit = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(fork.Id)));
        if (SyncFails is { } failure)
        {
            if (SyncFailsAfterMoving)
                _commits[fork.Id] = commit;
            return Task.FromException<string>(failure);
        }
        return Task.FromResult(_commits[fork.Id] = commit);
    }

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default)
    {
        Starting();
        return Task.FromResult(_commits[fork.Id] = commit);
    }

    /// <summary>Holds asking which commit a fork is on until released or cancelled, so a fork load waits at its start.</summary>
    public TaskCompletionSource? CommitHold { get; set; }

    public async Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default)
    {
        if (CommitHold is { } hold)
            await hold.Task.WaitAsync(ct);
        return _commits.GetValueOrDefault(forkId);
    }

    /// <summary>What undoing a download throws, as a store that cannot write would.</summary>
    public Exception? RevertFails { get; set; }

    public Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default)
    {
        if (RevertFails is { } failure)
            return Task.FromException(failure);
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
        Starting();
        var prefixes = folders.Select(f => f.TrimEnd('/') + "/").ToList();
        IReadOnlyList<StoreEntry> entries = _files.GetValueOrDefault(forkId, [])
            .Where(f => prefixes.Any(p => f.Key.StartsWith(p, StringComparison.Ordinal)))
            .Select(f => new StoreEntry(f.Value, f.Key))
            .ToList();
        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(objectIds.Where(id => !_objects.ContainsKey(id)).ToList());

    public async Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default)
    {
        var missing = entries.Where(e => !_objects.ContainsKey(e.ObjectId)).ToList();
        if (missing.Count == 0)
            return 0;
        Starting();
        Interlocked.Increment(ref _fetching);
        try
        {
            if (FetchHold is { } hold)
                await hold.Task;
            if (FetchFails is { } failure)
                throw failure;
            Fetches.Add(missing.Select(e => e.Path).ToList());
            foreach (var entry in missing)
            {
                if (_remote.TryGetValue(entry.ObjectId, out var data))
                    _objects[entry.ObjectId] = data;
            }
            return missing.Count;
        }
        finally
        {
            Interlocked.Decrement(ref _fetching);
        }
    }

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
        Starting();
        _files.Remove(forkId);
        _commits.Remove(forkId);
        return Task.CompletedTask;
    }

    public Task<long> CleanUpAsync(CancellationToken ct = default)
    {
        Starting();
        return Task.FromResult(0L);
    }

    private sealed class Reader(Dictionary<string, byte[]> objects) : IBlobReader
    {
        public Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default) => Task.FromResult(objects.GetValueOrDefault(objectId));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
