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
    public async Task<string> SyncAsync(ForkInfo fork, string url, CancellationToken ct = default)
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
             $"+refs/heads/{fork.Branch}:{RefFor(fork.Id)}"],
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
}
