using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Real exported characters, kept out of git in tests/private/. Skipped when the folder is empty
/// or network tests are off (PAPERDOLL_NETWORK_TESTS=1).
/// </summary>
public sealed class PrivateFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-private-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    public static string? PrivateFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "private");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Each private export opens against its fork's current data: old files convert to the new
    /// model, pass the game's rules, write back, read again and draw. Set PAPERDOLL_RENDER_OUT to
    /// keep the pictures.
    /// </summary>
    [Fact]
    public async Task Private_exports_import_into_their_forks()
    {
        var folder = PrivateFolder();
        var files = folder == null ? [] : Directory.GetFiles(folder, "*.yml");
        Assert.SkipWhen(files.Length == 0, "No private character files in tests/private.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var loaded = new Dictionary<string, (ForkContent Content, MemoryTextures Textures)>();
        var output = Environment.GetEnvironmentVariable("PAPERDOLL_RENDER_OUT");

        foreach (var path in files)
        {
            var file = CharacterFile.Parse(await File.ReadAllTextAsync(path, ct));
            var fork = KnownForks.FindByServerForkId(file.ForkId ?? "") ?? KnownForks.Find("deltav")!;
            if (!loaded.TryGetValue(fork.Id, out var fork_))
            {
                await store.SyncAsync(fork, ct);
                var content = await ForkContent.LoadAsync(store, fork, ct: ct);
                await using var reader = store.OpenReader();
                loaded[fork.Id] = fork_ = (content, await MemoryTextures.LoadAsync(content, reader, ct));
            }

            var markingsBefore = file.IsOldModel;
            var fixes = CharacterRules.EnsureValid(file, fork_.Content.Characters, fork);
            var again = CharacterFile.Parse(file.ToYaml());
            var look = again.ReadLook(fork_.Content.Characters);

            TestContext.Current.SendDiagnosticMessage(
                $"{Path.GetFileName(path)} -> {fork.Name}: {look.Species}, old model {markingsBefore}, " +
                $"{look.Markings.Values.Sum(l => l.Values.Sum(m => m.Count))} markings; " +
                string.Join(" | ", fixes.Select(f => $"{f.Field}: {f.Message}")));
            Assert.False(again.IsOldModel);
            Assert.Equal("2", again.Version);

            var renderer = new PaperdollRenderer(fork_.Content.Characters, fork_.Content.Prototypes, fork_.Textures);
            using var image = renderer.Render(look);
            if (output != null)
            {
                Directory.CreateDirectory(output);
                using var big = image.Resize(new SKImageInfo(image.Width * 8, image.Height * 8), SKSamplingOptions.Default);
                using var png = big.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".png"), png.ToArray(), ct);
                await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".fixes.txt"),
                    $"{fork.Name}, species {look.Species}\n" + string.Join("\n", fixes.Select(f => $"{f.Field}: {f.Message}")) + "\n", ct);
            }
        }
    }
}
