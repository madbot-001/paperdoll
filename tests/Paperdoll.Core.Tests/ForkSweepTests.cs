using Paperdoll.Core.Forks;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Loads every fork Paperdoll calls editable and draws each of its species as a new character.
/// Network only (PAPERDOLL_NETWORK_TESTS=1). PAPERDOLL_TEST_STORE reuses a download folder;
/// PAPERDOLL_RENDER_OUT keeps a contact sheet per fork.
/// </summary>
public sealed class ForkSweepTests
{
    public static TheoryData<string> EditableForks() =>
        new(KnownForks.All.Where(f => f.Model == AppearanceModel.New).Select(f => f.Id));

    [Theory]
    [MemberData(nameof(EditableForks))]
    public async Task Every_species_of_an_editable_fork_draws(string forkId)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var root = Environment.GetEnvironmentVariable("PAPERDOLL_TEST_STORE") ?? Directory.CreateTempSubdirectory("paperdoll-sweep-").FullName;
        var store = new GitForkStore(Path.Combine(root, "git"));
        await store.InitializeAsync(ct);
        var fork = KnownForks.Find(forkId)!;

        await store.SyncAsync(fork, ct);
        var content = await ForkContent.LoadAsync(store, fork, ct: ct);
        await using var reader = store.OpenReader();
        var renderer = new PaperdollRenderer(content.Characters, content.Prototypes, await MemoryTextures.LoadAsync(content, reader, ct));
        var species = content.Characters.Selectable(fork.HiddenSpecies).ToList();

        var problems = new List<string>();
        var images = new List<(string, SKBitmap)>();
        foreach (var s in species)
        {
            try
            {
                var look = LookDefaults.Create(content.Characters, s.Id, s.Sexes[0], content.Characters.DefaultSkin(s), Rgba.Parse("#000000"));
                var image = renderer.Render(look);
                var opaque = 0;
                for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).Alpha > 0) opaque++;
                if (opaque < 50)
                    problems.Add($"{s.Id} drew only {opaque} pixels");
                images.Add((s.Id, image));
            }
            catch (Exception e)
            {
                problems.Add($"{s.Id}: {e.GetType().Name}: {e.Message}");
            }
        }

        if (Environment.GetEnvironmentVariable("PAPERDOLL_RENDER_OUT") is { } output && images.Count > 0)
            SaveSheet(Path.Combine(output, $"sweep-{forkId}.png"), images);
        foreach (var (_, image) in images)
            image.Dispose();

        TestContext.Current.SendDiagnosticMessage($"{fork.Name}: {species.Count} species, {content.Prototypes.Problems.Count} prototype problems");
        Assert.NotEmpty(species);
        Assert.True(problems.Count == 0, $"{fork.Name}: " + string.Join("; ", problems));
    }

    private static void SaveSheet(string path, List<(string Name, SKBitmap Image)> images)
    {
        const int cell = 96;
        var columns = 10;
        var rows = (images.Count + columns - 1) / columns;
        using var sheet = new SKBitmap(columns * cell, rows * (cell + 14));
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(0x8C, 0x94, 0x9E)); // mid grey, so dark sprites show too
        using var font = new SKFont(SKTypeface.Default, 10);
        using var paint = new SKPaint { Color = SKColors.White };
        for (var i = 0; i < images.Count; i++)
        {
            var (name, image) = images[i];
            var scale = Math.Min(cell / (float)image.Width, cell / (float)image.Height);
            var x = i % columns * cell;
            var y = i / columns * (cell + 14);
            using var img = SKImage.FromBitmap(image);
            canvas.DrawImage(img, SKRect.Create(x + (cell - image.Width * scale) / 2, y, image.Width * scale, image.Height * scale),
                new SKSamplingOptions(SKFilterMode.Nearest), null);
            canvas.DrawText(name, x + 2, y + cell + 11, font, paint);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
