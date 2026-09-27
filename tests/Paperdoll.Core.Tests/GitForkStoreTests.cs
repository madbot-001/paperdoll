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
    public async Task Every_forks_commit_comes_in_one_go()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "a" });
        var b = SourceRepo.Create(_root, "b", new() { ["Resources/Prototypes/b.yml"] = "b" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), a.Url, ct);
        await store.SyncAsync(Fork("b"), b.Url, ct);

        var commits = await store.CommitsAsync(ct);

        Assert.Equal(new Dictionary<string, string> { ["a"] = a.Head, ["b"] = b.Head }, commits);
    }

    [Fact]
    public async Task Removing_a_fork_frees_its_files_and_keeps_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "only in a" });
        var b = SourceRepo.Create(_root, "b", new() { ["Resources/Prototypes/b.yml"] = "only in b" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), a.Url, ct);
        await store.SyncAsync(Fork("b"), b.Url, ct);
        var aFiles = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        var bFiles = await store.ListAsync("b", ["Resources/Prototypes"], ct);
        await store.FetchAsync("a", aFiles, ct);
        await store.FetchAsync("b", bFiles, ct);

        await store.RemoveAsync("a", ct);

        Assert.Equal(aFiles.Select(e => e.ObjectId), await store.MissingAsync(aFiles.Select(e => e.ObjectId), ct));
        Assert.Empty(await store.MissingAsync(bFiles.Select(e => e.ObjectId), ct));
        Assert.Null(await store.CommitOfAsync("a", ct));
    }

    [Fact]
    public async Task A_failed_update_goes_back_to_the_version_before_without_the_network()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "old" });
        var store = await NewStoreAsync();
        var old = await store.SyncAsync(Fork("a"), source.Url, ct);
        var oldFiles = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        await store.FetchAsync("a", oldFiles, ct);
        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "new" });
        await store.SyncAsync(Fork("a"), source.Url, ct);
        // The source is gone: nothing can be fetched any more.
        Directory.Move(Path.Combine(_root, "source-a"), Path.Combine(_root, "gone"));

        await store.RevertSyncAsync("a", old, ct);

        Assert.Equal(old, await store.CommitOfAsync("a", ct));
        Assert.Equal(oldFiles, await store.ListAsync("a", ["Resources/Prototypes"], ct));
        Assert.Empty(await store.MissingAsync(oldFiles.Select(e => e.ObjectId), ct));

        await store.RevertSyncAsync("a", null, ct);
        Assert.Null(await store.CommitOfAsync("a", ct));
    }

    [Fact]
    public async Task Stopping_a_read_part_way_leaves_the_reader_in_step()
    {
        var ct = TestContext.Current.CancellationToken;
        var big = string.Concat(Enumerable.Repeat("0123456789abcdef", 400_000));
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/big.yml"] = big, ["Resources/Prototypes/small.yml"] = "small" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var files = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        await store.FetchAsync("a", files, ct);
        var bigId = files.Single(f => f.Path.EndsWith("big.yml", StringComparison.Ordinal)).ObjectId;
        var smallId = files.Single(f => f.Path.EndsWith("small.yml", StringComparison.Ordinal)).ObjectId;
        var reader = store.OpenReader();

        for (var i = 0; i < 30; i++)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromTicks(i * 500));
            try
            {
                await reader.ReadAsync(bigId, stop.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Assert.Equal("small", Encoding.UTF8.GetString((await reader.ReadAsync(smallId, ct))!));
        await reader.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    [Fact]
    public async Task Stopping_a_download_removes_the_locks_it_took()
    {
        var ct = TestContext.Current.CancellationToken;
        // A server that never answers, so the download can only be stopped.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var store = await NewStoreAsync();
        using var stop = new CancellationTokenSource();
        var download = store.SyncAsync(Fork("slow"), $"http://127.0.0.1:{port}/slow.git", stop.Token);
        await Task.Delay(500, ct);
        // As git's own lock would be, taken after the command began.
        File.WriteAllText(Path.Combine(_root, "store.git", "shallow.lock"), "");

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "store.git"), "*.lock"));
    }

    [Fact]
    public async Task Lock_files_left_by_a_crash_are_cleared()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "a" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        foreach (var name in new[] { "shallow.lock", "config.lock" })
        {
            var lockFile = Path.Combine(_root, "store.git", name);
            File.WriteAllText(lockFile, "");
            File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow.AddHours(-1));
        }
        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "newer" });

        var commit = await store.SyncAsync(Fork("a"), source.Url, ct);

        Assert.Equal(source.Head, commit);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "store.git"), "*.lock"));
    }

    [Fact]
    public async Task Lock_files_left_by_a_crash_are_cleared_in_a_store_reached_through_a_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Making links needs extra rights on Windows.");
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "a" });
        // git names the lock by the folder's real path, not the link's.
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), Path.Combine(_root, "real"));
        var store = new GitForkStore(Path.Combine(_root, "link", "store.git"));
        await store.InitializeAsync(ct);
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var lockFile = Path.Combine(_root, "link", "store.git", "shallow.lock");
        File.WriteAllText(lockFile, "");
        File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow.AddHours(-1));
        source.Commit(new() { ["Resources/Prototypes/a.yml"] = "newer" });

        Assert.Equal(source.Head, await store.SyncAsync(Fork("a"), source.Url, ct));
        Assert.False(File.Exists(lockFile));
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
    public async Task Removing_the_last_fork_forgets_it_and_frees_everything()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = SourceRepo.Create(_root, "a", new() { ["Resources/Prototypes/a.yml"] = "one" });
        var store = await NewStoreAsync();
        await store.SyncAsync(Fork("a"), source.Url, ct);
        var files = await store.ListAsync("a", ["Resources"], ct);
        await store.FetchAsync("a", files, ct);

        await store.RemoveAsync("a", ct);

        Assert.Null(await store.CommitOfAsync("a", ct));
        Assert.Equal(files.Select(e => e.ObjectId), await store.MissingAsync(files.Select(e => e.ObjectId), ct));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "store.git", "objects", "pack")));
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

        private string Git(params string[] args) => GitIn(_dir, args);
    }

    private static string GitIn(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(dir);
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

    [Fact]
    public async Task A_server_asking_for_a_password_fails_at_once_without_asking_anyone()
    {
        var ct = TestContext.Current.CancellationToken;
        // A server that answers everything with "sign in", like GitHub for a repository that is gone.
        using var listener = new System.Net.HttpListener();
        var port = System.Net.IPEndPoint.Parse("127.0.0.1:0");
        using (var probe = new System.Net.Sockets.TcpListener(port))
        {
            probe.Start();
            port = (System.Net.IPEndPoint)probe.LocalEndpoint;
        }
        listener.Prefixes.Add($"http://127.0.0.1:{port.Port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"test\"");
                context.Response.Close();
            }
        }, ct);
        var store = new GitForkStore(Path.Combine(_root, "asking.git"));
        await store.InitializeAsync(ct);
        // A password program that would keep git waiting, as a sign-in window would.
        GitIn(Path.Combine(_root, "asking.git"), "config", "credential.helper", "!f() { sleep 30; }; f");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<GitException>(() => store.SyncAsync(Fork("gone"), $"http://127.0.0.1:{port.Port}/gone.git", ct));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"git waited {clock.Elapsed.TotalSeconds:0} s");
        listener.Stop();
    }
}
