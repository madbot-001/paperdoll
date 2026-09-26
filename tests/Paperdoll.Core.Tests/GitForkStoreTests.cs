using System.Diagnostics;
using System.Text;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Tests the store against small local repositories standing in for forks on GitHub, so they run
/// offline. They need the git program.
/// </summary>
public sealed class GitForkStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-test-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private static ForkInfo Fork(string id) =>
        new(id, id, $"test/{id}", "main", AppearanceModel.New, false, []);

    private async Task<GitForkStore> NewStoreAsync()
    {
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        return store;
    }

    [Fact]
    public async Task Sync_fetches_listings_but_no_file_contents()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new()
        {
            ["Resources/Prototypes/species.yml"] = "- type: species\n  id: Human\n",
            ["Resources/Textures/Mobs/human.rsi/meta.json"] = "{}",
        });
        var store = await NewStoreAsync();

        var commit = await store.SyncAsync(Fork("a"), source.Url, ct);
        var entries = await store.ListAsync("a", ["Resources/Prototypes"], ct);

        Assert.Equal(source.Head, commit);
        Assert.Equal(["Resources/Prototypes/species.yml"], entries.Select(e => e.Path));
        Assert.Equal(entries.Select(e => e.ObjectId), await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task Fetched_files_can_be_read_and_unfetched_ones_read_as_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new()
        {
            ["Resources/Prototypes/species.yml"] = "- type: species\n  id: Human\n",
            ["Resources/Textures/big.png"] = "not needed",
        });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var wanted = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        var unwanted = await store.ListAsync("a", ["Resources/Textures"], ct);

        var fetched = await store.FetchAsync("a", wanted, ct);

        await using var reader = store.OpenReader();
        Assert.Equal(1, fetched);
        Assert.Equal("- type: species\n  id: Human\n", Encoding.UTF8.GetString((await reader.ReadAsync(wanted[0].ObjectId, ct))!));
        Assert.Null(await reader.ReadAsync(unwanted[0].ObjectId, ct));
    }

    [Fact]
    public async Task Files_shared_between_forks_are_fetched_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var shared = "shared sprite";
        var a = SourceRepo.Create(_root, "a", new() { ["Resources/Textures/x.png"] = shared, ["Resources/Textures/a.png"] = "a only" });
        var b = SourceRepo.Create(_root, "b", new() { ["Resources/Textures/x.png"] = shared, ["Resources/Textures/b.png"] = "b only" });
        var store = await NewStoreAsync();

        await store.SyncAsync(Fork("a"), a.Url, ct);
        var fromA = await store.FetchAsync("a", await store.ListAsync("a", ["Resources"], ct), ct);
        await store.SyncAsync(Fork("b"), b.Url, ct);
        var fromB = await store.FetchAsync("b", await store.ListAsync("b", ["Resources"], ct), ct);

        Assert.Equal(2, fromA);
        Assert.Equal(1, fromB);
    }

    [Fact]
    public async Task Syncing_again_moves_to_the_newest_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "one" });
        var store = await NewStoreAsync();
        var first = await store.SyncAsync(Fork("a"), source.Url, ct);

        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "two" });
        var second = await store.SyncAsync(Fork("a"), source.Url, ct);

        Assert.NotEqual(first, second);
        Assert.Equal(source.Head, second);
        Assert.Equal(second, await store.CommitOfAsync("a", ct));
    }

    [Fact]
    public async Task A_fork_can_be_moved_to_an_older_commit_such_as_a_servers()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "server's" });
        var serverCommit = source.Head;
        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "newest" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);

        var commit = await store.SyncToCommitAsync(Fork("a"), source.Url, serverCommit, ct);
        var files = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", files, ct);

        Assert.Equal(serverCommit, commit);
        Assert.Equal(serverCommit, await store.CommitOfAsync("a", ct));
        await using var reader = store.OpenReader();
        Assert.Equal("server's", Encoding.UTF8.GetString((await reader.ReadAsync(files.Single().ObjectId, ct))!));
    }

    [Fact]
    public async Task Cleaning_up_drops_an_older_versions_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "one", ["Resources/Prototypes/b.yml"] = "same" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var old = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", old, ct);

        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "two", ["Resources/Prototypes/b.yml"] = "same" });
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var current = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", current, ct);
        await store.CleanUpAsync(ct);

        var oldA = old.Single(e => e.Path == "Resources/Prototypes/a.yml").ObjectId;
        Assert.Equal([oldA], await store.MissingAsync(old.Concat(current).Select(e => e.ObjectId), ct));
        // The store still works: current files read, and dropped ones can be fetched again.
        await using (var reader = store.OpenReader())
            Assert.Equal("two", Encoding.UTF8.GetString((await reader.ReadAsync(current.Single(e => e.Path == "Resources/Prototypes/a.yml").ObjectId, ct))!));
        await store.SyncAsync(Fork("a"), source.Url, ct);
        Assert.Empty(await store.MissingAsync(current.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task Removing_a_fork_forgets_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "one" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        await store.FetchAsync("a", await store.ListAsync("a", ["Resources"], ct), ct);

        await store.RemoveAsync("a", ct);

        Assert.Null(await store.CommitOfAsync("a", ct));
    }

    /// <summary>A local git repository that serves partial fetches like GitHub does.</summary>
    private sealed class SourceRepo
    {
        private readonly string _dir;

        private SourceRepo(string dir) => _dir = dir;

        public string Url => new Uri(_dir).AbsoluteUri;
        public string Head => Git("rev-parse", "HEAD").Trim();

        public static SourceRepo Create(string root, string name, Dictionary<string, string> files)
        {
            var repo = new SourceRepo(Path.Combine(root, "source-" + name));
            Directory.CreateDirectory(repo._dir);
            repo.Git("init", "--quiet", "--initial-branch=main");
            repo.Git("config", "uploadpack.allowFilter", "true");
            repo.Git("config", "uploadpack.allowAnySHA1InWant", "true");
            repo.Commit(files);
            return repo;
        }

        public void Commit(Dictionary<string, string> files)
        {
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(_dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
            Git("add", "--all");
            Git("-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "-m", "files");
        }

        private string Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(_dir);
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)}: {error}");
            return output;
        }
    }
}
