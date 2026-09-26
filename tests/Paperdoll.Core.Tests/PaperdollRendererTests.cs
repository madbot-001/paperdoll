using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

public class PaperdollRendererTests
{
    // A species with a torso (Chest layer, markings on Chest and Tail) and eyes, drawn over a
    // doll whose sprite lists Chest, Eyes, then Tail.
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
