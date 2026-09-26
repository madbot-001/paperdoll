using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

public sealed class GitHubForkStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-api-").FullName;
    private readonly FakeGitHub _github = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ForkInfo Fork(string id) => new(id, id, $"owner/{id}", "main", AppearanceModel.New, false, []);

    private async Task<GitHubForkStore> NewStoreAsync()
    {
        var store = new GitHubForkStore(Path.Combine(_root, "store"), new HttpClient(_github));
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        return store;
    }

    [Fact]
    public async Task Lists_fetches_and_reads_only_the_chosen_folders()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new()
        {
            ["Resources/Prototypes/Species/human.yml"] = "id: Human",
            ["Resources/Prototypes/a.yml"] = "a",
            ["Resources/Textures/big.png"] = "not needed",
        });
        var store = await NewStoreAsync();

        var commit = await store.SyncAsync(Fork("a"), ct);
        var entries = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        var fetched = await store.FetchAsync("a", entries, ct);

        await using var reader = store.OpenReader();
        var human = entries.Single(e => e.Path == "Resources/Prototypes/Species/human.yml");
        Assert.Equal(_github.CommitOf("owner/a"), commit);
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, fetched);
        Assert.Equal(2, _github.RawRequests);
        Assert.Equal("id: Human", Encoding.UTF8.GetString((await reader.ReadAsync(human.ObjectId, ct))!));
        Assert.Empty(await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task Listings_cut_short_are_walked_folder_by_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new()
        {
            ["Resources/Textures/One/a.png"] = "a",
            ["Resources/Textures/One/b.png"] = "b",
            ["Resources/Textures/Two/c.png"] = "c",
        });
        _github.TruncateAbove = 1;
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), ct);

        var entries = await store.ListAsync("a", ["Resources/Textures"], ct);

        Assert.Equal(
            ["Resources/Textures/One/a.png", "Resources/Textures/One/b.png", "Resources/Textures/Two/c.png"],
            entries.Select(e => e.Path).Order());
    }

    [Fact]
    public async Task Folder_listings_are_asked_for_once()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new() { ["Resources/Prototypes/a.yml"] = "a" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), ct);

        await store.ListAsync("a", ["Resources/Prototypes"], ct);
        var after = _github.ApiRequests;
        await store.ListAsync("a", ["Resources/Prototypes"], ct);

        Assert.Equal(after, _github.ApiRequests);
    }

    [Fact]
    public async Task Files_shared_between_forks_are_downloaded_once_and_kept_while_used()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new() { ["Resources/x.png"] = "shared", ["Resources/a.png"] = "a only" });
        _github.AddRepo("owner/b", new() { ["Resources/x.png"] = "shared", ["Resources/b.png"] = "b only" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), ct);
        await store.SyncAsync(Fork("b"), ct);
        var inA = await store.ListAsync("a", ["Resources"], ct);
        var inB = await store.ListAsync("b", ["Resources"], ct);

        var fromA = await store.FetchAsync("a", inA, ct);
        var fromB = await store.FetchAsync("b", inB, ct);
        await store.RemoveAsync("a", ct);

        Assert.Equal(2, fromA);
        Assert.Equal(1, fromB);
        var gone = inA.Single(e => e.Path == "Resources/a.png").ObjectId;
        Assert.Equal([gone], await store.MissingAsync(inA.Select(e => e.ObjectId), ct));
        Assert.Null(await store.CommitOfAsync("a", ct));
    }

    [Fact]
    public async Task Cleaning_up_drops_what_an_older_version_used()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new() { ["Resources/x.png"] = "one", ["Resources/same.png"] = "same" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), ct);
        var old = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", old, ct);
        var oldTrees = Directory.GetFiles(Path.Combine(_root, "store", "trees")).Length;

        _github.AddRepo("owner/a", new() { ["Resources/x.png"] = "two", ["Resources/same.png"] = "same" });
        await store.SyncAsync(Fork("a"), ct);
        var current = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", current, ct);
        var freed = await store.CleanUpAsync(ct);

        Assert.True(freed > 0);
        var oldX = old.Single(e => e.Path == "Resources/x.png").ObjectId;
        Assert.Equal([oldX], await store.MissingAsync(old.Concat(current).Select(e => e.ObjectId), ct));
        Assert.Equal(oldTrees, Directory.GetFiles(Path.Combine(_root, "store", "trees")).Length);
    }

    [Fact]
    public async Task A_file_that_does_not_match_its_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new() { ["Resources/a.yml"] = "real" });
        _github.TamperWith = "Resources/a.yml";
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), ct);
        var entries = await store.ListAsync("a", ["Resources"], ct);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.FetchAsync("a", entries, ct));
        Assert.Single(await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task Running_out_of_requests_says_when_they_come_back()
    {
        var ct = TestContext.Current.CancellationToken;
        _github.AddRepo("owner/a", new() { ["Resources/a.yml"] = "a" });
        _github.RateLimitedUntil = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);
        var store = await NewStoreAsync();

        var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => store.SyncAsync(Fork("a"), ct));

        Assert.Equal(_github.RateLimitedUntil, error.ResetsAt);
    }

    /// <summary>Serves the three GitHub requests the store makes, from files held in memory.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, byte[]>> _repos = [];
        private readonly Dictionary<string, (string Repo, string Folder)> _trees = [];
        // Adding a repo again publishes a new version with its own commit and tree ids.
        private readonly Dictionary<string, int> _versions = [];

        private int _apiRequests;
        private int _rawRequests;

        public int ApiRequests => _apiRequests;
        public int RawRequests => _rawRequests;
        public int TruncateAbove { get; set; } = int.MaxValue;
        public string? TamperWith { get; set; }
        public DateTimeOffset? RateLimitedUntil { get; set; }

        public void AddRepo(string repo, Dictionary<string, string> files)
        {
            _versions[repo] = _versions.GetValueOrDefault(repo) + 1;
            _repos[repo] = files.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value));
            _trees[CommitOf(repo)] = (repo, "");
            foreach (var folder in files.Keys.SelectMany(Parents).Distinct())
                _trees[TreeId(repo, folder)] = (repo, folder);
        }

        public string CommitOf(string repo) => Hash($"commit {repo} {_versions.GetValueOrDefault(repo)}");

        private string TreeId(string repo, string folder) => Hash($"tree {repo} {_versions.GetValueOrDefault(repo)} {folder}");

        private static string Hash(string text) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)));

        private static IEnumerable<string> Parents(string path)
        {
            var parts = path.Split('/');
            for (var i = 1; i < parts.Length; i++)
                yield return string.Join('/', parts[..i]);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!;
            if (RateLimitedUntil is { } until)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
                limited.Headers.Add("x-ratelimit-remaining", "0");
                limited.Headers.Add("x-ratelimit-reset", until.ToUnixTimeSeconds().ToString());
                return Task.FromResult(limited);
            }

            if (url.Host == "raw.githubusercontent.com")
            {
                Interlocked.Increment(ref _rawRequests);
                // owner/repo/commit/path...
                var parts = url.AbsolutePath.TrimStart('/').Split('/', 4);
                var path = Uri.UnescapeDataString(parts[3]);
                var data = _repos[parts[0] + "/" + parts[1]][path];
                if (path == TamperWith)
                    data = Encoding.UTF8.GetBytes("tampered");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
            }

            Interlocked.Increment(ref _apiRequests);
            var segments = url.AbsolutePath.TrimStart('/').Split('/');
            var repo = segments[1] + "/" + segments[2];
            if (segments[3] == "commits")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CommitOf(repo)) });

            var (_, folder) = _trees[segments[5]];
            var recursive = url.Query.Contains("recursive=1");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listing(repo, folder, recursive)) });
        }

        private string Listing(string repo, string folder, bool recursive)
        {
            var prefix = folder.Length == 0 ? "" : folder + "/";
            var items = new List<object>();
            foreach (var (path, data) in _repos[repo].Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var rest = path[prefix.Length..];
                var slash = rest.IndexOf('/');
                if (slash < 0)
                    items.Add(new { path = rest, type = "blob", sha = GitHubForkStore.GitBlobId(data) });
                else if (recursive)
                    items.Add(new { path = rest, type = "blob", sha = GitHubForkStore.GitBlobId(data) });
            }
            var subfolders = _repos[repo].Keys
                .Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
                .SelectMany(p => Parents(p[prefix.Length..]))
                .Distinct()
                .Where(sub => recursive || !sub.Contains('/'));
            foreach (var sub in subfolders)
                items.Add(new { path = sub, type = "tree", sha = TreeId(repo, prefix + sub) });

            var truncated = recursive && items.Count > TruncateAbove;
            return JsonSerializer.Serialize(new { sha = TreeId(repo, folder), tree = truncated ? items.Take(TruncateAbove) : items, truncated });
        }
    }
}
