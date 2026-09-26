using System.Diagnostics;
using System.Text;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Rendering;
using SkiaSharp;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Tests against the real repositories on GitHub. Skipped unless PAPERDOLL_NETWORK_TESTS=1.
/// </summary>
public sealed class NetworkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-net-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Delta_v_prototypes_and_locale_download_and_read()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var timer = Stopwatch.StartNew();

        var commit = await store.SyncAsync(KnownForks.Find("deltav")!, ct: ct);
        var entries = await store.ListAsync("deltav", ["Resources/Prototypes", "Resources/Locale/en-US"], ct);
        var fetched = await store.FetchAsync("deltav", entries, ct);

        await using var reader = store.OpenReader();
        var harpy = entries.Single(e => e.Path == "Resources/Prototypes/_DV/Species/harpy.yml");
        var text = Encoding.UTF8.GetString((await reader.ReadAsync(harpy.ObjectId, ct))!);

        TestContext.Current.SendDiagnosticMessage(
            $"Delta-V {commit[..8]}: {entries.Count} files, {fetched} fetched in {timer.Elapsed.TotalSeconds:F1}s");
        Assert.Contains("id: Harpy", text);
        Assert.Empty(await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
    }

    [Theory]
    [InlineData("deltav", 21, 20)]
    [InlineData("upstream", 9, 9)]
    public async Task Forks_load_with_the_species_counted_in_the_survey(string forkId, int roundStart, int selectable)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var fork = KnownForks.Find(forkId)!;
        var timer = Stopwatch.StartNew();

        await store.SyncAsync(fork, ct);
        var content = await ForkContent.LoadAsync(store, fork, ct: ct);

        TestContext.Current.SendDiagnosticMessage(
            $"{fork.Name}: {content.Prototypes.Count} prototypes, {content.Strings.Count} strings, " +
            $"{content.TextureFiles.Count} sprite files in {timer.Elapsed.TotalSeconds:F1}s");
        Assert.Empty(content.Prototypes.Problems);
        Assert.Equal(roundStart, content.Characters.Species.Values.Count(s => s.RoundStart));
        Assert.Equal(selectable, content.Characters.Selectable(fork.HiddenSpecies).Count());
        Assert.Equal("Human", content.Strings.Get(content.Characters.Species["Human"].NameKey));
        Assert.All(content.Characters.Selectable(fork.HiddenSpecies), s => Assert.NotEmpty(s.Organs));
        Assert.Contains("Mobs/Species/Human/parts.rsi/meta.json", content.TextureFiles.Keys);
    }

    [Fact]
    public async Task Delta_v_harpy_wings_have_their_own_layer()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var fork = KnownForks.Find("deltav")!;
        await store.SyncAsync(fork, ct);

        var content = await ForkContent.LoadAsync(store, fork, ct: ct);

        Assert.Contains("RArmExtension", content.Characters.Species["Harpy"].Organs.SelectMany(o => o.MarkingLayers));
    }

    /// <summary>
    /// Draws Delta-V species with their default markings. Set PAPERDOLL_RENDER_OUT to a folder to
    /// keep the pictures (eight times size) for a look.
    /// </summary>
    [Fact]
    public async Task Delta_v_species_render()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var fork = KnownForks.Find("deltav")!;
        await store.SyncAsync(fork, ct);
        var content = await ForkContent.LoadAsync(store, fork, ct: ct);
        await using var reader = store.OpenReader();
        var textures = await MemoryTextures.LoadAsync(content, reader, ct);
        var renderer = new PaperdollRenderer(content.Characters, content.Prototypes, textures);
        var output = Environment.GetEnvironmentVariable("PAPERDOLL_RENDER_OUT");

        foreach (var species in content.Characters.Selectable(fork.HiddenSpecies))
        {
            var skin = content.Characters.DefaultSkin(species);
            Assert.True(content.Characters.SkinRuleFor(species).IsValid(skin), $"{species.Id}'s default skin breaks its own rule.");
            var look = LookDefaults.Create(content.Characters, species.Id, species.Sexes[0], skin, Rgba.Parse("#2F6FA8"));
            if (species.Id == "Human")
                look.Markings["Head"] = new() { ["Hair"] = [new MarkingEntry("HumanHairBedhead", [Rgba.Parse("#5A3A22")])] };

            using var image = renderer.Render(look);
            var opaque = 0;
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).Alpha > 0) opaque++;
            Assert.True(opaque > 50, $"{species.Id} drew only {opaque} pixels.");

            if (output != null)
            {
                Directory.CreateDirectory(output);
                using var big = image.Resize(new SKImageInfo(image.Width * 8, image.Height * 8), SKSamplingOptions.Default);
                using var png = big.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(Path.Combine(output, species.Id + ".png"), png.ToArray(), ct);
            }
        }
    }

    [Fact]
    public async Task Delta_v_species_download_through_the_github_api()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        // Unauthenticated, GitHub allows 60 API requests an hour; this test uses about six.
        var store = new GitHubForkStore(Path.Combine(_root, "files"), token: Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
        await store.InitializeAsync(ct);

        await store.SyncAsync(KnownForks.Find("deltav")!, ct);
        var entries = await store.ListAsync("deltav", ["Resources/Prototypes/_DV/Species"], ct);
        await store.FetchAsync("deltav", entries, ct);

        await using var reader = store.OpenReader();
        var harpy = entries.Single(e => e.Path == "Resources/Prototypes/_DV/Species/harpy.yml");
        Assert.Contains("id: Harpy", Encoding.UTF8.GetString((await reader.ReadAsync(harpy.ObjectId, ct))!));
    }
}
