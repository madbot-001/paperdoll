using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;

namespace Paperdoll.Core.Tests;

public class CharacterCatalogTests
{
    // A cut-down species in the new model: species -> doll -> organs, a markings group and a marking.
    private const string Yaml = """
        - type: species
          id: Lizard
          name: species-name-lizard
          roundStart: true
          prototype: MobLizard
          dollPrototype: AppearanceLizard
          skinColoration: Hues
          sexes: [ Male, Female, Unsexed ]
          maxAge: 80
        - type: species
          id: Hidden
          name: species-name-hidden
          roundStart: true
          dollPrototype: AppearanceLizard
        - type: species
          id: Ghost
          name: species-name-ghost
          roundStart: false
          dollPrototype: AppearanceLizard

        - type: entity
          id: OrganBaseTorso
          abstract: true
          components:
          - type: Organ
            category: Torso
          - type: Sprite
            state: torso
          - type: VisualOrgan
            layer: enum.HumanoidVisualLayers.Chest
            data:
              state: torso
            sexStateOverrides:
              Male: torso_m
          - type: VisualOrganMarkings
            markingData:
              layers: [ Chest, Tail ]
              group: Lizard
        - type: entity
          id: OrganLizardTorso
          parent: OrganBaseTorso
          components:
          - type: Sprite
            sprite: /Textures/Mobs/Species/Lizard/parts.rsi
        - type: entity
          id: OrganLizardBrain
          components:
          - type: Organ
            category: Brain
        - type: entity
          id: AppearanceLizard
          components:
          - type: InitialBody
            organs:
              Torso: OrganLizardTorso
              Brain: OrganLizardBrain

        - type: markingsGroup
          id: Lizard
          limits:
            enum.HumanoidVisualLayers.Tail:
              limit: 1
              required: true
              default: [ LizardTail ]
        - type: marking
          id: LizardTail
          bodyPart: Tail
          groupWhitelist: [ Lizard ]
          sprites:
          - sprite: Mobs/Customization/lizard_tails.rsi
            state: tail
        """;

    private static CharacterCatalog Build() =>
        CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("lizard.yml", Encoding.UTF8.GetBytes(Yaml))]));

    [Fact]
    public void Reads_species_fields_with_game_defaults()
    {
        var lizard = Build().Species["Lizard"];

        Assert.Equal("species-name-lizard", lizard.NameKey);
        Assert.Equal(["Male", "Female", "Unsexed"], lizard.Sexes);
        Assert.Equal(18, lizard.MinAge);
        Assert.Equal(80, lizard.MaxAge);
        Assert.Equal("Hues", lizard.SkinColoration);
    }

    [Fact]
    public void Organs_come_from_the_doll_with_parents_applied()
    {
        var organ = Assert.Single(Build().Species["Lizard"].Organs);

        Assert.Equal("Torso", organ.Category);
        Assert.Equal("Chest", organ.Layer);
        Assert.Equal(new SpriteRef("Mobs/Species/Lizard/parts.rsi", "torso"), organ.Sprite);
        Assert.Equal("torso_m", organ.SexStates["Male"]);
        Assert.Equal(["Chest", "Tail"], organ.MarkingLayers);
        Assert.Equal("Lizard", organ.MarkingGroup);
    }

    [Fact]
    public void Reads_marking_groups_and_markings()
    {
        var catalog = Build();

        var tail = catalog.MarkingsGroups["Lizard"].Limits["Tail"];
        Assert.True(tail.Required);
        Assert.Equal(["LizardTail"], tail.Default);
        var marking = catalog.Markings["LizardTail"];
        Assert.Equal("Tail", marking.Layer);
        Assert.Equal(["Lizard"], marking.GroupWhitelist);
    }

    [Fact]
    public void Selectable_species_are_round_start_and_not_hidden()
    {
        Assert.Equal(["Lizard"], Build().Selectable(["Hidden"]).Select(s => s.Id));
    }

    [Fact]
    public void Lists_every_sprite_folder_species_and_markings_use()
    {
        Assert.Equal(
            ["Mobs/Customization/lizard_tails.rsi", "Mobs/Species/Lizard/parts.rsi"],
            Build().SpriteFolders().Order());
    }
}
