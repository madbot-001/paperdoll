using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>Forks read from made-up game folders on disk rather than downloaded.</summary>
public sealed class GameFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-folder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string NewFolder(string name = "game") => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    // A game folder holding a made-up fork, with a checkout on TestGameFolders.Commit.
    private string Clone(string forkId, string name = "game")
    {
        var folder = TestGameFolders.Write(NewFolder(name), forkId);
        TestGameFolders.Git(folder, "https://github.com/o/" + forkId + ".git");
        return folder;
    }

    // A session over the test store whose fork comes from the folder.
    private static async Task<(EditorSession Session, MemoryForkStore Downloads)> FromFolder(ForkInfo fork, string folder)
    {
        var downloads = TestForks.Store();
        var session = new EditorSession(new SourcedForkStore(downloads));
        await session.UseFolderAsync(fork, folder, TestContext.Current.CancellationToken);
        return (session, downloads);
    }

    private static string Problem(string path) => Assert.Throws<GameFolderException>(() => GameFolder.Open(path)).Message;

    [Fact]
    public void A_folder_holding_Resources_or_the_Resources_folder_itself_is_a_game_folder()
    {
        var folder = Clone("a");

        // Its path with the temporary folder's own links followed, as macOS's /var is a link.
        var real = GameFolder.RealPath(folder);

        var top = GameFolder.Open(folder);
        var resources = GameFolder.Open(Path.Combine(folder, "Resources"));

        Assert.Equal(Path.Combine(real, "Resources"), top.Resources);
        Assert.Equal(real, top.Top);
        Assert.Equal((top.Top, top.Resources), (resources.Top, resources.Resources));
        Assert.Equal((real, Path.Combine(real, "Resources")), (top.Root, resources.Root));
    }

    [Fact]
    public void A_folder_that_is_not_a_game_folder_says_what_is_missing()
    {
        var empty = NewFolder("empty");
        Assert.Contains("has no Resources/Prototypes or Resources/Textures", Problem(empty));
        Assert.Contains("There is no folder at", Problem(Path.Combine(_root, "absent")));
        Assert.Equal("No folder was chosen.", Problem(" "));

        Directory.CreateDirectory(Path.Combine(empty, "Resources", "Prototypes"));
        Assert.Contains("has a Resources folder but no Resources/Textures", Problem(empty));
        Assert.Contains("looks like a Resources folder but has no Textures", Problem(Path.Combine(empty, "Resources")));
    }

    [Fact]
    public void The_launchers_own_files_are_not_a_game_folder()
    {
        var withDb = NewFolder("launcher");
        File.WriteAllText(Path.Combine(withDb, "content.db"), "");
        var merged = Clone("a", "merged");
        File.WriteAllText(Path.Combine(merged, "Resources", "Prototypes", "__merged.yml"), "");

        Assert.Contains("the SS14 launcher's own files", Problem(withDb));
        Assert.Contains("not a game folder", Problem(merged));
    }

    [Theory]
    [InlineData("https://github.com/o/a.git")]
    [InlineData("https://github.com/O/A")]
    [InlineData("https://github.com/o/a/")]
    [InlineData("git@github.com:o/a.git")]
    [InlineData("ssh://git@github.com/o/a.git")]
    [InlineData("ssh://git@github.com:22/o/a")]
    public void The_fork_is_found_from_the_origin_remote_in_any_of_gits_forms(string url)
    {
        var folder = Clone("a");
        TestGameFolders.Git(folder, url);

        var git = GameFolder.ReadGit(folder);
        var (sure, likely) = GameFolder.ForkOf(git, [TestForks.A, TestForks.B]);

        Assert.Equal([("origin", url)], git.Remotes);
        Assert.Equal(TestForks.A, sure);
        Assert.Empty(likely);
    }

    [Fact]
    public void An_origin_Paperdoll_does_not_know_leaves_the_fork_to_the_player_with_other_remotes_offered_first()
    {
        var folder = Clone("a");
        TestGameFolders.Git(folder, "https://github.com/someone/a-fork.git", other: "https://github.com/o/b");

        var (sure, likely) = GameFolder.ForkOf(GameFolder.ReadGit(folder), [TestForks.A, TestForks.B]);
        Assert.Null(sure);
        Assert.Equal([TestForks.B], likely);

        // Another host, or no remote at all, names nothing.
        TestGameFolders.Git(folder, "https://gitlab.com/o/a.git");
        var none = GameFolder.ForkOf(GameFolder.ReadGit(folder), [TestForks.A]);
        Assert.Null(none.Sure);
        Assert.Empty(none.Likely);
        TestGameFolders.Git(folder, null);
        Assert.Null(GameFolder.ForkOf(GameFolder.ReadGit(folder), [TestForks.A]).Sure);
        // The only remote, whatever its name, is taken as sure.
        TestGameFolders.Git(folder, null, other: "git@github.com:o/a.git");
        Assert.Equal(TestForks.A, GameFolder.ForkOf(GameFolder.ReadGit(folder), [TestForks.A]).Sure);
    }

    [Fact]
    public void A_known_forks_clone_is_found_among_the_known_forks()
    {
        var folder = Clone("a");
        TestGameFolders.Git(folder, "git@github.com:DeltaV-Station/Delta-v.git");

        var check = GameFolder.Inspect(folder, KnownForks.All);

        Assert.Null(check.Problem);
        Assert.Equal("deltav", check.Fork?.Id);
        Assert.Equal(TestGameFolders.Commit, check.Git.Commit);
        Assert.Contains("has no Resources", GameFolder.Inspect(NewFolder("empty"), KnownForks.All).Problem);
    }

    [Theory]
    [InlineData(TestGameFolders.Ref.Loose)]
    [InlineData(TestGameFolders.Ref.Packed)]
    [InlineData(TestGameFolders.Ref.Detached)]
    public void The_commit_is_read_from_the_checkout_without_git(TestGameFolders.Ref kind)
    {
        var folder = Clone("a");
        TestGameFolders.Git(folder, "https://github.com/o/a", kind);

        Assert.Equal(TestGameFolders.Commit, GameFolder.ReadGit(folder).Commit);
    }

    [Fact]
    public void A_folder_without_git_or_with_an_unborn_branch_has_no_commit_and_a_worktree_is_followed()
    {
        var plain = TestGameFolders.Write(NewFolder("plain"), "a");
        Assert.Equal(GitCheckout.None, GameFolder.ReadGit(plain));

        var unborn = Clone("a", "unborn");
        File.Delete(Path.Combine(unborn, ".git", "refs", "heads", "master"));
        Assert.Null(GameFolder.ReadGit(unborn).Commit);

        // A worktree's .git is a file naming its own folder in the main checkout's .git.
        var main = Clone("a", "main");
        var own = Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "side")).FullName;
        File.WriteAllText(Path.Combine(own, "HEAD"), "ref: refs/heads/master\n");
        File.WriteAllText(Path.Combine(own, "commondir"), "../..\n");
        var side = TestGameFolders.Write(NewFolder("side"), "a");
        File.WriteAllText(Path.Combine(side, ".git"), $"gitdir: {own}\n");
        var git = GameFolder.ReadGit(side);
        Assert.Equal(TestGameFolders.Commit, git.Commit);
        Assert.Equal("https://github.com/o/a.git", Assert.Single(git.Remotes).Url);

        // A ref that would lead out of .git is not read.
        File.WriteAllText(Path.Combine(main, ".git", "HEAD"), "ref: refs/../../../outside\n");
        Assert.Null(GameFolder.ReadGit(main).Commit);
    }

    [Fact]
    public void Versions_show_the_commit_and_mark_a_folders_files_as_possibly_different()
    {
        Assert.Equal("01234567", ForkVersions.Short(TestGameFolders.Commit));
        Assert.Equal("01234567 (folder)", ForkVersions.Short(ForkVersions.OfFolder(TestGameFolders.Commit, 5)));
        Assert.Equal("folder", ForkVersions.Short(ForkVersions.OfFolder(null, 5)));
        Assert.Null(ForkVersions.Commit(ForkVersions.OfFolder(null, 5)));
        Assert.Equal(TestGameFolders.Commit, ForkVersions.Commit(ForkVersions.OfFolder(TestGameFolders.Commit, 5)));
    }

    [Fact]
    public async Task A_fork_loads_from_a_folder_and_the_character_is_drawn()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var (session, downloads) = await FromFolder(TestForks.A, folder);
        await using var _ = session;

        await session.LoadForkAsync(TestForks.A, update: false, ct: ct);
        session.NewCharacter("Lizard");
        using var picture = session.Render();

        Assert.Equal(["Human", "Lizard"], session.Selectable().Select(s => s.Id).Order());
        Assert.Equal("01234567 (folder)", ForkVersions.Short(session.Content!.Commit));
        Assert.Equal(ForkSource.FromFolder(folder), session.SourceOf("a"));
        Assert.Equal(ForkSource.Download, session.SourceOf("b"));
        Assert.Contains("Mobs/m.rsi/meta.json", session.Content.TextureFiles.Keys);
        Assert.True(picture.Width > 0);
        // Nothing came through the download store.
        Assert.Null(await downloads.CommitOfAsync("a", ct));
        Assert.Empty(downloads.Fetches);
        Assert.Contains(session.Content.Commit, (await session.Store.CommitsAsync(ct)).Values);
    }

    [Fact]
    public async Task Another_fork_still_downloads_through_the_usual_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, downloads) = await FromFolder(TestForks.A, Clone("a"));
        await using var _ = session;

        await session.LoadForkAsync(TestForks.W, update: false, ct: ct);
        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);

        Assert.NotNull(await downloads.CommitOfAsync("w", ct));
        Assert.Equal(await downloads.CommitOfAsync("w", ct), session.Content!.Commit);
        Assert.Equal(["Resources/Textures/Clothing/Neck/scarf.rsi/meta.json"], Assert.Single(downloads.Fetches));
        Assert.Equal("git", new EditorSession(new SourcedForkStore(new GitForkStore(_root))).StoreKind);
        Assert.Equal("GitHub API", session.StoreKind);
    }

    [Fact]
    public async Task Clothing_is_tried_on_and_a_demo_room_drawn_from_a_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        TestGameFolders.Write(NewFolder("w"), "w");
        var (session, downloads) = await FromFolder(TestForks.W, Path.Combine(_root, "w"));
        await using var _ = session;
        session.DressUpShown = true;
        await session.LoadForkAsync(TestForks.W, update: false, ct: ct);

        Assert.Contains("Clothing/Neck/scarf.rsi/meta.json", session.Content!.WardrobeFiles.Keys);
        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["neck"]);
        Assert.True(session.Draws("ClothingNeckScarf", "neck"));
        Assert.Empty(downloads.Fetches);
        // Without git, the version is just "folder".
        Assert.Equal("folder", ForkVersions.Short(session.Content.Commit));

        TestGameFolders.Write(NewFolder("r"), "r");
        await session.UseFolderAsync(TestRooms.Fork, Path.Combine(_root, "r"), ct);
        await session.LoadForkAsync(TestRooms.Fork, update: false, ct: ct);
        var scene = await session.RoomAsync(Room.TryParse(TestRooms.Room, out var problem) ?? throw new InvalidDataException(problem), ct);
        var picture = scene.Paint(SKColors.Black);

        Assert.NotEmpty(scene.Entities);
        Assert.Contains(scene.CreditsWithin(new Rendering.Box(-50, -50, 50, 50)), c => c.Copyright == "Tile made up");
        Assert.True(picture.Below.Width > 0);
        Assert.Empty(downloads.Fetches);
    }

    [Fact]
    public async Task Each_load_reads_the_folder_again_picking_up_edited_files_and_its_new_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("b");
        var (session, _) = await FromFolder(TestForks.B, folder);
        await using var _s = session;
        await session.LoadForkAsync(TestForks.B, update: false, ct: ct);
        var before = session.Content!.Commit;
        Assert.Equal(["Human"], session.Selectable().Select(s => s.Id));

        // Edited on disk but not committed: the files as they are, on the same commit, as a
        // version of their own so nothing kept for the last reading is used.
        File.AppendAllText(Path.Combine(folder, "Resources", "Prototypes", "body.yml"), "\n- type: species\n  id: Moth\n  name: species-name-moth\n  roundStart: true\n  dollPrototype: Doll\n");
        await session.LoadForkAsync(TestForks.B, update: false, ct: ct);
        Assert.Contains("Moth", session.Selectable().Select(s => s.Id));
        Assert.Equal("01234567 (folder)", ForkVersions.Short(session.Content.Commit));
        Assert.NotEqual(before, session.Content.Commit);

        // Committed: Update picks up the commit the branch is on now.
        File.WriteAllText(Path.Combine(folder, ".git", "refs", "heads", "master"), "89abcdef89abcdef89abcdef89abcdef89abcdef\n");
        await session.LoadForkAsync(TestForks.B, update: true, ct: ct);
        Assert.Equal("89abcdef (folder)", ForkVersions.Short(session.Content.Commit));

        // A folder that will not do now leaves the fork on what it had.
        var kept = session.Content.Commit;
        Directory.Move(Path.Combine(folder, "Resources", "Textures"), Path.Combine(folder, "Resources", "Moved"));
        await Assert.ThrowsAsync<GameFolderException>(() => session.LoadForkAsync(TestForks.B, update: true, ct: ct));
        Assert.Equal(kept, await session.Store.CommitOfAsync("b", ct));
        Assert.Equal(kept, session.Content.Commit);
    }

    [Fact]
    public async Task Nothing_in_the_folder_is_ever_written_and_removing_the_fork_leaves_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = TestGameFolders.Write(NewFolder("rw"), "r");
        TestGameFolders.Write(folder, "w");
        TestGameFolders.Git(folder, "https://github.com/o/r");
        var before = TestGameFolders.Snapshot(folder);

        var (session, _) = await FromFolder(TestRooms.Fork, folder);
        await using (session)
        {
            await session.LoadForkAsync(TestRooms.Fork, update: false, ct: ct);
            await session.RoomAsync(Room.TryParse(TestRooms.Room, out _)!, ct);
            await session.LoadForkAsync(TestRooms.Fork, update: true, ct: ct);
            await session.UseDownloadAsync(TestRooms.Fork, ct);
            await session.UseFolderAsync(TestForks.W, folder, ct);
            session.DressUpShown = true;
            await session.LoadForkAsync(TestForks.W, update: false, ct: ct);
            await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);
            await session.LoadForkAsync(TestRooms.Fork, update: false, ct: ct);
            await session.CleanUpStoreAsync(ct);
            await session.RemoveForkAsync(TestForks.W, ct);

            Assert.Equal(ForkSource.Download, session.SourceOf("w"));
            Assert.Empty(session.Sources());
            Assert.DoesNotContain("w", (await session.Store.CommitsAsync(ct)).Keys);
        }

        Assert.Equal(before, TestGameFolders.Snapshot(folder));
    }

    [Fact]
    public async Task One_folder_serves_one_fork_and_a_folder_that_will_not_do_leaves_the_source_as_it_was()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var (session, _) = await FromFolder(TestForks.A, folder);
        await using var _s = session;

        var taken = await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.B, Path.Combine(folder, "Resources"), ct));
        Assert.Contains("is already the game folder of", taken.Message);
        await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.A, NewFolder("empty"), ct));

        Assert.Equal(ForkSource.FromFolder(folder), session.SourceOf("a"));
        Assert.Equal(ForkSource.Download, session.SourceOf("b"));
    }

    [Fact]
    public async Task A_folder_fork_cannot_be_moved_to_a_servers_commit()
    {
        var store = new SourcedForkStore(TestForks.Store());
        store.UseFolder("a", Clone("a"));

        var refused = await Assert.ThrowsAsync<GameFolderException>(() => store.SyncToCommitAsync(TestForks.A, TestGameFolders.Commit, TestContext.Current.CancellationToken));
        Assert.Contains("switch Fork A back to Download", refused.Message);
    }

    [Fact]
    public async Task A_source_kept_from_before_whose_folder_is_gone_stays_with_the_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await using var _ = session;
        var gone = Path.Combine(_root, "gone");

        await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromFolder(gone), ["b"] = ForkSource.Download }, ct);

        Assert.Equal(ForkSource.FromFolder(gone), session.SourceOf("a"));
        Assert.Equal(["a"], session.Sources().Keys);
        Assert.Contains("There is no folder at", session.FolderProblem("a"));
        Assert.DoesNotContain("a", (await session.Store.CommitsAsync(ct)).Keys);
        var failed = await Assert.ThrowsAsync<GameFolderException>(() => session.LoadForkAsync(TestForks.A, update: false, ct: ct));
        Assert.Contains("There is no folder at", failed.Message);
    }

    [Fact]
    public async Task Links_are_left_out_and_paths_out_of_the_folder_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var outside = NewFolder("outside");
        File.WriteAllText(Path.Combine(outside, "secret.yml"), "- type: species\n  id: Secret\n");
        var prototypes = Path.Combine(folder, "Resources", "Prototypes");
        Directory.CreateDirectory(Path.Combine(folder, "Resources", "Locale", "en-US"));
        File.WriteAllText(Path.Combine(folder, "Resources", "Locale", "en-US", "made-up.ftl"), "made-up = Made up\n");
        try
        {
            File.CreateSymbolicLink(Path.Combine(prototypes, "leak.yml"), Path.Combine(outside, "secret.yml"));
            Directory.CreateSymbolicLink(Path.Combine(prototypes, "leaks"), outside);
            // One leading to another place within the folder is not followed either.
            Directory.CreateSymbolicLink(Path.Combine(prototypes, "again"), Path.Combine(folder, "Resources", "Locale"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Links cannot be made here: {e.Message}");
        }
        var store = new SourcedForkStore(new MemoryForkStore());
        var source = store.UseFolder("a", folder);

        var listed = await store.ListAsync("a", ["Resources/Prototypes", "Resources/../outside", "Resources/Prototypes/leaks"], ct);
        await using var reader = store.OpenReader();

        Assert.Equal(["Resources/Prototypes/body.yml", "Resources/Prototypes/extra.yml"], listed.Select(e => e.Path));
        Assert.Contains("Resources/Prototypes/leak.yml: a link, and links in a game folder are not followed", source.Skipped);
        Assert.Contains("Resources/Prototypes/leaks: a link, and links in a game folder are not followed", source.Skipped);
        Assert.Contains("Resources/Prototypes/again: a link, and links in a game folder are not followed", source.Skipped);
        Assert.Contains(source.Skipped, s => s.StartsWith("Resources/../outside: not a path", StringComparison.Ordinal));
        await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:1:1:Resources/Prototypes/leak.yml", ct));
        await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:1:1:Resources/../../outside/secret.yml", ct));
        await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync("a:1:1:/etc/hostname", ct));
        Assert.Null(await reader.ReadAsync("b:1:1:Resources/Prototypes/body.yml", ct));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_reported_not_thrown_as_a_crash()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var store = new SourcedForkStore(new MemoryForkStore());
        store.UseFolder("a", folder);
        var huge = Path.Combine(folder, "Resources", "Prototypes", "huge.yml");
        await using (var stream = File.Create(huge))
            stream.SetLength(GameFolder.MaxFileSize + 1);
        var entries = await store.ListAsync("a", ["Resources/Prototypes"], ct);
        await using var reader = store.OpenReader();

        var tooLarge = await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync(entries.Single(e => e.Path.EndsWith("huge.yml", StringComparison.Ordinal)).ObjectId, ct));
        Assert.Contains("larger than 32 MB", tooLarge.Message);

        File.Delete(huge);
        var body = entries.Single(e => e.Path.EndsWith("body.yml", StringComparison.Ordinal));
        File.Delete(Path.Combine(folder, "Resources", "Prototypes", "body.yml"));
        var gone = await Assert.ThrowsAsync<GameFolderException>(() => reader.ReadAsync(body.ObjectId, ct));
        Assert.Contains("Resources/Prototypes/body.yml", gone.Message);
        Assert.Contains("not there any more", gone.Message);
        // A game folder holds all it lists; nothing is downloaded for it.
        Assert.Empty(await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
        Assert.Equal(0, await store.FetchAsync("a", entries, ct));
    }

    [Fact]
    public async Task An_edited_file_gets_a_new_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Clone("a");
        var store = new SourcedForkStore(new MemoryForkStore());
        store.UseFolder("a", folder);
        var before = (await store.ListAsync("a", ["Resources/Prototypes"], ct)).Single(e => e.Path.EndsWith("body.yml", StringComparison.Ordinal));

        File.AppendAllText(Path.Combine(folder, "Resources", "Prototypes", "body.yml"), "\n");
        var after = (await store.ListAsync("a", ["Resources/Prototypes"], ct)).Single(e => e.Path.EndsWith("body.yml", StringComparison.Ordinal));

        Assert.NotEqual(before.ObjectId, after.ObjectId);
        Assert.StartsWith("a:", after.ObjectId, StringComparison.Ordinal);
    }
}
