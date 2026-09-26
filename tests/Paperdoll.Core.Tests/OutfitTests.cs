using System.Text;
using Paperdoll.Core.Outfits;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class OutfitTests
{
    private const string Yaml = """
        - type: job
          id: Passenger
          name: job-name-passenger
          startingGear: PassengerGear
        - type: job
          id: Borg
          name: job-name-borg
          setPreference: false
        - type: startingGear
          id: PassengerGear
          equipment:
            id: PassengerPDA
            ears: HeadsetGrey
        - type: roleLoadout
          id: JobPassenger
          groups: [ Jumpsuit, Mask, Hat ]
        - type: loadoutGroup
          id: Jumpsuit
          name: loadout-group-jumpsuit
          loadouts: [ SeniorSuit, GreySuit, BlueSuit ]
        - type: loadoutGroup
          id: Mask
          name: loadout-group-mask
          minLimit: 0
          defaultSelected: 1
          loadouts: [ VoxMask ]
        - type: loadoutGroup
          id: Hat
          name: loadout-group-hat
          minLimit: 0
          loadouts: [ Beret ]
        - type: loadoutEffectGroup
          id: Senior
          effects:
          - !type:JobRequirementLoadoutEffect
            requirement: {}
        - type: loadout
          id: SeniorSuit
          effects:
          - !type:GroupLoadoutEffect
            proto: Senior
          equipment:
            jumpsuit: SuitSenior
        - type: loadout
          id: GreySuit
          equipment:
            jumpsuit: SuitGrey
            id: LoadoutPDA
        - type: loadout
          id: BlueSuit
          equipment:
            jumpsuit: SuitBlue
        - type: loadout
          id: VoxMask
          effects:
          - !type:SpeciesLoadoutEffect
            species: [ Vox ]
          equipment:
            mask: BreathMask
        - type: loadout
          id: Beret
          equipment:
            head: HatBeret
        """;

    private static OutfitCatalog Build() =>
        OutfitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("o.yml", Encoding.UTF8.GetBytes(Yaml))]));

    [Fact]
    public void Defaults_skip_loadouts_that_need_playtime_or_another_species()
    {
        var outfits = Build();

        var human = outfits.LoadoutFor("Passenger", "Human", saved: null);
        var vox = outfits.LoadoutFor("Passenger", "Vox", saved: null);

        Assert.Equal(["GreySuit"], human.Groups.Single(g => g.Group == "Jumpsuit").Loadouts);
        Assert.Empty(human.Groups.Single(g => g.Group == "Mask").Loadouts);
        Assert.Equal(["VoxMask"], vox.Groups.Single(g => g.Group == "Mask").Loadouts);
        Assert.Empty(human.Groups.Single(g => g.Group == "Hat").Loadouts);
    }

    [Fact]
    public void Saved_groups_are_kept_and_the_rest_get_defaults()
    {
        var outfits = Build();
        var saved = new Dictionary<string, IReadOnlyList<string>> { ["Jumpsuit"] = ["BlueSuit"], ["Hat"] = ["Beret"] };

        var loadout = outfits.LoadoutFor("Passenger", "Human", saved);

        Assert.Equal(["BlueSuit"], loadout.Groups.Single(g => g.Group == "Jumpsuit").Loadouts);
        Assert.Equal(["Beret"], loadout.Groups.Single(g => g.Group == "Hat").Loadouts);
    }

    [Fact]
    public void The_jobs_gear_goes_on_first_and_loadouts_only_fill_empty_slots()
    {
        var outfits = Build();

        var outfit = outfits.OutfitFor("Passenger", outfits.LoadoutFor("Passenger", "Human", null));

        Assert.Equal("PassengerPDA", outfit["id"]);
        Assert.Equal("SuitGrey", outfit["jumpsuit"]);
        Assert.Equal("HeadsetGrey", outfit["ears"]);
    }

    [Fact]
    public void Species_and_playtime_checks_are_reported()
    {
        var outfits = Build();

        Assert.Equal(LoadoutCheck.WrongSpecies, outfits.Check(outfits.Loadouts["VoxMask"], "Human"));
        Assert.Equal(LoadoutCheck.ServerChecks, outfits.Check(outfits.Loadouts["SeniorSuit"], "Human"));
        Assert.Equal(LoadoutCheck.Allowed, outfits.Check(outfits.Loadouts["GreySuit"], "Human"));
        Assert.DoesNotContain(outfits.SelectableJobs(), j => j.Id == "Borg");
    }
}

public class ClothingResolverTests
{
    private const string Yaml = """
        - type: entity
          id: ClothingBase
          abstract: true
          components:
          - type: Clothing
        - type: entity
          id: SuitGrey
          parent: ClothingBase
          components:
          - type: Sprite
            sprite: Clothing/suit.rsi
        - type: entity
          id: SuitPrefixed
          parent: ClothingBase
          components:
          - type: Sprite
            sprite: Clothing/suit.rsi
          - type: Clothing
            equippedPrefix: rolled
        - type: entity
          id: Coat
          components:
          - type: Sprite
            sprite: Clothing/coat.rsi
          - type: Clothing
            clothingVisuals:
              outerClothing:
              - state: body
              - state: trim
                color: "#FF0000"
        - type: entity
          id: Helmet
          components:
          - type: Sprite
            sprite: Clothing/helmet.rsi
          - type: Clothing
          - type: HideLayerClothing
            slots: [ Hair, HeadTop ]
        """;

    private static readonly Dictionary<string, RsiMeta> Metas = new()
    {
        ["Clothing/suit.rsi"] = Meta("equipped-INNERCLOTHING", "equipped-INNERCLOTHING-vox", "rolled-equipped-INNERCLOTHING"),
        ["Clothing/coat.rsi"] = Meta("body", "trim"),
        ["Clothing/helmet.rsi"] = Meta("equipped-HELMET"),
    };

    private static RsiMeta Meta(params string[] states) => RsiMeta.Parse(Encoding.UTF8.GetBytes(
        $$"""{"size":{"x":32,"y":32},"states":[{{string.Join(',', states.Select(s => $$"""{"name":"{{s}}"}"""))}}]}"""));

    private static ClothingResolver Build() => new(
        PrototypeIndex.Load([new PrototypeSource("c.yml", Encoding.UTF8.GetBytes(Yaml))]),
        rsi => Metas.GetValueOrDefault(rsi));

    [Fact]
    public void Uses_the_equipped_state_for_the_slot_and_a_species_version_when_there_is_one()
    {
        var resolver = Build();

        var human = resolver.Resolve("SuitGrey", "jumpsuit", "human")!;
        var vox = resolver.Resolve("SuitGrey", "jumpsuit", "vox")!;

        Assert.Equal("equipped-INNERCLOTHING", human.Layers.Single().Sprite.State);
        Assert.False(human.Layers.Single().SpeciesSpecific);
        Assert.Equal("equipped-INNERCLOTHING-vox", vox.Layers.Single().Sprite.State);
        Assert.True(vox.Layers.Single().SpeciesSpecific);
    }

    [Fact]
    public void Applies_the_equipped_prefix()
    {
        Assert.Equal("rolled-equipped-INNERCLOTHING", Build().Resolve("SuitPrefixed", "jumpsuit", null)!.Layers.Single().Sprite.State);
    }

    [Fact]
    public void Clothing_visuals_give_several_coloured_layers()
    {
        var coat = Build().Resolve("Coat", "outerClothing", null)!;

        Assert.Equal(["body", "trim"], coat.Layers.Select(l => l.Sprite.State));
        Assert.Equal(Rgba.Parse("#FF0000"), coat.Layers[1].Color);
    }

    [Fact]
    public void Lists_the_body_layers_an_item_hides()
    {
        Assert.Equal(["Hair", "HeadTop"], Build().Resolve("Helmet", "head", null)!.HiddenLayers.Order());
    }

    [Fact]
    public void An_item_without_a_sprite_for_the_slot_draws_nothing()
    {
        Assert.Empty(Build().Resolve("Helmet", "jumpsuit", null)!.Layers);
    }
}
