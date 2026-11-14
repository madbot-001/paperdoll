using System.Buffers.Binary;
using System.Text;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>Forks copied out of a made-up SS14 launcher's content.db into Paperdoll's store.</summary>
public sealed class LauncherSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-launcher-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Copies => Path.Combine(_root, "store", "launcher");

    private TestLauncher NewLauncher(string name = "launcher", string schema = TestLauncher.Schema) => new(Path.Combine(_root, name), schema);

    private static readonly DateTime Monday = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    // A session over the test store, keeping launcher copies in this test's own store folder.
    private (EditorSession Session, SourcedForkStore Store, MemoryForkStore Downloads) NewSession()
    {
        var downloads = TestForks.Store();
        var store = new SourcedForkStore(downloads, Copies);
        return (new EditorSession(store), store, downloads);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Nothing in the launcher's folder was written, made or deleted: no -wal, -shm or -journal file either.
    private static void Untouched(TestLauncher launcher, IReadOnlyList<string> before)
    {
        Assert.Equal(before, TestLauncher.Snapshot(launcher.Folder));
        Assert.DoesNotContain(Directory.EnumerateFiles(launcher.Folder), f => f.EndsWith("-wal", StringComparison.Ordinal) || f.EndsWith("-shm", StringComparison.Ordinal) || f.EndsWith("-journal", StringComparison.Ordinal));
    }

    // What is in the copies folder: the forks' copies, and nothing left part way.
    private string[] CopyFolders() => Directory.Exists(Copies) ? Directory.EnumerateFileSystemEntries(Copies).Select(p => Path.GetFileName(p)).Order().ToArray() : [];

    [Fact]
    public void Only_builds_of_known_forks_are_listed_and_only_the_newest_of_each()
    {
        var launcher = NewLauncher();
        var files = TestLauncher.Build("w");
        launcher.AddBuild("w", TestGameFolders.Commit, Monday.AddDays(-7), files);
        var newest = launcher.AddBuild("w", "fedcba98fedcba98fedcba98fedcba98fedcba98", Monday, files);
        launcher.AddBuild("r", null, Monday.AddDays(-1), TestLauncher.Build("r"));
        // Not offered: content of no fork, a fork Paperdoll does not know, a build still
        // downloading and one listing no files, however recent.
        launcher.AddBuild("custom", null, Monday.AddDays(1), files);
        launcher.AddBuild("AnonymousContentBundle", null, Monday.AddDays(1), files);
        launcher.AddBuild("someone-elses", null, Monday.AddDays(1), files);
        launcher.AddBuild(null, null, Monday.AddDays(1), files);
        launcher.AddBuild("w", null, Monday.AddDays(2), files, downloading: true);
        launcher.AddBuild("w", null, Monday.AddDays(3), new Dictionary<string, byte[]>());
        var before = TestLauncher.Snapshot(launcher.Folder);

        var scan = LauncherCache.Scan([launcher.Folder, Path.Combine(_root, "absent")], [TestForks.W, TestRooms.Fork, TestForks.A]);

        Assert.Empty(scan.Problems);
        Assert.Equal([("w", newest), ("r", 3L)], scan.Builds.Select(b => (b.Fork.Id, b.VersionId)));
        Assert.Equal("fedcba98 (launcher)", scan.Builds[0].ShortVersion);
        Assert.Equal(Monday, scan.Builds[0].LastUsed);
        Assert.Equal(launcher.Folder, scan.Builds[0].Launcher);
        // Without a commit, the build's own hash.
        Assert.Equal(scan.Builds[1].Hash[..8] + " (launcher)", scan.Builds[1].ShortVersion);
        Assert.Null(scan.Builds[1].Commit);
        Untouched(launcher, before);
    }

    [Fact]
    public void A_known_forks_build_is_found_by_the_fork_id_its_servers_give()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("wizards", TestGameFolders.Commit, Monday, TestLauncher.Build("b"));

        var build = Assert.Single(LauncherCache.Scan([launcher.Folder]).Builds);

        Assert.Equal("upstream", build.Fork.Id);
        Assert.Equal("wizards", build.ServerForkId);
    }

    [Fact]
    public void A_content_db_of_another_shape_is_refused_with_a_reason_and_never_guessed_at()
    {
        var noVersion = NewLauncher("old", TestLauncher.Schema.Replace("ForkVersion TEXT NULL,", ""));
        var textData = NewLauncher("text", TestLauncher.Schema.Replace("Data BLOB NOT NULL", "Data TEXT NOT NULL"));
        var noTable = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        File.WriteAllBytes(Path.Combine(noTable, "content.db"), []);
        var notDb = Directory.CreateDirectory(Path.Combine(_root, "junk")).FullName;
        File.WriteAllText(Path.Combine(notDb, "content.db"), "not a database at all, just text that is long enough to be read as a header");

        var scan = LauncherCache.Scan([noVersion.Folder, textData.Folder, noTable, notDb]);

        Assert.Empty(scan.Builds);
        Assert.Equal(4, scan.Problems.Count);
        Assert.Contains("ContentVersion has no ForkVersion TEXT", scan.Problems[0]);
        Assert.Contains("Content has no Data BLOB", scan.Problems[1]);
        Assert.Contains("has no ContentVersion table", scan.Problems[2]);
        Assert.Contains("Could not read the SS14 launcher's", scan.Problems[3]);
        var build = new LauncherBuild(noVersion.Folder, 1, TestForks.W, "w", null, new string('1', 64), Monday);
        Assert.Contains("Paperdoll does not know", Assert.Throws<LauncherException>(() => LauncherImport.Copy(build, new LauncherImports(Copies), ct: Ct)).Message);
        Assert.Empty(CopyFolders());
    }

    [Fact]
    public async Task A_build_is_copied_into_Paperdolls_store_and_the_fork_loads_from_it_with_clothing_and_credits()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("w", TestGameFolders.Commit, Monday, TestLauncher.Build("w"));
        var before = TestLauncher.Snapshot(launcher.Folder);
        var (session, store, downloads) = NewSession();
        await using var _ = session;
        var progress = new List<string>();

        session.Use(await session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromLauncher(launcher.Folder), null, new Recorder(progress), Ct));
        session.DressUpShown = true;
        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, Ct);
        using var picture = session.Render();

        Assert.Equal(ForkSource.FromLauncher(launcher.Folder), session.SourceOf("w"));
        Assert.Equal("01234567 (launcher)", ForkVersions.Short(session.Content!.Commit));
        Assert.Equal(TestGameFolders.Commit, ForkVersions.Commit(session.Content.Commit));
        Assert.True(session.Draws("ClothingNeckScarf", "neck"));
        Assert.True(picture.Width > 0);
        // The scarf's RSI came packed, and its licence still shows.
        Assert.Contains(session.Credits(), c => c.Rsi == "Clothing/Neck/scarf.rsi" && c.License == "CC-BY-SA-3.0" && c.Copyright == "made up");
        Assert.Contains("Copying Fork W from the SS14 launcher", string.Join("\n", progress));
        // Laid out as a game folder: merged files split back, packed RSIs unpacked.
        var copy = Path.Combine(Copies, "w", "Resources");
        Assert.True(File.Exists(Path.Combine(copy, "Prototypes", "wardrobe.yml")));
        Assert.True(File.Exists(Path.Combine(copy, "Textures", "Clothing", "Neck", "scarf.rsi", "equipped-NECK.png")));
        Assert.Empty(Directory.EnumerateFiles(copy, "__merged.*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(copy, "*.rsic", SearchOption.AllDirectories));
        Assert.Equal(["w"], CopyFolders());
        Assert.Empty(downloads.Fetches);
        Assert.Null(await downloads.CommitOfAsync("w", Ct));
        Assert.Equal(session.Content.Commit, (await store.CommitsAsync(Ct))["w"]);
        Untouched(launcher, before);
    }

    [Fact]
    public async Task A_demo_room_is_drawn_from_a_copy_with_packed_and_loose_pictures_and_their_credits()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("r", null, Monday, TestLauncher.Build("r"));
        var before = TestLauncher.Snapshot(launcher.Folder);
        var (session, _, downloads) = NewSession();
        await using var _s = session;

        session.Use(await session.PrepareFromSourceAsync(TestRooms.Fork, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));
        var scene = await session.RoomAsync(Room.TryParse(TestRooms.Room, out var problem) ?? throw new InvalidDataException(problem), Ct);
        var picture = scene.Paint(SKColors.Black);

        Assert.NotEmpty(scene.Entities);
        var credits = scene.CreditsWithin(new Box(-50, -50, 50, 50));
        // A loose picture's credit from attributions.yml, and packed RSIs' from their meta.json.
        Assert.Contains(credits, c => c.Copyright == "Tile made up");
        Assert.Contains(credits, c => c.Copyright == "Crate made up" && c.License == "CC-BY-SA-3.0");
        Assert.True(picture.Below.Width > 0);
        Assert.Empty(downloads.Fetches);
        Untouched(launcher, before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_compression_the_launcher_uses_gives_the_same_copy(long compression)
    {
        var launcher = NewLauncher();
        var build = launcher.AddBuild("r", null, Monday, TestLauncher.Build("r"), compression);
        var expected = NewLauncher("zstd");
        var zstd = expected.AddBuild("r", null, Monday, TestLauncher.Build("r"), compression: 2);

        var copies = new LauncherImports(Copies);
        var one = LauncherImport.Copy(LauncherCache.Newest(launcher.Folder, TestRooms.Fork)!, copies, ct: Ct);
        var other = LauncherImport.Copy(LauncherCache.Newest(expected.Folder, TestRooms.Fork)!, copies, ct: Ct);

        Assert.Equal((1L, 1L), (build, zstd));
        Assert.Equal(Contents(Path.Combine(other, "Resources")), Contents(Path.Combine(one, "Resources")));
        Assert.NotEmpty(Contents(Path.Combine(one, "Resources")));
    }

    // Every file below a folder, by path, with its bytes as text.
    private static Dictionary<string, string> Contents(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));

    [Theory]
    [InlineData(TextChunk.Text)]
    [InlineData(TextChunk.Compressed)]
    [InlineData(TextChunk.International)]
    [InlineData(TextChunk.InternationalCompressed)]
    public void A_packed_RSI_unpacks_to_states_whose_every_frame_is_the_loose_ones(TextChunk form)
    {
        // Latin-1 text for the forms that hold only that, any text for the international ones.
        var author = form is TextChunk.Text or TextChunk.Compressed ? "Zoë" : "Зоя 🦎";
        var meta = Encoding.UTF8.GetBytes($$"""
            {"version":1,"license":"CC-BY-SA-3.0","copyright":"{{author}}","size":{"x":32,"y":48},"states":[
              {"name":"one"},
              {"name":"four","directions":4},
              {"name":"eight","directions":8},
              {"name":"walk","directions":4,"delays":[[0.1,0.1,0.1],[0.1],[],[0.2,0.2]]},
              {"name":"blink","delays":[[0.1,0.1,0.1,0.1,0.1]]}]}
            """);
        var loose = new Dictionary<string, byte[]>
        {
            ["one"] = TestLauncher.StatePng(32, 48, 1, 1),
            ["four"] = TestLauncher.StatePng(32, 48, 4, 2),
            ["eight"] = TestLauncher.StatePng(32, 48, 8, 3),
            ["walk"] = TestLauncher.StatePng(32, 48, 7, 4),
            // Laid in one row, as some RSI folders have them.
            ["blink"] = TestLauncher.StatePng(32, 48, 5, 5, columns: 5),
        };
        var packed = TestLauncher.PackRsi(meta, s => loose[s], form);

        var unpacked = Rsic.Unpack(packed);

        Assert.Equal(meta, unpacked.MetaJson);
        Assert.Contains(author, Encoding.UTF8.GetString(unpacked.MetaJson));
        Assert.Equal(loose.Keys, unpacked.States.Select(s => s.State));
        var rsi = RsiMeta.Parse(unpacked.MetaJson);
        foreach (var (state, png) in unpacked.States)
        {
            using var original = TestLauncher.Decode(loose[state]);
            using var rebuilt = TestLauncher.Decode(png);
            var info = rsi.States[state];
            var frames = info.FramesPerDirection.Sum();
            Assert.Equal(Rsic.Grid(frames), (rebuilt.Width / 32, rebuilt.Height / 48));
            for (var direction = 0; direction < info.Directions; direction++)
            {
                for (var frame = 0; frame < info.FramesPerDirection[direction]; frame++)
                {
                    var from = rsi.FrameRect(info, original.Width, (Direction)direction, frame);
                    var to = rsi.FrameRect(info, rebuilt.Width, (Direction)direction, frame);
                    Assert.Equal(Pixels(original, from), Pixels(rebuilt, to));
                    // And as Paperdoll draws it.
                    using var drawnFrom = rsi.Frame(info, loose[state], (Direction)direction, frame)!;
                    using var drawnTo = rsi.Frame(info, png, (Direction)direction, frame)!;
                    Assert.Equal(drawnFrom.Bytes, drawnTo.Bytes);
                }
            }
        }
    }

    private static uint[] Pixels(SKBitmap bitmap, SKRectI rect)
    {
        var pixels = new List<uint>();
        for (var y = rect.Top; y < rect.Bottom; y++)
        for (var x = rect.Left; x < rect.Right; x++)
            pixels.Add((uint)bitmap.GetPixel(x, y));
        return [.. pixels];
    }

    [Fact]
    public void A_packed_RSI_that_is_too_large_broken_or_unlike_its_meta_is_refused()
    {
        var meta = Encoding.UTF8.GetBytes("""{"version":1,"size":{"x":32,"y":32},"states":[{"name":"a"}]}""");
        var good = TestLauncher.PackRsi(meta, _ => TestLauncher.StatePng(32, 32, 1, 0), TextChunk.Text);
        Assert.Single(Rsic.Unpack(good).States);

        // A header claiming a picture far larger than any RSI's, with a CRC that checks out.
        var huge = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(16), 20000);
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(20), 20000);
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(29), Rsic.Crc32(huge.AsSpan(12, 17)));
        Assert.Contains("larger than Paperdoll takes", Assert.Throws<FormatException>(() => Rsic.Unpack(huge)).Message);

        var badCrc = TestLauncher.WithChunk(good, "tEXt", Encoding.Latin1.GetBytes("Comment\0x"), badCrc: true);
        Assert.Contains("fails its CRC check", Assert.Throws<FormatException>(() => Rsic.Unpack(badCrc)).Message);
        Assert.Contains("runs past its end", Assert.Throws<FormatException>(() => Rsic.Unpack(good[..^20])).Message);
        Assert.Contains("ends part way", Assert.Throws<FormatException>(() => Rsic.Unpack(good[..^12])).Message);
        Assert.Contains("is not a PNG", Assert.Throws<FormatException>(() => Rsic.Unpack([1, 2, 3])).Message);
        var twice = TestLauncher.WithText(good, Rsic.MetaKeyword, "{}", TextChunk.Text);
        Assert.Contains("more than one", Assert.Throws<FormatException>(() => Rsic.Unpack(twice)).Message);

        // Meta the engine itself refuses, and a picture not the size its frames take.
        string[] wrong =
        [
            """{"size":{"x":32,"y":32},"states":[{"name":"a","directions":3}]}""",
            """{"size":{"x":32,"y":32},"states":[{"name":"a","directions":4,"delays":[[1]]}]}""",
            """{"size":{"x":32,"y":32},"states":[{"name":"a"},{"name":"a"}]}""",
            """{"size":{"x":0,"y":32},"states":[{"name":"a"}]}""",
            """{"size":{"x":32,"y":32},"states":[{"name":"a"},{"name":"b"}]}""",
            """not json""",
        ];
        var plain = TestLauncher.Decode(TestLauncher.StatePng(32, 32, 1, 0));
        using var encoded = plain.Encode(SKEncodedImageFormat.Png, 100);
        foreach (var text in wrong)
            Assert.Throws<FormatException>(() => Rsic.Unpack(TestLauncher.WithText(encoded.ToArray(), Rsic.MetaKeyword, text, TextChunk.Text)));
        Assert.Contains("has no robusttoolbox_rsic_meta", Assert.Throws<FormatException>(() => Rsic.Unpack(encoded.ToArray())).Message);
    }

    [Fact]
    public void A_merged_file_splits_back_into_the_files_it_was_made_of()
    {
        var yml = TestLauncher.Merge([("Prototypes/Entities/a.yml", "- type: a\n  id: A\r\n"u8.ToArray()), ("Prototypes/Entities/b.yml", "- type: b\n# END Prototypes/Entities/b.yml\n  id: B"u8.ToArray())], yml: true);
        var ftl = TestLauncher.Merge([("Locale/en-US/x.ftl", "x = X\n"u8.ToArray()), ("Locale/en-US/y.ftl", "# BEGIN not a file\ny = Y\n"u8.ToArray())], yml: false);

        var files = MergedText.Split("Prototypes/Entities", "yml", yml);
        var strings = MergedText.Split("Locale/en-US", "ftl", ftl);
        var unmarked = MergedText.Split("Prototypes", "yml", "- type: c\n"u8.ToArray());

        Assert.Equal([("Prototypes/Entities/a.yml", "- type: a\n  id: A\n"), ("Prototypes/Entities/b.yml", "- type: b\n# END Prototypes/Entities/b.yml\n  id: B\n")],
            files.Select(f => (f.Path, Encoding.UTF8.GetString(f.Data))));
        Assert.Equal([("Locale/en-US/x.ftl", "x = X\n"), ("Locale/en-US/y.ftl", "# BEGIN not a file\ny = Y\n")], strings.Select(f => (f.Path, Encoding.UTF8.GetString(f.Data))));
        Assert.Equal([("Prototypes/merged.yml", "- type: c\n")], unmarked.Select(f => (f.Path, Encoding.UTF8.GetString(f.Data))));
    }

    [Theory]
    [InlineData("--- # BEGIN Prototypes/Other/a.yml\nx\n# END Prototypes/Other/a.yml\n", "not a .yml file in Prototypes/Entities")]
    [InlineData("--- # BEGIN Prototypes/Entities/../../a.yml\nx\n# END Prototypes/Entities/../../a.yml\n", "not a .yml file")]
    [InlineData("--- # BEGIN Prototypes/Entities/Deeper/a.yml\nx\n# END Prototypes/Entities/Deeper/a.yml\n", "not a .yml file")]
    [InlineData("--- # BEGIN /etc/a.yml\nx\n# END /etc/a.yml\n", "not a .yml file")]
    [InlineData("--- # BEGIN Prototypes/Entities/a.txt\nx\n# END Prototypes/Entities/a.txt\n", "not a .yml file")]
    [InlineData("--- # BEGIN Prototypes/Entities/a.yml\nx\n", "ends inside")]
    [InlineData("stray\n--- # BEGIN Prototypes/Entities/a.yml\nx\n# END Prototypes/Entities/a.yml\n", "outside every file")]
    [InlineData("--- # BEGIN Prototypes/Entities/a.yml\n# END Prototypes/Entities/a.yml\n--- # BEGIN Prototypes/Entities/a.yml\n# END Prototypes/Entities/a.yml\n", "twice")]
    public void A_merged_file_naming_a_file_outside_its_folder_or_not_laid_out_as_packed_is_refused(string merged, string why)
    {
        var problem = Assert.Throws<FormatException>(() => MergedText.Split("Prototypes/Entities", "yml", Encoding.UTF8.GetBytes(merged)));
        Assert.Contains(why, problem.Message);
    }

    // Records progress as it is reported, with no thread in between.
    private sealed class Recorder(List<string> lines, Action<string>? then = null) : IProgress<string>
    {
        public void Report(string value)
        {
            lines.Add(value);
            then?.Invoke(value);
        }
    }

    public static TheoryData<string, string> BadBuilds => new()
    {
        { "checksum", "does not match its checksum; close the SS14 launcher and the game, then try again" },
        { "truncated", LauncherCache.TryAgain },
        { "size", "is not the size recorded for it; close the SS14 launcher" },
        { "bomb", "decodes to more than its recorded size; close the SS14 launcher" },
        { "huge", "is larger than 32 MB" },
        { "Textures/../evil.png", "which is not a path within the game's files" },
        { "Textures/a\\b.png", "which is not a path within the game's files" },
        { "Textures/C:/b.png", "which is not a path within the game's files" },
        { "Textures/con.png", "which is not a path within the game's files" },
        { "Textures/a\u0001.png", "which is not a path within the game's files" },
        { "Textures//b.png", "which is not a path within the game's files" },
        { "Prototypes/./b.yml", "which is not a path within the game's files" },
        { "picture", "larger than Paperdoll takes" },
        { "merged", "is not a .yml file in Prototypes" },
        { "twice", "twice; Paperdoll copies nothing from it" },
        { "state", "has a state named" },
    };

    [Theory]
    [MemberData(nameof(BadBuilds))]
    public async Task A_bad_build_stops_the_copy_with_a_reason_keeping_nothing_of_it_and_the_copy_from_before(string bad, string why)
    {
        var launcher = NewLauncher();
        launcher.AddBuild("w", TestGameFolders.Commit, Monday.AddDays(-1), TestLauncher.Build("w"));
        var (session, _, _) = NewSession();
        await using var _s = session;
        session.Use(await session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));
        var kept = TestLauncher.Snapshot(Path.Combine(Copies, "w"));
        var version = session.Content!.Commit;

        var files = TestLauncher.Build("w");
        Func<string, TestBlob, TestBlob>? change = null;
        // A file of this build only: one the build before shares is kept once, as it was then.
        const string picked = "Textures/fresh.png";
        files[picked] = TestLauncher.StatePng(64, 64, 1, 9);
        switch (bad)
        {
            case "checksum":
                change = (path, blob) => path == picked ? blob with { Hash = new byte[32] } : blob;
                break;
            case "truncated":
                change = (path, blob) => path == picked ? blob with { Data = blob.Data[..(blob.Data.Length / 2)] } : blob;
                break;
            case "size":
                change = (path, blob) => path == picked ? blob with { Size = blob.Size + 1 } : blob;
                break;
            case "bomb":
                // Ten million bytes recorded as a hundred.
                var zeros = new byte[10_000_000];
                files["Textures/bomb.png"] = zeros;
                change = (path, blob) => path == "Textures/bomb.png" ? blob with { Size = 100, Hash = Blake2Fast.Blake2b.ComputeHash(32, zeros.AsSpan(0, 100)) } : blob;
                break;
            case "huge":
                files["Textures/huge.png"] = [1, 2, 3];
                change = (path, blob) => path == "Textures/huge.png" ? blob with { Size = 64L * 1024 * 1024 } : blob;
                break;
            case "picture":
                var packed = files["Textures/Clothing/Neck/scarf.rsic"];
                BinaryPrimitives.WriteUInt32BigEndian(packed.AsSpan(16), 20000);
                BinaryPrimitives.WriteUInt32BigEndian(packed.AsSpan(20), 20000);
                BinaryPrimitives.WriteUInt32BigEndian(packed.AsSpan(29), Rsic.Crc32(packed.AsSpan(12, 17)));
                break;
            case "merged":
                files["Prototypes/Other/__merged.yml"] = "--- # BEGIN Prototypes/a.yml\nx\n# END Prototypes/a.yml\n"u8.ToArray();
                break;
            case "twice":
                files["Textures/Clothing/Neck/scarf.rsi/meta.json"] = "{}"u8.ToArray();
                break;
            case "state":
                files["Textures/odd.rsic"] = TestLauncher.PackRsi("""{"size":{"x":32,"y":32},"states":[{"name":"../up"}]}"""u8.ToArray(), _ => TestLauncher.StatePng(32, 32, 1, 0), TextChunk.Text);
                break;
            default:
                files[bad] = [1];
                break;
        }
        launcher.AddBuild("w", TestGameFolders.Commit, Monday, files, change: change);
        var before = TestLauncher.Snapshot(launcher.Folder);

        var problem = await Assert.ThrowsAsync<LauncherException>(() => session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromLauncher(launcher.Folder), session.CharacterSource(), ct: Ct));

        Assert.Contains(why, problem.Message);
        Assert.Equal(["w"], CopyFolders());
        Assert.Equal(kept, TestLauncher.Snapshot(Path.Combine(Copies, "w")));
        Assert.Equal(ForkSource.FromLauncher(launcher.Folder), session.SourceOf("w"));
        Assert.Equal(version, await session.Store.CommitOfAsync("w", Ct));
        Untouched(launcher, before);
    }

    [Fact]
    public async Task Cancel_part_way_through_a_copy_keeps_nothing_of_it()
    {
        var launcher = NewLauncher();
        var files = TestLauncher.Build("w");
        for (var i = 0; i < 450; i++)
            files[$"Textures/Many/{i}.png"] = TestLauncher.StatePng(4, 4, 1, i);
        launcher.AddBuild("w", null, Monday, files);
        var before = TestLauncher.Snapshot(launcher.Folder);
        var (session, _, _) = NewSession();
        await using var _s = session;
        using var cancel = new CancellationTokenSource();
        var reports = new List<string>();
        var progress = new Recorder(reports, text =>
        {
            if (text.Contains(": 200 of ", StringComparison.Ordinal))
                cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PrepareFromSourceAsync(TestForks.W, ForkSource.FromLauncher(launcher.Folder), null, progress, cancel.Token));

        Assert.Contains(reports, r => r.StartsWith("Copying Fork W from the SS14 launcher: 200 of", StringComparison.Ordinal));
        Assert.Empty(CopyFolders());
        Assert.Equal(ForkSource.Download, session.SourceOf("w"));
        Untouched(launcher, before);
    }

    [Fact]
    public async Task Update_copies_the_newest_build_again_and_Remove_deletes_only_Paperdolls_copy()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", TestGameFolders.Commit, Monday.AddDays(-1), TestLauncher.Build("a"));
        var (session, store, downloads) = NewSession();
        await using var _s = session;
        session.Use(await session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));
        Assert.Equal(["Human", "Lizard"], session.Selectable().Select(s => s.Id).Order());
        var first = session.Content!.Commit;

        // The launcher has since run a newer build, with a species more.
        var newer = TestLauncher.Build("a");
        newer["Prototypes/moth.yml"] = "- type: species\n  id: Moth\n  name: species-name-moth\n  roundStart: true\n  dollPrototype: Doll\n"u8.ToArray();
        newer = TestLauncher.Package(TestForks.Store().FilesOf("a").ToDictionary(f => f.Key["Resources/".Length..], f => f.Value)
            .Append(new KeyValuePair<string, byte[]>("Prototypes/moth.yml", newer["Prototypes/moth.yml"])).ToDictionary());
        launcher.AddBuild("a", "89abcdef89abcdef89abcdef89abcdef89abcdef", Monday, newer);
        var before = TestLauncher.Snapshot(launcher.Folder);

        session.Use(await session.PrepareFromSourceAsync(TestForks.A, session.SourceOf("a"), session.CharacterSource(), ct: Ct));

        Assert.Contains("Moth", session.Selectable().Select(s => s.Id));
        Assert.Equal("89abcdef (launcher)", ForkVersions.Short(session.Content!.Commit));
        Assert.NotEqual(first, session.Content.Commit);
        Assert.Equal(["a"], CopyFolders());

        // Remove, with another fork open: the copy goes, the launcher stays as it is.
        session.Use(await session.PrepareForkAsync(TestForks.B, update: false, null, ct: Ct));
        var aside = Path.Combine(_root, "store", "other.txt");
        File.WriteAllText(aside, "Paperdoll's own file beside the copies");
        await session.RemoveForkAsync(TestForks.A, Ct);

        Assert.Empty(CopyFolders());
        Assert.True(File.Exists(aside));
        Assert.Equal(ForkSource.Download, session.SourceOf("a"));
        Assert.Null(await store.CommitOfAsync("a", Ct));
        Assert.NotNull(await downloads.CommitOfAsync("b", Ct));
        Untouched(launcher, before);
    }

    [Fact]
    public async Task A_copy_is_read_at_the_next_start_without_the_launcher_and_left_part_way_copies_are_tidied()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        IReadOnlyDictionary<string, ForkSource> sources;
        string version;
        var (first, _, _) = NewSession();
        await using (first)
        {
            first.Use(await first.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));
            sources = first.Sources();
            version = first.Content!.Commit;
        }
        // Left behind by a copy stopped when Paperdoll was: one part way, and one set aside.
        Directory.CreateDirectory(Path.Combine(Copies, $".copying-a-{Guid.NewGuid():N}", "Resources"));
        Directory.CreateDirectory(Path.Combine(Copies, $".previous-a-{Guid.NewGuid():N}"));
        // Another fork's, whose id starts the same, is left alone.
        var others = Directory.CreateDirectory(Path.Combine(Copies, $".previous-a-b-{Guid.NewGuid():N}")).Name;
        // The launcher has dropped the build, or is gone altogether.
        Directory.Delete(launcher.Folder, recursive: true);

        var (second, _, _) = NewSession();
        await using var _s = second;
        await second.RestoreSourcesAsync(sources, Ct);
        await second.LoadForkAsync(TestForks.A, update: false, ct: Ct);

        Assert.Equal(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromLauncher(launcher.Folder) }, sources);
        Assert.Equal(version, second.Content!.Commit);
        Assert.Equal([others, "a"], CopyFolders());
        Directory.Delete(Path.Combine(Copies, others));
        Assert.Null(second.FolderProblem("a"));

        // A copy set aside goes back in place when the copy itself is missing.
        Directory.Move(Path.Combine(Copies, "a"), Path.Combine(Copies, $".previous-a-{Guid.NewGuid():N}"));
        new LauncherImports(Copies).Recover("a");
        Assert.Equal(["a"], CopyFolders());
    }

    [Fact]
    public async Task A_copy_that_is_gone_is_kept_as_the_source_with_what_to_do()
    {
        var (session, store, _) = NewSession();
        await using var _s = session;
        await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromLauncher(Path.Combine(_root, "launcher")) }, Ct);

        Assert.Equal(ForkSource.FromLauncher(Path.Combine(_root, "launcher")), session.SourceOf("a"));
        Assert.Contains("Choose From launcher... in Fork > Forks... to copy it again", session.FolderProblem("a"));
        Assert.Null(await store.CommitOfAsync("a", Ct));
    }

    [Fact]
    public async Task Use_download_deletes_the_copy_and_matching_a_server_needs_the_builds_own_commit()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", TestGameFolders.Commit, Monday, TestLauncher.Build("a"));
        var (session, store, _) = NewSession();
        await using var _s = session;
        session.Use(await session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));

        var copy = store.LauncherOf("a")!;
        Assert.Equal(session.Content!.Commit, await copy.SyncToCommitAsync(TestForks.A, TestGameFolders.Commit.ToUpperInvariant(), Ct));
        var other = await Assert.ThrowsAsync<LauncherException>(() => copy.SyncToCommitAsync(TestForks.A, "89abcdef89abcdef89abcdef89abcdef89abcdef", Ct));
        Assert.Contains("is a copy of the SS14 launcher's 01234567 (launcher), not of the server's version 89abcdef. Join that server once with the launcher", other.Message);

        await session.UseDownloadAsync(TestForks.A, Ct);
        Assert.Empty(CopyFolders());
        Assert.Equal(ForkSource.Download, session.SourceOf("a"));
    }

    [Fact]
    public void Nothing_is_ever_written_outside_Paperdolls_folder_for_launcher_copies()
    {
        var copies = new LauncherImports(Copies);
        var staging = copies.NewStaging("w");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);

        Assert.Throws<InvalidOperationException>(() => copies.Inside(Path.Combine(Copies, "..", "outside")));
        Assert.Throws<InvalidOperationException>(() => copies.Inside(Copies));
        Assert.Throws<InvalidOperationException>(() => copies.Write(staging, ["..", "..", "outside", "x"], [1]));
        Assert.Throws<InvalidOperationException>(() => copies.Write(outside, ["x"], [1]));
        Assert.Throws<InvalidOperationException>(() => copies.DeleteStaging(outside));
        Assert.Throws<ArgumentException>(() => copies.FolderOf(".."));
        Assert.Throws<ArgumentException>(() => copies.Delete("a/b"));
        Assert.Throws<LauncherException>(() => LauncherPaths.Parts("Textures/../../outside/x"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));

        // Within it, a file is never written over.
        copies.Write(staging, ["Resources", "a"], [1]);
        Assert.Throws<IOException>(() => copies.Write(staging, ["Resources", "a"], [2]));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(staging, "Resources", "a")));
    }

    [Fact]
    public void Launcher_versions_show_the_commit_or_the_builds_hash_marked_as_the_launchers()
    {
        var hash = "aabbccdd" + new string('e', 56);
        Assert.Equal("01234567 (launcher)", ForkVersions.Short(ForkVersions.OfLauncher(TestGameFolders.Commit, hash, 5)));
        Assert.Equal("aabbccdd (launcher)", ForkVersions.Short(ForkVersions.OfLauncher(null, hash, 5)));
        Assert.Null(ForkVersions.Commit(ForkVersions.OfLauncher(null, hash, 5)));
        Assert.Equal(TestGameFolders.Commit, ForkVersions.Commit(ForkVersions.OfLauncher(TestGameFolders.Commit, hash, 5)));
        Assert.True(ForkVersions.IsLauncher(ForkVersions.OfLauncher(null, hash, 5)));
        Assert.False(ForkVersions.IsFolder(ForkVersions.OfLauncher(null, hash, 5)));
    }

    // Makes a whole copy of fork a in the copies folder, through a session, and returns its folder.
    private async Task<string> CopyOfA(TestLauncher launcher)
    {
        var (session, _, _) = NewSession();
        await using (session)
            await session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct);
        return Path.Combine(Copies, "a");
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string AnyTexture(string copy) =>
        Directory.EnumerateFiles(Path.Combine(copy, "Resources", "Textures"), "*.png", SearchOption.AllDirectories).First();

    [Fact]
    public async Task A_copy_whose_files_do_not_add_up_to_its_record_is_never_read_and_must_be_copied_again()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        // Left half deleted, its record and top folders still there.
        File.Delete(AnyTexture(copy));

        var (session, store, _) = NewSession();
        await using var _s = session;
        await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromLauncher(launcher.Folder) }, Ct);

        Assert.Contains("is not whole", session.FolderProblem("a"));
        Assert.Contains("Choose From launcher... in Fork > Forks... to copy it again", session.FolderProblem("a"));
        Assert.Null(await store.CommitOfAsync("a", Ct));
        await Assert.ThrowsAsync<LauncherException>(() => session.LoadForkAsync(TestForks.A, update: false, ct: Ct));
        Assert.Null(session.Fork);
    }

    [Fact]
    public async Task After_a_stop_part_way_through_taking_a_new_copy_down_the_whole_copy_from_before_is_kept()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        var whole = Contents(copy);
        var previous = Path.Combine(Copies, $".previous-a-{Guid.NewGuid():N}");
        CopyFolder(copy, previous);
        // The new copy in place was being deleted when Paperdoll stopped.
        File.Delete(AnyTexture(copy));

        new LauncherImports(Copies).Recover("a");

        Assert.Equal(["a"], CopyFolders());
        Assert.Equal(whole, Contents(copy));
        Assert.Equal("a", new LauncherImports(Copies).ReadRecord("a").ForkId);
    }

    [Fact]
    public async Task After_a_stop_between_the_steps_of_an_undo_the_copy_from_before_goes_back_and_the_rest_is_deleted()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        var whole = Contents(copy);
        // The new copy renamed out of place and half deleted, the old one not yet back, and an
        // unfinished copy beside them.
        var previous = Path.Combine(Copies, $".previous-a-{Guid.NewGuid():N}");
        Directory.Move(copy, previous);
        var deleting = Path.Combine(Copies, $".deleting-a-{Guid.NewGuid():N}");
        CopyFolder(previous, deleting);
        File.Delete(AnyTexture(deleting));
        Directory.CreateDirectory(Path.Combine(Copies, $".copying-a-{Guid.NewGuid():N}", "Resources"));

        new LauncherImports(Copies).Recover("a");

        Assert.Equal(["a"], CopyFolders());
        Assert.Equal(whole, Contents(copy));
    }

    [Fact]
    public async Task Of_several_copies_set_aside_the_newest_whole_one_goes_back_and_a_whole_copy_in_place_stays()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        var record = Path.Combine(copy, LauncherRecord.FileName);
        var copied = System.Text.Json.JsonSerializer.Deserialize<LauncherRecord>(File.ReadAllBytes(record))!;
        string SetAside(long copiedAt, bool whole)
        {
            var folder = Path.Combine(Copies, $".previous-a-{Guid.NewGuid():N}");
            CopyFolder(copy, folder);
            File.WriteAllBytes(Path.Combine(folder, LauncherRecord.FileName), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(copied with { CopiedAt = copiedAt }));
            if (!whole)
                File.Delete(AnyTexture(folder));
            return folder;
        }
        SetAside(1, whole: true);
        SetAside(2, whole: true);
        SetAside(3, whole: false);

        // A whole copy in place, as after a stop once a new copy was put in: it stays.
        new LauncherImports(Copies).Recover("a");
        Assert.Equal(["a"], CopyFolders());
        Assert.Equal(copied.CopiedAt, new LauncherImports(Copies).ReadRecord("a").CopiedAt);

        SetAside(1, whole: true);
        SetAside(2, whole: true);
        SetAside(3, whole: false);
        Directory.Delete(copy, recursive: true);
        new LauncherImports(Copies).Recover("a");

        Assert.Equal(["a"], CopyFolders());
        Assert.Equal(2, new LauncherImports(Copies).ReadRecord("a").CopiedAt);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Removing_a_copy_takes_it_out_of_place_at_once_even_when_part_of_it_cannot_be_deleted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Needs a folder whose files the user cannot delete.");
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var (session, _, _) = NewSession();
        await using var _s = session;
        await session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct);
        var locked = Path.GetDirectoryName(AnyTexture(Path.Combine(Copies, "a")))!;
        var mode = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await session.RemoveForkAsync(TestForks.A, Ct);

            Assert.Equal(ForkSource.Download, session.SourceOf("a"));
            Assert.StartsWith(".deleting-a-", Assert.Single(CopyFolders()));
        }
        finally
        {
            foreach (var folder in Directory.EnumerateDirectories(Copies, "*", SearchOption.AllDirectories))
                File.SetUnixFileMode(folder, mode);
        }
        new LauncherImports(Copies).Recover("a");
        Assert.Empty(CopyFolders());
    }

    [Fact]
    public async Task A_game_folder_in_Paperdolls_store_or_holding_it_is_refused_and_nothing_of_it_is_moved_or_deleted()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var (session, store, _) = NewSession();
        await using var _s = session;
        session.Use(await session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, ct: Ct));
        var copy = Path.Combine(Copies, "a");
        var kept = TestLauncher.Snapshot(copy);

        // The fork's own copy, as its game folder: once it loaded, the copy would have been deleted.
        var own = await Assert.ThrowsAsync<GameFolderException>(() => session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromFolder(copy), session.CharacterSource(), ct: Ct));
        Assert.Contains("is in Paperdoll's own folder", own.Message);
        await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.A, copy, Ct));
        // Another fork's copy, which Update or Remove of that fork would move or delete; its Resources too.
        await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.B, copy, Ct));
        await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.B, Path.Combine(copy, "Resources"), Ct));
        // Through a link leading there.
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, copy);
        await Assert.ThrowsAsync<GameFolderException>(() => session.UseFolderAsync(TestForks.B, link, Ct));
        // Kept from an earlier run: not read.
        await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["b"] = ForkSource.FromFolder(copy) }, Ct);
        Assert.Contains("is in Paperdoll's own folder", session.FolderProblem("b"));
        Assert.Null(await store.CommitOfAsync("b", Ct));

        Assert.Equal(ForkSource.FromLauncher(launcher.Folder), session.SourceOf("a"));
        Assert.Equal(kept, TestLauncher.Snapshot(copy));

        // A game folder holding Paperdoll's store is refused as well.
        var game = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "game")).FullName, "b");
        await using var inside = new EditorSession(new SourcedForkStore(TestForks.Store(), Path.Combine(game, "paperdoll", "launcher")));
        Assert.Contains("or holds it", (await Assert.ThrowsAsync<GameFolderException>(() => inside.UseFolderAsync(TestForks.B, game, Ct))).Message);
    }

    [Fact]
    public async Task A_launcher_file_that_changes_during_the_copy_stops_it_and_nothing_is_kept()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var (session, _, _) = NewSession();
        await using var _s = session;
        // The launcher writes to a table Paperdoll does not read, as the copy starts: the rows
        // read stay the same, but the file grows, whatever the file system's clock.
        var size = new FileInfo(launcher.Db).Length;
        var progress = new Recorder([], text =>
        {
            if (text.Contains(": 0 of ", StringComparison.Ordinal))
                launcher.Execute("INSERT INTO ContentEngineDependency(VersionId, ModuleName, ModuleVersion) VALUES (1, 'Robust', printf('%.*c', 100000, 'x'))");
        });

        var problem = await Assert.ThrowsAsync<LauncherException>(() => session.PrepareFromSourceAsync(TestForks.A, ForkSource.FromLauncher(launcher.Folder), null, progress, Ct));

        Assert.NotEqual(size, new FileInfo(launcher.Db).Length);
        Assert.Contains("changed while Fork A was being copied", problem.Message);
        Assert.Contains(LauncherCache.TryAgain, problem.Message);
        Assert.Empty(CopyFolders());
        Assert.Equal(ForkSource.Download, session.SourceOf("a"));
    }

    [Fact]
    public void A_build_holding_none_of_the_files_Paperdoll_reads_is_not_copied()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, new Dictionary<string, byte[]> { ["Audio/beep.ogg"] = [1, 2, 3] });

        var problem = Assert.Throws<LauncherException>(() => LauncherImport.Copy(LauncherCache.Newest(launcher.Folder, TestForks.A)!, new LauncherImports(Copies), ct: Ct));

        Assert.Contains("holds none of the files Paperdoll reads", problem.Message);
        Assert.Empty(CopyFolders());
    }

    [Fact]
    public async Task An_update_that_loads_keeps_its_new_copy_marked_and_one_stopped_before_loading_gives_way_to_the_copy_that_loaded()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", TestGameFolders.Commit, Monday.AddDays(-1), TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        var loaded = new LauncherImports(Copies).ReadRecord("a").Build;
        Assert.True(File.Exists(Path.Combine(copy, LauncherRecord.LoadedName)));
        var newer = TestLauncher.Build("a");
        newer["Textures/extra.png"] = TestLauncher.StatePng(8, 8, 1, 3);
        launcher.AddBuild("a", "89abcdef89abcdef89abcdef89abcdef89abcdef", Monday, newer);

        // Update copies the newer build and puts it in place; Paperdoll stops before it loads.
        var copies = new LauncherImports(Copies);
        copies.SwapIn("a", LauncherImport.Copy(LauncherCache.Newest(launcher.Folder, TestForks.A)!, copies, ct: Ct));
        Assert.False(File.Exists(Path.Combine(copy, LauncherRecord.LoadedName)));
        copies.Recover("a");

        Assert.Equal(["a"], CopyFolders());
        Assert.Equal(loaded, copies.ReadRecord("a").Build);

        // Update again, and this time it loads: the new copy stays, marked, the old one goes.
        var (session, _, _) = NewSession();
        await using var _s = session;
        await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromLauncher(launcher.Folder) }, Ct);
        session.Use(await session.PrepareFromSourceAsync(TestForks.A, session.SourceOf("a"), null, ct: Ct));

        Assert.Equal(["a"], CopyFolders());
        Assert.NotEqual(loaded, copies.ReadRecord("a").Build);
        Assert.True(File.Exists(Path.Combine(copy, LauncherRecord.LoadedName)));
        Assert.Equal("89abcdef (launcher)", ForkVersions.Short(session.Content!.Commit));
    }

    [Fact]
    public async Task Files_the_operating_system_leaves_in_a_copy_keep_it_whole_and_are_never_read_but_a_missing_file_does_not()
    {
        var launcher = NewLauncher();
        launcher.AddBuild("a", null, Monday, TestLauncher.Build("a"));
        var copy = await CopyOfA(launcher);
        File.WriteAllText(Path.Combine(copy, "Resources", ".DS_Store"), "Finder");
        File.WriteAllText(Path.Combine(copy, "Resources", "Prototypes", "Thumbs.db"), "Explorer");
        File.WriteAllText(Path.Combine(copy, "Resources", "Textures", "DESKTOP.INI"), "Explorer");

        var (session, _, _) = NewSession();
        await using (session)
        {
            await session.RestoreSourcesAsync(new Dictionary<string, ForkSource> { ["a"] = ForkSource.FromLauncher(launcher.Folder) }, Ct);
            await session.LoadForkAsync(TestForks.A, update: false, ct: Ct);
            Assert.Null(session.FolderProblem("a"));
            Assert.Empty(session.FolderSkipped("a"));
            Assert.DoesNotContain(await session.Store.ListAsync("a", [ForkContent.PrototypesFolder, ForkContent.TexturesFolder], Ct), e => e.Path.EndsWith("Thumbs.db", StringComparison.Ordinal) || e.Path.EndsWith("DESKTOP.INI", StringComparison.Ordinal));
        }

        File.Delete(AnyTexture(copy));
        Assert.Contains("is not whole", Assert.Throws<LauncherException>(() => new LauncherImports(Copies).ReadRecord("a")).Message);
    }

    [Fact]
    public async Task A_game_folders_operating_system_files_are_left_out_quietly()
    {
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(_root, "game")).FullName, "a");
        File.WriteAllText(Path.Combine(folder, "Resources", "Prototypes", ".DS_Store"), "Finder");
        await using var session = new EditorSession(new SourcedForkStore(TestForks.Store()));
        await session.UseFolderAsync(TestForks.A, folder, Ct);

        await session.LoadForkAsync(TestForks.A, update: false, ct: Ct);

        Assert.Empty(session.FolderSkipped("a"));
        Assert.Empty(session.Content!.Prototypes.Problems);
        Assert.DoesNotContain(await session.Store.ListAsync("a", [ForkContent.PrototypesFolder], Ct), e => e.Path.EndsWith(".DS_Store", StringComparison.Ordinal));
    }
}
