using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

/// <summary>
/// What a game folder cannot make Paperdoll do: wait without end on a pipe, read without end,
/// list or open anything outside it, or lose files to links. And what a change of source leaves.
/// </summary>
public sealed class GameFolderSafetyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-safety-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Clone(string forkId, string name = "game")
    {
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, name)).FullName, forkId);
        TestGameFolders.Git(folder, "https://github.com/o/" + forkId + ".git");
        return folder;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

    // A named pipe at the path, which no one ever writes to; skips the test where none can be made.
    internal static void Fifo(string path)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Named pipes of this kind are made on Linux and macOS only.");
        if (File.Exists(path))
            File.Delete(path);
        if (MakeFifo(path, Convert.ToUInt32("644", 8)) != 0)
            Assert.Skip($"A named pipe could not be made (error {Marshal.GetLastPInvokeError()}).");
    }

    internal static void Link(string path, string target, bool folder)
    {
        try
        {
            if (folder)
                Directory.CreateSymbolicLink(path, target);
            else
                File.CreateSymbolicLink(path, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Links cannot be made here: {e.Message}");
        }
    }

    // Runs work that must end, failing rather than waiting for ever when it does not.
    private static T Within<T>(Func<T> work)
    {
        var task = Task.Run(work);
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "It was still waiting after 10 seconds.");
        return task.Result;
    }

    [Fact]
    public void A_pipe_anywhere_in_git_is_never_waited_on()
    {
        var folder = Clone("a");
        var dotGit = Path.Combine(folder, ".git");
        File.Delete(Path.Combine(dotGit, "refs", "heads", "master"));
        Fifo(Path.Combine(dotGit, "packed-refs"));

        Assert.Null(Within(() => GameFolder.ReadGit(folder)).Commit);

        foreach (var name in new[] { "config", "HEAD" })
            Fifo(Path.Combine(dotGit, name));
        var git = Within(() => GameFolder.ReadGit(folder));
        Assert.Null(git.Commit);
        Assert.Empty(git.Remotes);
    }

    [Fact]
    public async Task A_pipe_among_the_games_files_is_read_as_empty_and_the_fork_still_loads()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        Fifo(Path.Combine(folder, "Resources", "Prototypes", "pipe.yml"));
        await using var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await session.UseFolderAsync(TestForks.A, folder, ct);

        Within(() =>
        {
            session.LoadForkAsync(TestForks.A, update: false, ct: ct).Wait(ct);
            return true;
        });

        Assert.Equal(["Human", "Lizard"], session.Selectable().Select(s => s.Id).Order());
    }

    [Fact]
    public void A_packed_refs_file_larger_than_any_real_one_is_not_read()
    {
        var folder = Clone("a");
        var git = Path.Combine(folder, ".git");
        File.Delete(Path.Combine(git, "refs", "heads", "master"));
        using (var stream = File.Create(Path.Combine(git, "packed-refs")))
        {
            stream.SetLength(GameFolder.MaxFileSize);
            stream.Seek(0, SeekOrigin.End);
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"\n{TestGameFolders.Commit} refs/heads/master\n"));
        }

        Assert.Null(GameFolder.ReadGit(folder).Commit);
    }

    [Fact]
    public void Links_in_git_are_not_followed()
    {
        var folder = Clone("a");
        var git = Path.Combine(folder, ".git");
        File.Delete(Path.Combine(git, "refs", "heads", "master"));
        var elsewhere = Path.Combine(_root, "packed-refs");
        File.WriteAllText(elsewhere, $"{TestGameFolders.Commit} refs/heads/master\n");
        Link(Path.Combine(git, "packed-refs"), elsewhere, folder: false);

        Assert.Null(GameFolder.ReadGit(folder).Commit);
    }

    // Textures/sub leads out of the folder, to out/a; Textures/L leads to sub/../../Prototypes/secretdir,
    // which the system resolves from where sub leads: out/a/../.. is the folder holding the game.
    private (string Folder, string L) DotDotThroughALink()
    {
        var folder = Clone("a");
        var textures = Path.Combine(folder, "Resources", "Textures");
        var outside = Directory.CreateDirectory(Path.Combine(_root, "out", "a")).FullName;
        var secret = Directory.CreateDirectory(Path.Combine(_root, "Prototypes", "secretdir")).FullName;
        File.WriteAllText(Path.Combine(secret, "marker.png"), "outside");
        var decoy = Directory.CreateDirectory(Path.Combine(folder, "Resources", "Prototypes", "secretdir")).FullName;
        File.WriteAllText(Path.Combine(decoy, "marker.png"), "inside");
        Link(Path.Combine(textures, "sub"), outside, folder: true);
        var l = Path.Combine(textures, "L");
        Link(l, Path.Combine("sub", "..", "..", "Prototypes", "secretdir"), folder: true);
        return (folder, l);
    }

    [Fact]
    public void A_dot_dot_after_a_link_is_resolved_where_the_link_leads()
    {
        var (_, l) = DotDotThroughALink();

        var real = GameFolder.RealPath(l);

        Assert.Equal(File.ReadAllText(Path.Combine(l, "marker.png")), File.ReadAllText(Path.Combine(real, "marker.png")));
        Assert.Equal(Path.Combine(_root, "Prototypes", "secretdir"), real);
    }

    [Fact]
    public async Task Nothing_outside_the_folder_is_listed_walked_or_opened_through_links()
    {
        var ct = TestContext.Current.CancellationToken;
        var (folder, _) = DotDotThroughALink();
        var textures = Path.Combine(folder, "Resources", "Textures");
        // A link to a link, an absolute and a relative one, and two loops.
        Link(Path.Combine(textures, "chain"), Path.Combine(textures, "L"), folder: true);
        Link(Path.Combine(textures, "up"), "..", folder: true);
        Link(Path.Combine(textures, "loop1"), Path.Combine(textures, "loop2"), folder: true);
        Link(Path.Combine(textures, "loop2"), Path.Combine(textures, "loop1"), folder: true);
        var store = new SourcedForkStore(new MemoryForkStore());
        var source = store.UseFolder("a", folder);

        var listed = Within(() => store.ListAsync("a", ["Resources/Textures", "Resources/Textures/L", "Resources/Textures/chain"], ct).Result);
        await using var reader = store.OpenReader();

        Assert.DoesNotContain(listed, e => e.Path.Contains("marker", StringComparison.Ordinal));
        Assert.Equal(["Resources/Textures/Mobs/m.rsi/meta.json"], listed.Select(e => e.Path));
        foreach (var name in new[] { "sub", "L", "chain", "up", "loop1", "loop2" })
            Assert.Contains($"Resources/Textures/{name}: a link, and links in a game folder are not followed", source.Skipped);
        Assert.Contains(source.Skipped, s => s.StartsWith("Resources/Textures/L: a link", StringComparison.Ordinal));
        await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:7:1:Resources/Textures/L/marker.png", ct));
        await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:7:1:Resources/Textures/chain/marker.png", ct));
        Assert.Throws<IOException>(() => GameFolder.RealPath(Path.Combine(textures, "loop1")));
    }

    [Fact]
    public async Task A_link_inside_the_folder_takes_no_files_from_their_own_path()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var locale = Directory.CreateDirectory(Path.Combine(folder, "Resources", "Locale", "en-US")).FullName;
        File.WriteAllText(Path.Combine(locale, "made-up.ftl"), "made-up = Made up\n");
        Link(Path.Combine(folder, "Resources", "Prototypes", "again"), Path.Combine("..", "Locale"), folder: true);
        await using var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await session.UseFolderAsync(TestForks.A, folder, ct);

        await session.LoadForkAsync(TestForks.A, update: false, ct: ct);

        Assert.Equal("Made up", session.Content!.Strings["made-up"]);
        Assert.Contains("Resources/Prototypes/again: a link, and links in a game folder are not followed", session.FolderSkipped("a"));
    }

    [Fact]
    public async Task Each_file_goes_to_the_store_its_id_came_from_whatever_the_forks_source_is_now()
    {
        var ct = TestContext.Current.CancellationToken;
        var downloads = TestForks.Store();
        var store = new SourcedForkStore(downloads);
        await store.SyncAsync(TestForks.W, ct);
        var scarf = (await store.ListAsync("w", ["Resources/Textures/Clothing/Neck"], ct)).ToList();
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "w")).FullName, "w");
        store.UseFolder("w", folder);
        var folderFiles = await store.ListAsync("w", ["Resources/Textures/Clothing/Neck"], ct);

        // Listed from the download, fetched after the fork moved to a folder: still downloaded.
        Assert.Equal(1, await store.FetchAsync("w", scarf, ct));
        Assert.Equal(["Resources/Textures/Clothing/Neck/scarf.rsi/meta.json"], Assert.Single(downloads.Fetches));

        // Listed from the folder, fetched after the fork went back: nothing to download.
        store.UseDownload("w");
        Assert.Equal(0, await store.FetchAsync("w", folderFiles, ct));
        Assert.Single(downloads.Fetches);
        Assert.Empty(await store.MissingAsync(folderFiles.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task A_new_source_the_open_fork_cannot_be_read_from_leaves_it_on_the_one_it_had()
    {
        var ct = TestContext.Current.CancellationToken;
        var downloads = TestForks.Store();
        await using var session = new EditorSession(new SourcedForkStore(downloads)) { DressUpShown = true };
        await session.LoadForkAsync(TestForks.W, update: false, ct: ct);
        var commit = session.Content!.Commit;
        // A game folder, but one whose prototypes cannot be read.
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "w")).FullName, "w");
        await using (var huge = File.Create(Path.Combine(folder, "Resources", "Prototypes", "huge.yml")))
            huge.SetLength(GameFolder.MaxFileSize + 1);

        var failed = await Assert.ThrowsAsync<GameFolderException>(() =>
            session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromFolder(folder), session.CharacterSource(), ct: ct));

        Assert.Contains("larger than 32 MB", failed.Message);
        Assert.Equal(ForkSource.Download, session.SourceOf("w"));
        Assert.Empty(session.Sources());
        Assert.Equal(commit, await session.Store.CommitOfAsync("w", ct));
        // What is open still downloads what it lacks.
        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);
        Assert.Equal(["Resources/Textures/Clothing/Neck/scarf.rsi/meta.json"], Assert.Single(downloads.Fetches));
        Assert.True(session.Draws("ClothingNeckScarf", "neck"));

        // One it can be read from is kept, and back to Download the same way.
        File.Delete(Path.Combine(folder, "Resources", "Prototypes", "huge.yml"));
        session.Use(await session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromFolder(folder), session.CharacterSource(), ct: ct));
        Assert.Equal(ForkSource.FromFolder(folder), session.SourceOf("w"));
        Assert.Equal("folder", ForkVersions.Short(session.Content.Commit));
        session.Use(await session.PrepareFromSourceAsync(TestForks.W, ForkSource.Download, session.CharacterSource(), ct: ct));
        Assert.Equal(ForkSource.Download, session.SourceOf("w"));
        Assert.Equal(commit, session.Content.Commit);
    }

    [Fact]
    public void A_Resources_folder_takes_its_git_checkout_only_from_the_games_own_folder()
    {
        // A folder of game files inside a home folder that is itself a git checkout.
        var home = Directory.CreateDirectory(Path.Combine(_root, "home")).FullName;
        TestGameFolders.Git(home, "https://github.com/DeltaV-Station/Delta-v.git");
        foreach (var name in new[] { "ss14res", "Resources" })
        {
            Directory.CreateDirectory(Path.Combine(home, name, "Prototypes"));
            Directory.CreateDirectory(Path.Combine(home, name, "Textures"));
        }

        var other = GameFolder.Inspect(Path.Combine(home, "ss14res"), Forks.KnownForks.All);
        var named = GameFolder.Inspect(Path.Combine(home, "Resources"), Forks.KnownForks.All);

        Assert.Null(other.Folder!.Top);
        Assert.Equal(GitCheckout.None, other.Git);
        Assert.Null(other.Fork);
        // Named Resources, but beside nothing of the game's.
        Assert.Null(named.Folder!.Top);
        Assert.Null(named.Fork);
        // The game's own Resources folder, in its clone.
        var clone = Clone("a");
        Assert.Equal(clone, GameFolder.Open(Path.Combine(clone, "Resources")).Top);
        Assert.Equal(TestGameFolders.Commit, GameFolder.Inspect(Path.Combine(clone, "Resources"), [TestForks.A]).Git.Commit);
    }

    [Fact]
    public async Task Rooms_kept_for_a_folders_older_readings_go_once_it_is_read_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "r")).FullName, "r");
        await using var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await session.UseFolderAsync(TestRooms.Fork, folder, ct);
        var room = Room.TryParse(TestRooms.Room, out _)!;
        var scenes = (System.Collections.IDictionary)typeof(EditorSession).GetField("_roomScenes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        var pictures = (System.Collections.IDictionary)typeof(EditorSession).GetField("_tilePictures", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;

        for (var i = 0; i < 4; i++)
        {
            await session.LoadForkAsync(TestRooms.Fork, update: false, ct: ct);
            await session.RoomAsync(room, ct);
            await session.LoadForkAsync(TestForks.A, update: false, ct: ct);
        }
        await session.LoadForkAsync(TestRooms.Fork, update: false, ct: ct);
        await session.RoomAsync(room, ct);

        Assert.Single(scenes.Keys.Cast<object>());
        var commits = pictures.Keys.Cast<object>().Select(k => (string)k.GetType().GetField("Item1")!.GetValue(k)!).Distinct().ToList();
        Assert.Equal([session.Content!.Commit], commits);
    }

    private sealed class Steps : IProgress<string>
    {
        public ConcurrentQueue<string> Seen { get; } = new();

        public void Report(string value) => Seen.Enqueue(value);
    }

    [Fact]
    public async Task Loading_from_a_folder_says_it_reads_rather_than_downloads()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await session.UseFolderAsync(TestForks.A, Clone("a"), ct);
        var folder = new Steps();
        var download = new Steps();

        await session.PrepareForkAsync(TestForks.A, update: false, null, folder, ct);
        await session.PrepareForkAsync(TestForks.B, update: false, null, download, ct);

        Assert.Contains("Reading 2 files", folder.Seen);
        Assert.Contains("Reading character and clothing sprites", folder.Seen);
        Assert.DoesNotContain(folder.Seen, s => s.StartsWith("Downloading", StringComparison.Ordinal));
        Assert.Contains("Downloading 1 files", download.Seen);
    }

    [Fact]
    public async Task Resources_swapped_for_a_link_after_the_folder_was_read_is_not_followed()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var store = new SourcedForkStore(new MemoryForkStore());
        var source = store.UseFolder("a", folder);
        var elsewhere = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName, "b");
        File.WriteAllText(Path.Combine(elsewhere, "Resources", "Prototypes", "secret.yml"), "- type: species\n  id: Secret\n");
        Directory.Move(Path.Combine(folder, "Resources"), Path.Combine(folder, "R2"));
        Link(Path.Combine(folder, "Resources"), Path.Combine(elsewhere, "Resources"), folder: true);

        var listed = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        await using var reader = store.OpenReader();

        Assert.Empty(listed);
        Assert.Contains("Resources/Prototypes: a link, and links in a game folder are not followed", source.Skipped);
        var refused = await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:1:1:Resources/Prototypes/secret.yml", ct));
        Assert.Contains("reached through a link", refused.Message);
    }

    [Fact]
    public async Task A_switch_whose_download_cannot_be_undone_still_goes_back_to_the_folder_and_says_why_it_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var downloads = TestForks.Store();
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "w")).FullName, "w");
        await using var session = new EditorSession(new SourcedForkStore(downloads));
        await session.UseFolderAsync(TestForks.W, folder, ct);
        downloads.SyncFails = new HttpRequestException("Offline.");
        downloads.SyncFailsAfterMoving = true;
        downloads.RevertFails = new IOException("The disk is full.");

        var failed = await Assert.ThrowsAnyAsync<Exception>(() => session.PrepareFromSourceAsync(TestForks.W, ForkSource.Download, null, ct: ct));

        Assert.Equal(ForkSource.FromFolder(folder), session.SourceOf("w"));
        Assert.StartsWith("Offline.", failed.Message);
        Assert.Contains("The disk is full.", failed.Message);
        Assert.IsType<HttpRequestException>(failed.InnerException);
    }

    [Fact]
    public void A_git_folder_reached_through_a_link_inside_the_game_folder_is_not_read()
    {
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "worktree")).FullName, "a");
        var elsewhere = Clone("a", "main");
        Link(Path.Combine(folder, "inside"), Path.Combine(elsewhere, ".git"), folder: true);
        File.WriteAllText(Path.Combine(folder, ".git"), "gitdir: inside\n");

        var refused = GameFolder.ReadGit(folder);
        Assert.Null(refused.Commit);
        Assert.Empty(refused.Remotes);
        Assert.Contains("passes through a link inside the game folder", refused.Problem);

        // Pointing straight there, out of the folder, as git itself would follow it.
        File.WriteAllText(Path.Combine(folder, ".git"), $"gitdir: {Path.Combine(elsewhere, ".git")}\n");
        Assert.Equal(TestGameFolders.Commit, GameFolder.ReadGit(folder).Commit);
    }

    [Fact]
    public void Inside_the_game_folder_is_decided_as_the_file_system_compares_names()
    {
        var root = Path.Combine(_root, "game");
        var written = Path.Combine(_root, "GAME", "inside");

        Assert.True(GameFolder.IsWithin(written, root, ignoreCase: true));
        Assert.False(GameFolder.IsWithin(written, root, ignoreCase: false));
        Assert.True(GameFolder.IsWithin(root, root + Path.DirectorySeparatorChar, ignoreCase: false));
        Assert.False(GameFolder.IsWithin(root + "2", root, ignoreCase: true));
    }

    [Fact]
    public async Task A_timed_out_download_whose_undo_fails_reports_both()
    {
        var ct = TestContext.Current.CancellationToken;
        var downloads = TestForks.Store();
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "w")).FullName, "w");
        await using var session = new EditorSession(new SourcedForkStore(downloads));
        await session.UseFolderAsync(TestForks.W, folder, ct);
        // A request that timed out, which is not the player stopping it.
        downloads.SyncFails = new TaskCanceledException("The request timed out.");
        downloads.SyncFailsAfterMoving = true;
        downloads.RevertFails = new IOException("The disk is full.");

        var failed = await Assert.ThrowsAnyAsync<Exception>(() => session.PrepareFromSourceAsync(TestForks.W, ForkSource.Download, null, ct: ct));

        Assert.StartsWith("The request timed out.", failed.Message);
        Assert.Contains("The disk is full.", failed.Message);
        Assert.Equal(ForkSource.FromFolder(folder), session.SourceOf("w"));
    }

    [Fact]
    public void Git_details_that_cannot_be_read_say_why_in_the_folders_notes()
    {
        var folder = Clone("a");
        var git = Path.Combine(folder, ".git");
        File.Delete(Path.Combine(git, "refs", "heads", "master"));
        using (var stream = File.Create(Path.Combine(git, "packed-refs")))
            stream.SetLength(GameFolder.MaxFileSize + 1);

        Assert.Equal("packed-refs: it is larger than 32 MB", GameFolder.ReadGit(folder).Problem);
        var store = new FolderForkStore("a", folder);
        store.Read();
        Assert.Contains(".git: packed-refs: it is larger than 32 MB", store.Skipped);
        Assert.Null(GameFolder.ReadGit(Clone("b", "fine")).Problem);
    }
}

/// <summary>Run alone: opens left waiting count for the whole process, and other tests must not meet them.</summary>
[CollectionDefinition(nameof(WaitingOpens), DisableParallelization = true)]
public sealed class WaitingOpens;

/// <summary>A file that will not open, such as a pipe swapped in for a file after it was checked.</summary>
[Collection(nameof(WaitingOpens))]
public sealed class WaitingOpenTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-open-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void An_open_that_waits_is_given_up_on_cancellable_and_few_are_left_waiting_per_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = Path.Combine(_root, "swapped.png");
        GameFolderSafetyTests.Fifo(pipe);
        var gate = new OpenGate();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Assert.Null(GameFolder.OpenPlain(pipe, 1, TimeSpan.FromMilliseconds(200), ct, gate, out var why));
            Assert.Equal("it did not open within 0.2 seconds; on a slow or network drive, Update the fork to try again", why);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));

            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                Assert.ThrowsAny<OperationCanceledException>(() => GameFolder.OpenPlain(pipe, 1, TimeSpan.FromMinutes(1), cancel.Token, gate, out _));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));

            for (var i = 0; i < 2; i++)
                Assert.Null(GameFolder.OpenPlain(pipe, 1, TimeSpan.FromMilliseconds(50), ct, gate, out _));
            Assert.Equal(4, gate.Waiting);
            // No more of this folder's files are tried while four wait, and the reason says what to do.
            Assert.Null(GameFolder.OpenPlain(pipe, 1, TimeSpan.FromMinutes(1), ct, gate, out var refused));
            Assert.Equal($"{pipe} and other files in the game folder did not open, as a pipe would not. Fix or remove them, then Update the fork; if this goes on, restart Paperdoll", refused);
            // Another folder, or this one read again, has a count of its own.
            var file = Path.Combine(_root, "plain.png");
            File.WriteAllText(file, "four");
            using (var other = GameFolder.OpenPlain(file, 4, GameFolder.OpenWait, ct, new OpenGate(), out _))
                Assert.NotNull(other);
        }
        finally
        {
            // A writer lets the waiting opens finish; each file is closed and no longer counted.
            using (new FileStream(pipe, FileMode.Open, FileAccess.Write)) { }
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (gate.Waiting > 0 && DateTime.UtcNow < until)
                Thread.Sleep(10);
        }
        Assert.Equal(0, gate.Waiting);

        // A plain file that is not the size it was checked at is refused.
        var changedFile = Path.Combine(_root, "changed.png");
        File.WriteAllText(changedFile, "four");
        Assert.Null(GameFolder.OpenPlain(changedFile, 3, GameFolder.OpenWait, ct, gate, out var changed));
        Assert.Contains("changed while it was being read", changed);
        Assert.Equal(TimeSpan.FromSeconds(15), GameFolder.OpenWait);
    }
}
