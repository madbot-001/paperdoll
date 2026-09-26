using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

public class PaperdollRendererTests
{
    // A species with a torso (Chest layer, markings on Chest and Tail) and eyes, drawn over a
    // doll whose sprite lists TailBehind, Chest, Eyes, then Tail.
    private const string Yaml = """
        - type: species
          id: Lizard
          name: species-name-lizard
          roundStart: true
          dollPrototype: AppearanceLizard
        - type: entity
          id: AppearanceLizard
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.TailBehind" ]
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
            - map: [ "enum.HumanoidVisualLayers.Eyes" ]
            - map: [ "enum.HumanoidVisualLayers.Tail" ]
          - type: InitialBody
            organs:
              Torso: OrganTorso
              Eyes: OrganEyes
        - type: entity
          id: OrganTorso
          components:
          - type: Organ
            category: Torso
          - type: VisualOrgan
            layer: enum.HumanoidVisualLayers.Chest
            data:
              sprite: Mobs/lizard.rsi
              state: torso
            sexStateOverrides:
              Female: torso_f
          - type: VisualOrganMarkings
            markingData:
              layers: [ Chest, Tail ]
              group: Lizard
        - type: entity
          id: OrganEyes
          components:
          - type: Organ
            category: Eyes
          - type: VisualOrgan
            layer: enum.HumanoidVisualLayers.Eyes
            data:
              sprite: Mobs/lizard.rsi
              state: eyes
        - type: markingsGroup
          id: Lizard
          limits:
            enum.HumanoidVisualLayers.Tail:
              limit: 1
              required: true
              default: [ Tail ]
        - type: marking
          id: StripeA
          bodyPart: Chest
          sprites:
          - sprite: Mobs/markings.rsi
            state: a
        - type: marking
          id: StripeB
          bodyPart: Chest
          sprites:
          - sprite: Mobs/markings.rsi
            state: b
        - type: marking
          id: Tattoo
          bodyPart: Chest
          forcedColoring: true
          coloring:
            default:
              type: !type:TattooColoring
          sprites:
          - sprite: Mobs/markings.rsi
            state: a
        - type: marking
          id: Tail
          bodyPart: Tail
          sprites:
          - sprite: /Textures/Mobs/big.rsi
            state: tail
        - type: marking
          id: Side
          bodyPart: Chest
          sprites:
          - sprite: Mobs/side.rsi
            state: side
        - type: marking
          id: SplitTail
          bodyPart: Tail
          layering:
            back: TailBehind
          colorLinks:
            back: front
          sprites:
          - sprite: Mobs/markings.rsi
            state: front
          - sprite: Mobs/markings.rsi
            state: back
        """;

    private static readonly Rgba Skin = Rgba.Parse("#FF8040");

    private static (PaperdollRenderer Renderer, CharacterCatalog Catalog) Build()
    {
        var index = PrototypeIndex.Load([new PrototypeSource("lizard.yml", Encoding.UTF8.GetBytes(Yaml))]);
        var catalog = CharacterCatalog.Build(index);
        var textures = new MemoryTextures(new Dictionary<string, byte[]>
        {
            ["Mobs/lizard.rsi/meta.json"] = Meta(32, "torso", "torso_f", "eyes"),
            ["Mobs/lizard.rsi/torso.png"] = Png(32, 32, Solid(SKColors.White)),
            ["Mobs/lizard.rsi/torso_f.png"] = Png(32, 32, Solid(new SKColor(128, 128, 128))),
            ["Mobs/lizard.rsi/eyes.png"] = Png(32, 32, (x, y) => x == 10 && y == 10 ? SKColors.White : SKColors.Transparent),
            ["Mobs/markings.rsi/meta.json"] = Meta(32, "a", "b"),
            ["Mobs/markings.rsi/a.png"] = Png(32, 32, (x, y) => x == 1 && y == 1 ? SKColors.White : SKColors.Transparent),
            ["Mobs/markings.rsi/b.png"] = Png(32, 32, (x, y) => x == 1 && y == 1 ? SKColors.White : SKColors.Transparent),
            // Four facings: one pixel from the south, three from the east.
            ["Mobs/side.rsi/meta.json"] = Encoding.UTF8.GetBytes("""{"version":1,"license":"CC0-1.0","copyright":"test","size":{"x":32,"y":32},"states":[{"name":"side","directions":4}]}"""),
            ["Mobs/side.rsi/side.png"] = Png(128, 32, (x, y) => y == 2 && (x == 1 || x is >= 66 and <= 68) ? SKColors.White : SKColors.Transparent),
            ["Mobs/big.rsi/meta.json"] = Meta(64, "tail"),
            ["Mobs/big.rsi/tail.png"] = Png(64, 64, (x, y) => x == 0 && y == 0 ? SKColors.White : SKColors.Transparent),
        });
        return (new PaperdollRenderer(catalog, index, textures), catalog);
    }

    private static Func<int, int, SKColor> Solid(SKColor color) => (_, _) => color;

    private static byte[] Meta(int size, params string[] states) => Encoding.UTF8.GetBytes(
        $$"""{"version":1,"license":"CC0-1.0","copyright":"test","size":{"x":{{size}},"y":{{size}}},"states":[{{string.Join(',', states.Select(s => $$"""{"name":"{{s}}","directions":1}"""))}}]}""");

    private static byte[] Png(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, pixel(x, y));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static CharacterLook Look(string sex = "Male", params (string Id, Rgba Color)[] chest) => new()
    {
        Species = "Lizard",
        Sex = sex,
        SkinColor = Skin,
        EyeColor = Rgba.Parse("#00FF00"),
        Markings = new() { ["Torso"] = new() { ["Chest"] = chest.Select(c => new MarkingEntry(c.Id, [c.Color])).ToList() } },
    };

    [Fact]
    public void Organs_take_the_skin_colour_and_eyes_the_eye_colour()
    {
        var (renderer, _) = Build();

        using var image = renderer.Render(Look());

        Assert.Equal(new SKColor(255, 128, 64), image.GetPixel(5, 5));
        Assert.Equal(new SKColor(0, 255, 0), image.GetPixel(10, 10));
    }

    [Fact]
    public void An_organ_uses_its_per_sex_state()
    {
        var (renderer, _) = Build();

        var layers = renderer.Layers(Look(sex: "Female"));

        Assert.Equal("torso_f", layers[0].Sprite.State);
    }

    [Fact]
    public void Markings_go_just_above_their_body_part_and_later_ones_go_underneath()
    {
        var (renderer, _) = Build();

        var layers = renderer.Layers(Look("Male", ("StripeA", Rgba.White), ("StripeB", Rgba.White)));

        Assert.Equal(["enum.HumanoidVisualLayers.Chest", "StripeB-b", "StripeA-a", "enum.HumanoidVisualLayers.Eyes"],
            layers.Select(l => l.Key));
    }

    [Fact]
    public void The_top_marking_colours_the_pixel()
    {
        var (renderer, _) = Build();

        using var image = renderer.Render(Look("Male", ("StripeA", Rgba.Parse("#0000FF")), ("StripeB", Rgba.Parse("#FF0000"))));

        Assert.Equal(new SKColor(0, 0, 255), image.GetPixel(1, 1));
    }

    [Fact]
    public void Forced_colours_come_from_the_marking_rules()
    {
        var (renderer, _) = Build();

        var layers = renderer.Layers(Look("Male", ("Tattoo", Rgba.White)));

        var tattoo = layers.Single(l => l.Key == "Tattoo-a").Color;
        Assert.Equal(0.4f, tattoo.ToHsv().V, 3);
        Assert.Equal(Skin.ToHsv().H, tattoo.ToHsv().H, 3);
    }

    [Fact]
    public void Layering_draws_a_sprite_on_its_own_layer_in_the_colour_it_links_to()
    {
        var (renderer, _) = Build();
        var look = new CharacterLook
        {
            Species = "Lizard",
            Sex = "Male",
            SkinColor = Skin,
            EyeColor = Rgba.White,
            Markings = new() { ["Torso"] = new() { ["Tail"] = [new MarkingEntry("SplitTail", [Rgba.Parse("#0000FF"), Rgba.Parse("#FF0000")])] } },
        };

        var layers = renderer.Layers(look);

        Assert.Equal(["SplitTail-back", "enum.HumanoidVisualLayers.Chest", "enum.HumanoidVisualLayers.Eyes", "SplitTail-front"],
            layers.Select(l => l.Key));
        Assert.All(layers.Where(l => l.Key.StartsWith("SplitTail", StringComparison.Ordinal)), l => Assert.Equal(Rgba.Parse("#0000FF"), l.Color));
    }

    [Fact]
    public void A_marking_picture_is_the_marking_alone_facing_the_way_that_shows_the_most()
    {
        var (renderer, _) = Build();

        using var image = renderer.RenderMarking(Look("Male", ("Side", Rgba.Parse("#FF0000")), ("StripeA", Rgba.White)), "Side");

        Assert.Equal(3, image.Pixels.Count(p => p.Alpha > 0));
        Assert.Equal(new SKColor(255, 0, 0), image.GetPixel(3, 2));
    }

    [Fact]
    public void Bigger_frames_are_centred_and_leading_slash_paths_work()
    {
        var (renderer, catalog) = Build();

        using var image = renderer.Render(LookDefaults.Create(catalog, "Lizard", "Male", Skin, Rgba.White));

        Assert.Equal(64, image.Width);
        // The tail (64x64, drawn from its top-left pixel) follows the skin colour by default.
        Assert.Equal(new SKColor(255, 128, 64), image.GetPixel(0, 0));
        Assert.Equal(new SKColor(255, 128, 64), image.GetPixel(16 + 5, 16 + 5));
    }

    [Fact]
    public void A_new_look_gets_its_required_default_markings()
    {
        var (_, catalog) = Build();

        var look = LookDefaults.Create(catalog, "Lizard", "Male", Skin, Rgba.White);

        var tail = Assert.Single(look.Markings["Torso"]["Tail"]);
        Assert.Equal("Tail", tail.Id);
        Assert.Equal([Skin], tail.Colors);
    }
}

public class DisplacementTests
{
    private static SKBitmap Solid(int size, Func<int, int, SKColor> pixel)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            bitmap.SetPixel(x, y, pixel(x, y));
        return bitmap;
    }

    [Fact]
    public void Each_pixel_comes_from_the_source_moved_by_the_map()
    {
        // A red dot at (5, 5); the map says "take from one right, two down" everywhere.
        using var frame = Solid(8, (x, y) => x == 5 && y == 5 ? SKColors.Red : SKColors.Transparent);
        using var map = Solid(8, (_, _) => new SKColor(129, 130, 0, 255));

        using var result = PaperdollRenderer.Displace(frame, map);

        Assert.Equal(SKColors.Red, result.GetPixel(4, 3));
        Assert.Equal(0, result.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public void The_maps_alpha_masks_the_layer()
    {
        using var frame = Solid(4, (_, _) => SKColors.Blue);
        using var map = Solid(4, (x, _) => new SKColor(128, 128, 0, (byte)(x < 2 ? 255 : 0)));

        using var result = PaperdollRenderer.Displace(frame, map);

        Assert.Equal(255, result.GetPixel(1, 1).Alpha);
        Assert.Equal(0, result.GetPixel(3, 1).Alpha);
    }
}
