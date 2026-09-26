using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class RandomizerTests
{
    // A human with a head (Hair limit 1) and a torso (Chest limit 2, always rolled; Tail never).
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: Doll
          skinColoration: Toned
          sexes: [ Male, Female ]
          minAge: 18
          oldAge: 60
        - type: skinColoration
          id: Toned
          strategy: !type:HumanTonedSkinColoration {}
          realisticColors: true
        - type: entity
          id: Doll
          components:
          - type: InitialBody
            organs:
              Head: OrganHead
              Torso: OrganTorso
        - type: entity
          id: OrganHead
          components:
          - type: Organ
            category: Head
          - type: VisualOrganMarkings
            markingData: { layers: [ Hair ], group: Human }
        - type: entity
          id: OrganTorso
          components:
          - type: Organ
            category: Torso
          - type: VisualOrganMarkings
            markingData: { layers: [ Chest, Tail ], group: Human }
        - type: markingsGroup
          id: Human
          limits:
            enum.HumanoidVisualLayers.Hair: { limit: 1, weight: 1 }
            enum.HumanoidVisualLayers.Chest: { limit: 2, weight: 1 }
            enum.HumanoidVisualLayers.Tail: { limit: 1, weight: 0 }
        - type: marking
          id: HairShort
          bodyPart: Hair
          sprites: [ { sprite: Mobs/hair.rsi, state: short }, { sprite: Mobs/hair.rsi, state: shine } ]
        - type: marking
          id: Tattoo
          bodyPart: Chest
          coloring: { default: { type: !type:SkinColoring {} } }
          sprites: [ { sprite: Mobs/body.rsi, state: tattoo } ]
        - type: marking
          id: Scar
          bodyPart: Chest
          sprites: [ { sprite: Mobs/body.rsi, state: scar } ]
        - type: marking
          id: Stripes
          bodyPart: Chest
          randomWeight: 0
          sprites: [ { sprite: Mobs/body.rsi, state: stripes } ]
        - type: marking
          id: Tail
          bodyPart: Tail
          sprites: [ { sprite: Mobs/tail.rsi, state: tail } ]
        """;

    private static readonly CharacterCatalog Catalog =
        CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("r.yml", Encoding.UTF8.GetBytes(Yaml))]));

    [Fact]
    public void Palettes_follow_the_skin_rule_and_realistic_colours()
    {
        var human = Catalog.Species["Human"];
        var rule = Catalog.SkinRuleFor(human);
        for (var seed = 0; seed < 50; seed++)
        {
            var palette = new Randomizer(Catalog, new Random(seed)).RandomPalette(human);

            Assert.True(rule.IsValid(palette.Skin));
            Assert.Contains(palette.Eyes, new[] { "#A52A2A", "#808080", "#F0FFFF", "#4682B4", "#000000" }.Select(Rgba.Parse));
        }
    }

    [Fact]
    public void Markings_are_rolled_per_layer_within_limits_and_weights()
    {
        var human = Catalog.Species["Human"];
        for (var seed = 0; seed < 50; seed++)
        {
            var randomizer = new Randomizer(Catalog, new Random(seed));
            var palette = randomizer.RandomPalette(human);
            var markings = randomizer.Markings(human, "Male", palette);

            var hair = Assert.Single(markings["Head"]["Hair"]);
            Assert.Equal([palette.Hair, palette.Hair], hair.Colors);
            // Weight 1 on a limit of 2 always fills both places, never with the zero-weight marking.
            var chest = markings["Torso"]["Chest"];
            Assert.Equal(2, chest.Count);
            Assert.Equal(2, chest.Select(m => m.Id).Distinct().Count());
            Assert.DoesNotContain(chest, m => m.Id == "Stripes");
            Assert.Equal(palette.Skin, chest.Single(m => m.Id == "Tattoo").Colors[0]);
            Assert.Contains(chest.Single(m => m.Id == "Scar").Colors[0], new[] { palette.Hair, palette.Eyes });
            Assert.Empty(markings["Torso"]["Tail"]);
        }
    }

    [Fact]
    public void Strength_scales_the_places_rolled_and_their_chance_but_not_hair()
    {
        var human = Catalog.Species["Human"];
        var counts = new List<int>();
        for (var seed = 0; seed < 200; seed++)
        {
            var none = new Randomizer(Catalog, new Random(seed)) { Strength = 0f };
            var markings = none.Markings(human, "Male", none.RandomPalette(human));
            Assert.Single(markings["Head"]["Hair"]);
            Assert.Empty(markings["Torso"]["Chest"]);

            var half = new Randomizer(Catalog, new Random(seed)) { Strength = 0.5f };
            counts.Add(half.Markings(human, "Male", half.RandomPalette(human))["Torso"]["Chest"].Count);
        }

        // Half of a limit of 2 is one place, rolled at half of weight 1.
        Assert.Equal(1, counts.Max());
        Assert.InRange(counts.Average(), 0.35, 0.65);
    }

    [Fact]
    public void Ages_run_up_to_but_not_including_old_age()
    {
        var human = Catalog.Species["Human"];
        var ages = Enumerable.Range(0, 500).Select(seed => new Randomizer(Catalog, new Random(seed)).Age(human)).ToList();

        Assert.Equal(18, ages.Min());
        Assert.Equal(59, ages.Max());
        Assert.Equal("Epicene", Randomizer.PronounsFor("Unsexed"));
    }
}
