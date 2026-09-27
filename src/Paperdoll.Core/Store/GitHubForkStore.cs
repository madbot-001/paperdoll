using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>GitHub refused a request because the hourly limit is used up.</summary>
public sealed class GitHubRateLimitException(DateTimeOffset? resetsAt)
    : Exception(resetsAt is { } at
        ? $"GitHub's limit for downloads without git is used up until {at.ToLocalTime():HH:mm}."
        : "GitHub's limit for downloads without git is used up.")
{
    public DateTimeOffset? ResetsAt { get; } = resetsAt;
}

/// <summary>
/// The backup store for when the git program is missing. Lists folders through the GitHub API
/// and downloads files from raw.githubusercontent.com, keeping each file under its git object id.
/// Unauthenticated, the API allows 60 requests an hour, so folder listings are cached: a listing
/// never changes for a given id.
/// </summary>
/// <remarks>
/// Layout: <c>objects/ab/cdef…</c> file contents; <c>trees/&lt;id&gt;.json</c> cached listings;
/// <c>forks/&lt;fork&gt;.json</c> each fork's repository, commit and the files it uses.
/// </remarks>
public sealed class GitHubForkStore : IForkStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly Uri _api;
    private readonly Uri _raw;

    public GitHubForkStore(string directory, HttpClient? http = null, string? token = null,
        Uri? apiBase = null, Uri? rawBase = null)
    {
        Directory = directory;
        _http = http ?? new HttpClient();
        _token = token;
        _api = apiBase ?? new Uri("https://api.github.com/");
        _raw = rawBase ?? new Uri("https://raw.githubusercontent.com/");
    }

    public string Directory { get; }

    /// <summary>How many files to download at once.</summary>
    public int Parallelism { get; init; } = 8;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        foreach (var sub in new[] { "objects", "trees", "forks" })
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, sub));
        return Task.CompletedTask;
    }

    public Task<string> SyncAsync(ForkInfo fork, CancellationToken ct = default) => SyncToAsync(fork, fork.Branch, ct);

    public Task<string> SyncToCommitAsync(ForkInfo fork, string commit, CancellationToken ct = default) => SyncToAsync(fork, commit, ct);

    // Resolves a branch or a commit id to the full commit id and records it for the fork.
    private async Task<string> SyncToAsync(ForkInfo fork, string branchOrCommit, CancellationToken ct)
    {
        using var request = ApiRequest($"repos/{fork.Repository}/commits/{Uri.EscapeDataString(branchOrCommit)}");
        request.Headers.Accept.ParseAdd("application/vnd.github.sha");
        var commit = (await SendAsync(request, ct)).Trim();

        var record = await LoadForkAsync(fork.Id, ct);
        var pending = File.Exists(PendingPath(fork.Id)) ? ReadJson<ForkRecord>(await File.ReadAllBytesAsync(PendingPath(fork.Id), ct)) : null;
        if (record?.Commit != commit)
        {
            // The old record is kept until the next clean-up, in case the new version fails to load.
            if (record != null)
                SafeFile.WriteAllText(PreviousPath(fork.Id), JsonSerializer.Serialize(record, Json));
            // A new version starts its own lists, so clean-up can tell what the old one used,
            // or the lists of an earlier try at this same version.
            record = pending?.Commit == commit ? pending : (record ?? new ForkRecord(fork.Repository, fork.Branch, commit, [])) with { Objects = [], Trees = [] };
        }
        File.Delete(PendingPath(fork.Id));
        await SaveForkAsync(fork.Id, record with { Repository = fork.Repository, Branch = fork.Branch, Commit = commit }, ct);
        return commit;
    }

    public async Task<string?> CommitOfAsync(string forkId, CancellationToken ct = default) =>
        (await LoadForkAsync(forkId, ct))?.Commit;

    public async Task RevertSyncAsync(string forkId, string? previousCommit, CancellationToken ct = default)
    {
        // An update that found nothing new changed nothing.
        if (await LoadForkAsync(forkId, ct) is not { } current || current.Commit == previousCommit)
            return;
        // What the version that failed fetched is kept for the next try: its listings count
        // against the API's hourly limit.
        SafeFile.WriteAllText(PendingPath(forkId), JsonSerializer.Serialize(current, Json));
        if (previousCommit == null)
        {
            File.Delete(ForkPath(forkId));
            return;
        }
        var previous = File.Exists(PreviousPath(forkId)) ? ReadJson<ForkRecord>(await File.ReadAllBytesAsync(PreviousPath(forkId), ct)) : null;
        await SaveForkAsync(forkId, previous?.Commit == previousCommit ? previous : current with { Commit = previousCommit, Objects = [], Trees = [] }, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> CommitsAsync(CancellationToken ct = default)
    {
        var commits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "forks"), "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (await LoadForkAsync(id, ct) is { } record)
                commits[id] = record.Commit;
        }
        return commits;
    }

    public async Task<IReadOnlyList<StoreEntry>> ListAsync(string forkId, IEnumerable<string> folders, CancellationToken ct = default)
    {
        var fork = await RequireForkAsync(forkId, ct);
        var entries = new List<StoreEntry>();

        foreach (var folder in folders)
        {
            var path = folder.Trim('/');
            var treeId = await FindFolderAsync(fork, path, ct);
            if (treeId != null)
                await ListFolderAsync(fork, treeId, path, entries, ct);
            // Kept as it goes: running out of requests part way must not let a clean-up drop the
            // listings already fetched, which the next try uses.
            await SaveForkAsync(forkId, fork, ct);
        }
        return entries;
    }

    public Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> objectIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(objectIds
            .Distinct(StringComparer.Ordinal)
            .Where(id => !File.Exists(ObjectPath(id)))
            .ToList());

    public async Task<int> FetchAsync(string forkId, IEnumerable<StoreEntry> entries, CancellationToken ct = default)
    {
        var fork = await RequireForkAsync(forkId, ct);
        var list = entries.ToList();
        var missing = await MissingAsync(list.Select(e => e.ObjectId), ct);
        var byId = list.GroupBy(e => e.ObjectId).ToDictionary(g => g.Key, g => g.First().Path);

        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (id, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    new Uri(_raw, $"{fork.Repository}/{fork.Commit}/{EscapePath(byId[id])}"));
                request.Headers.UserAgent.ParseAdd("Paperdoll");
                using var response = await _http.SendAsync(request, token);
                ThrowIfLimited(response);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync(token);

                if (GitBlobId(data) != id)
                    throw new InvalidDataException($"{byId[id]} did not match its git id {id}.");

                var target = ObjectPath(id);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temp, data, token);
                File.Move(temp, target, overwrite: true);
            });

        fork.Objects.UnionWith(list.Select(e => e.ObjectId));
        await SaveForkAsync(forkId, fork, ct);
        return missing.Count;
    }

    public IBlobReader OpenReader() => new FileBlobReader(this);

    public async Task RemoveAsync(string forkId, CancellationToken ct = default)
    {
        File.Delete(ForkPath(forkId));
        File.Delete(PreviousPath(forkId));
        File.Delete(PendingPath(forkId));
        await CleanUpAsync(ct);
    }

    // Keeps only the files and listings some fork's current version uses. A fork synced before
    // listings were tracked keeps every listing.
    public async Task<long> CleanUpAsync(CancellationToken ct = default)
    {
        var before = StoreSize.Of(Directory);
        // Versions kept in case an update failed to load are dropped with the rest.
        foreach (var previous in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "forks"), "*.json.previous"))
            File.Delete(previous);
        var keepObjects = new HashSet<string>(StringComparer.Ordinal);
        var keepTrees = new HashSet<string>(StringComparer.Ordinal);
        var keepAllTrees = false;
        var forks = Path.Combine(Directory, "forks");
        foreach (var file in System.IO.Directory.EnumerateFiles(forks, "*.json").Concat(System.IO.Directory.EnumerateFiles(forks, "*.json.pending")))
        {
            // A record that cannot be read gives no way to tell its files from leftovers, so
            // nothing is deleted.
            if (ReadJson<ForkRecord>(await File.ReadAllBytesAsync(file, ct)) is not { } record)
                return 0;
            keepObjects.UnionWith(record.Objects);
            if (record.Trees == null)
                keepAllTrees = true;
            else
                keepTrees.UnionWith(record.Trees);
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "objects"), "*", SearchOption.AllDirectories))
        {
            var id = Path.GetFileName(Path.GetDirectoryName(file)) + Path.GetFileName(file);
            if (!keepObjects.Contains(id))
                File.Delete(file);
        }
        if (!keepAllTrees)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "trees"), "*.json"))
            {
                if (!keepTrees.Contains(Path.GetFileNameWithoutExtension(file)))
                    File.Delete(file);
            }
        }
        return Math.Max(0, before - StoreSize.Of(Directory));
    }

    /// <summary>The git object id of a file's contents: SHA-1 of "blob &lt;size&gt;\0" and the bytes.</summary>
    public static string GitBlobId(byte[] data)
    {
        var header = Encoding.ASCII.GetBytes($"blob {data.Length}\0");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha.AppendData(header);
        sha.AppendData(data);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private string ObjectPath(string id) => Path.Combine(Directory, "objects", id[..2], id[2..]);
    private string ForkPath(string forkId) => Path.Combine(Directory, "forks", forkId + ".json");

    // The record of the version before the last sync, until the next clean-up.
    private string PreviousPath(string forkId) => Path.Combine(Directory, "forks", forkId + ".json.previous");

    // The record of a version that failed to load, whose files and listings are kept for the next try.
    private string PendingPath(string forkId) => Path.Combine(Directory, "forks", forkId + ".json.pending");

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    // Walks from the commit's root to the folder, one listing per level (cached).
    private async Task<string?> FindFolderAsync(ForkRecord fork, string path, CancellationToken ct)
    {
        var id = fork.Commit;
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var listing = await GetTreeAsync(fork, id, recursive: false, ct);
            var child = listing.Tree.FirstOrDefault(e => e.Type == "tree" && e.Path == name);
            if (child == null)
                return null;
            id = child.Sha;
        }
        return id;
    }

    // Lists a folder in one request, or folder by folder when GitHub cuts the listing short.
    private async Task ListFolderAsync(ForkRecord fork, string treeId, string prefix, List<StoreEntry> entries, CancellationToken ct)
    {
        var listing = await GetTreeAsync(fork, treeId, recursive: true, ct);
        if (!listing.Truncated)
        {
            entries.AddRange(listing.Tree.Where(e => e.Type == "blob").Select(e => new StoreEntry(e.Sha, Join(prefix, e.Path))));
            return;
        }

        var level = await GetTreeAsync(fork, treeId, recursive: false, ct);
        foreach (var child in level.Tree)
        {
            if (child.Type == "blob")
                entries.Add(new StoreEntry(child.Sha, Join(prefix, child.Path)));
            else if (child.Type == "tree")
                await ListFolderAsync(fork, child.Sha, Join(prefix, child.Path), entries, ct);
        }
    }

    private static string Join(string prefix, string path) => prefix.Length == 0 ? path : prefix + "/" + path;

    private async Task<TreeListing> GetTreeAsync(ForkRecord fork, string id, bool recursive, CancellationToken ct)
    {
        var name = id + (recursive ? ".r" : "");
        fork.Trees?.Add(name);
        var cache = Path.Combine(Directory, "trees", name + ".json");
        // A listing cut short by a crash is asked for again.
        if (File.Exists(cache) && ReadJson<TreeListing>(await File.ReadAllBytesAsync(cache, ct)) is { } cached)
            return cached;

        using var request = ApiRequest($"repos/{fork.Repository}/git/trees/{id}" + (recursive ? "?recursive=1" : ""));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        var text = await SendAsync(request, ct);

        var listing = JsonSerializer.Deserialize<TreeListing>(text, Json)!;
        SafeFile.WriteAllText(cache, text);
        return listing;
    }

    private HttpRequestMessage ApiRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_api, path));
        request.Headers.UserAgent.ParseAdd("Paperdoll");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        ThrowIfLimited(response);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static void ThrowIfLimited(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return;
        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) || remaining.First() != "0")
            return;

        DateTimeOffset? resetsAt = null;
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset) && long.TryParse(reset.First(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        throw new GitHubRateLimitException(resetsAt);
    }

    // A record cut short by a crash reads as none: the fork is downloaded again rather than
    // stopping Paperdoll from starting.
    private async Task<ForkRecord?> LoadForkAsync(string forkId, CancellationToken ct)
    {
        var path = ForkPath(forkId);
        return File.Exists(path) ? ReadJson<ForkRecord>(await File.ReadAllBytesAsync(path, ct)) : null;
    }

    private static T? ReadJson<T>(byte[] data) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ForkRecord> RequireForkAsync(string forkId, CancellationToken ct) =>
        await LoadForkAsync(forkId, ct) ?? throw new InvalidOperationException($"Fork {forkId} has not been synced.");

    private Task SaveForkAsync(string forkId, ForkRecord record, CancellationToken ct)
    {
        SafeFile.WriteAllText(ForkPath(forkId), JsonSerializer.Serialize(record, Json));
        return Task.CompletedTask;
    }

    /// <param name="Trees">Cached listings the current version used; null in records from before these were tracked.</param>
    private sealed record ForkRecord(string Repository, string Branch, string Commit, HashSet<string> Objects, HashSet<string>? Trees = null);

    private sealed record TreeListing(string Sha, List<TreeItem> Tree, bool Truncated);

    private sealed record TreeItem(string Path, string Type, string Sha);

    private sealed class FileBlobReader(GitHubForkStore store) : IBlobReader
    {
        public async Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default)
        {
            var path = store.ObjectPath(objectId);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
