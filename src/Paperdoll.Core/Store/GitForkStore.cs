using System.Text;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>
/// One bare git repository holding every chosen fork, with a remote per fork. Each fork is
/// fetched as a single commit with its folder listings but no file contents; the files Paperdoll
/// needs are then fetched in one request. Files that forks share are stored once.
/// </summary>
public sealed class GitForkStore : IForkStore
{
    private readonly GitCommand _git;

    public GitForkStore(string directory, string gitPath = "git")
    {
        Directory = directory;
        _git = new GitCommand(gitPath, directory);
    }

    public string Directory { get; }

    /// <summary>The ref that holds a fork's synced commit.</summary>
    public static string RefFor(string forkId) => $"refs/paperdoll/{forkId}";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (File.Exists(Path.Combine(Directory, "HEAD")))
            return;

        System.IO.Directory.CreateDirectory(Directory);
        await _git.RunAsync(["init", "--quiet", "--bare"], ct: ct);
    }

    /// <summary>
    /// Fetches the newest commit of the fork's branch, with listings but without file contents,
    /// and returns its id.
    /// </summary>
    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) =>
        SyncAsync(fork, $"https://github.com/{fork.Repository}.git", ct);

    /// <inheritdoc cref="SyncAsync(ForkInfo, CancellationToken)"/>
    /// <param name="url">Where to fetch from, in place of the fork's GitHub repository.</param>
    public Task<string> SyncAsync(ForkInfo fork, string url, CancellationToken ct = default) =>
        FetchRefAsync(fork, url, $"refs/heads/{fork.Branch}", ct);

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) =>
        SyncToCommitAsync(fork, $"https://github.com/{fork.Repository}.git", commit, ct);

    /// <param name="url">Where to fetch from, in place of the fork's GitHub repository.</param>
    public Task<string> SyncToCommitAsync(ForkInfo fork, string url, string commit, CancellationToken ct = default) =>
        FetchRefAsync(fork, url, commit, ct);

    // Fetches one commit, named by branch or by id, without its history or file contents.
    private async Task<string> FetchRefAsync(ForkInfo fork, string url, string source, CancellationToken ct)
    {
        var remotes = (await _git.RunTextAsync(["remote"], ct: ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (remotes.Contains(fork.Id))
            await _git.RunAsync(["remote", "set-url", fork.Id, url], ct: ct);
        else
            await _git.RunAsync(["remote", "add", fork.Id, url], ct: ct);

        await _git.RunAsync(["config", $"remote.{fork.Id}.promisor", "true"], ct: ct);
        await _git.RunAsync(["config", $"remote.{fork.Id}.partialclonefilter", "blob:none"], ct: ct);

        await _git.RunAsync(
            ["fetch", "--quiet", "--no-tags", "--depth", "1", "--filter=blob:none", fork.Id,
             $"+{source}:{RefFor(fork.Id)}"],
            ct: ct);

        return (await _git.RunTextAsync(["rev-parse", RefFor(fork.Id)], ct: ct)).Trim();
    }

    public async Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default)
    {
        try
        {
            return (await _git.RunTextAsync(["rev-parse", "--verify", "--quiet", RefFor(forkId)], ct: ct)).Trim();
        }
        catch (GitException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default)
    {
        var text = await _git.RunTextAsync(["for-each-ref", "--format=%(refname:lstrip=2) %(objectname)", "refs/paperdoll/"], ct: ct);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    /// <summary>Reads listings only; needs no network.</summary>
    public async Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default)
    {
        var output = await _git.RunAsync(
            ["ls-tree", "-r", "-z", "--format=%(objectname) %(path)", RefFor(forkId), "--", .. folders],
            ct: ct);

        var entries = new List<StoreEntry>();
        foreach (var record in Encoding.UTF8.GetString(output).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = record.IndexOf(' ');
            entries.Add(new StoreEntry(record[..space], record[(space + 1)..]));
        }
        return entries;
    }

    public async Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default)
    {
        var unique = objectIds.Distinct(StringComparer.Ordinal).ToList();
        if (unique.Count == 0)
            return [];

        var output = await _git.RunTextAsync(
            ["cat-file", "--batch-check=%(objectname)"], string.Join('\n', unique) + "\n", ct);

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith(" missing", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(' ')])
            .ToList();
    }

    /// <summary>Fetches all missing files from the fork's remote in one request.</summary>
    public async Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default)
    {
        var missing = await MissingAsync(entries.Select(e => e.ObjectId), ct);
        if (missing.Count == 0)
            return 0;

        // The same request git makes for a partial clone's missing files, for all of them at once.
        await _git.RunAsync(
            ["-c", "fetch.negotiationAlgorithm=noop", "fetch", "--quiet", "--no-tags", "--no-write-fetch-head",
             "--recurse-submodules=no", "--filter=blob:none", "--stdin", forkId],
            string.Join('\n', missing) + "\n", ct);

        return missing.Count;
    }

    public IBlobReader OpenReader() => new BlobReader(_git);

    public async Task RemoveAsync(string forkId, CancellationToken ct = default)
    {
        if (await CommitOfAsync(forkId, ct) != null)
            await _git.RunAsync(["update-ref", "-d", RefFor(forkId)], ct: ct);

        var remotes = (await _git.RunTextAsync(["remote"], ct: ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (remotes.Contains(forkId))
            await _git.RunAsync(["remote", "remove", forkId], ct: ct);

        await _git.RunAsync(["gc", "--quiet", "--prune=now"], ct: ct);
    }

    // Updating a fork leaves its old commit and files behind. Everything fetched here sits in
    // promisor packs, which gc keeps whether or not anything uses them, so repack by hand: pack
    // what the current refs reach into one new promisor pack, then drop the old packs.
    public async Task<long> CleanUpAsync(CancellationToken ct = default)
    {
        var before = StoreSize.Of(Directory);
        await _git.RunAsync(["reflog", "expire", "--expire=now", "--all"], ct: ct);

        var reachable = await _git.RunTextAsync(["rev-list", "--objects", "--all", "--missing=allow-promisor"], ct: ct);
        if (reachable.Length == 0)
            return 0;
        var packDir = Path.Combine(Directory, "objects", "pack");
        var name = (await _git.RunTextAsync(["pack-objects", "--quiet", Path.Combine(packDir, "pack")], reachable, ct)).Trim();
        if (name.Length == 0 || !File.Exists(Path.Combine(packDir, $"pack-{name}.pack")))
            throw new InvalidOperationException("git did not write the new pack; nothing was removed.");
        await File.WriteAllTextAsync(Path.Combine(packDir, $"pack-{name}.promisor"), "", ct);

        foreach (var file in System.IO.Directory.EnumerateFiles(packDir, "pack-*").ToList())
        {
            if (!Path.GetFileName(file).StartsWith($"pack-{name}.", StringComparison.Ordinal))
                File.Delete(file);
        }
        // Indexes over the old packs would now point at missing files.
        foreach (var stale in new[] { Path.Combine(packDir, "multi-pack-index"), Path.Combine(Directory, "objects", "info", "commit-graph") })
            File.Delete(stale);
        if (System.IO.Directory.Exists(Path.Combine(Directory, "objects", "info", "commit-graphs")))
            System.IO.Directory.Delete(Path.Combine(Directory, "objects", "info", "commit-graphs"), recursive: true);
        await _git.RunAsync(["prune", "--expire=now"], ct: ct);

        return Math.Max(0, before - StoreSize.Of(Directory));
    }
}
