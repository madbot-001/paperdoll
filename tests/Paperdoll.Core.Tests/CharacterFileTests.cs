using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class CharacterFileTests
{
    // Human: a head (Hair, FacialHair) and a torso (Chest, Tail), Hair limited to one marking,
    // Tail required with a default.
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: AppearanceHuman
          sexes: [ Male, Female ]
          minAge: 18
          maxAge: 120
        - type: species
          id: Ghost
          name: species-name-ghost
          roundStart: false
          dollPrototype: AppearanceHuman
        - type: entity
          id: AppearanceHuman
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
            markingData:
              layers: [ Hair, FacialHair ]
              group: Human
        - type: entity
          id: OrganTorso
          components:
          - type: Organ
            category: Torso
          - type: VisualOrganMarkings
            markingData:
              layers: [ Chest, Tail ]
              group: Human
        - type: markingsGroup
          id: Human
          limits:
            enum.HumanoidVisualLayers.Hair:
              limit: 1
              required: false
            enum.HumanoidVisualLayers.Tail:
              limit: 1
              required: true
              default: [ HumanTail ]
        - type: marking
          id: HairShort
          bodyPart: Hair
          sprites: [ { sprite: Mobs/hair.rsi, state: short } ]
        - type: marking
          id: HairLong
          bodyPart: Hair
          sprites: [ { sprite: Mobs/hair.rsi, state: long } ]
        - type: marking
          id: Beard
          bodyPart: FacialHair
          sexRestriction: Male
          sprites: [ { sprite: Mobs/hair.rsi, state: beard } ]
        - type: marking
          id: Stripes
          bodyPart: Chest
          sprites:
          - { sprite: Mobs/body.rsi, state: a }
          - { sprite: Mobs/body.rsi, state: b }
        - type: marking
          id: HumanTail
          bodyPart: Tail
          sprites: [ { sprite: Mobs/tail.rsi, state: tail } ]
        """;

    private static readonly CharacterCatalog Catalog =
        CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("h.yml", Encoding.UTF8.GetBytes(Yaml))]));

    private static readonly ForkInfo Fork = new("test", "Test", "o/r", "main", AppearanceModel.New, false, []);

    private const string OldFile = """
        profile:
          name: alex morrow
          species: Human
          customspeciename: Half Giant
          height: 1.12
          age: 26
          sex: Male
          gender: Male
          flavorText: A [color=red]tall[/color] man.
          appearance:
            markings:
            - markingId: Stripes
              visible: True
              markingColor:
              - '#FF0000FF'
            - markingId: SomethingOnlyTheOldForkHad
              markingColor: []
            skinColor: '#C49A7EFF'
            eyeColor: '#3A5F8CFF'
            facialHairColor: '#6E4B2AFF'
            facialHair: Beard
            hairColor: '#5B3A1EFF'
            hair: HairShort
          _jobPriorities:
            Passenger: High
        version: 1
        forkId: floof-station-nova
        """;

    [Fact]
    public void Old_files_are_recognised_by_shape_even_when_they_say_version_2()
    {
        var file = CharacterFile.Parse(OldFile.Replace("version: 1", "version: 2"));

        Assert.True(file.IsOldModel);
    }

    [Fact]
    public void Old_hair_and_markings_are_sorted_onto_organs()
    {
        var look = CharacterFile.Parse(OldFile).ReadLook(Catalog);

        Assert.Equal("HairShort", Assert.Single(look.Markings["Head"]["Hair"]).Id);
        Assert.Equal(Rgba.Parse("#5B3A1E"), look.Markings["Head"]["Hair"][0].Colors[0]);
        Assert.Equal("Beard", Assert.Single(look.Markings["Head"]["FacialHair"]).Id);
        Assert.Equal("Stripes", Assert.Single(look.Markings["Torso"]["Chest"]).Id);
        Assert.Equal(Rgba.Parse("#C49A7E"), look.SkinColor);
    }

    [Fact]
    public void Writing_a_look_gives_the_new_model_and_keeps_unknown_keys()
    {
        var file = CharacterFile.Parse(OldFile);
        file.WriteLook(file.ReadLook(Catalog));

        var again = CharacterFile.Parse(file.ToYaml());

        Assert.False(again.IsOldModel);
        Assert.Equal("2", again.Version);
        Assert.Equal("Half Giant", again.GetValue("customspeciename"));
        Assert.Equal("1.12", again.GetValue("height"));
        Assert.Contains("_jobPriorities", again.ToYaml());
        Assert.Equal("HairShort", again.ReadLook(Catalog).Markings["Head"]["Hair"][0].Id);
        Assert.Contains("'#5B3A1EFF'", again.ToYaml());
    }

    [Fact]
    public void The_rules_clean_the_name_text_and_markings()
    {
        var file = CharacterFile.Parse(OldFile);

        var fixes = CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Equal("Alex Morrow", file.Name);
        Assert.Equal("A tall man.", file.FlavorText);
        var look = file.ReadLook(Catalog);
        // Two colours for two sprites, the missing one repeated from the last.
        Assert.Equal([Rgba.Parse("#FF0000"), Rgba.Parse("#FF0000")], look.Markings["Torso"]["Chest"][0].Colors);
        // The required tail gets its default.
        Assert.Equal("HumanTail", look.Markings["Torso"]["Tail"][0].Id);
        Assert.Contains(fixes, f => f.Field == "name");
    }

    [Fact]
    public void A_species_that_is_not_round_start_becomes_human_and_age_is_clamped()
    {
        var file = CharacterFile.Parse(OldFile.Replace("species: Human", "species: Ghost").Replace("age: 26", "age: 500"));

        CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Equal("Human", file.Species);
        Assert.Equal(120, file.Age);
    }

    [Fact]
    public void Markings_for_the_other_sex_are_removed()
    {
        var file = CharacterFile.Parse(OldFile.Replace("sex: Male", "sex: Female"));

        CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.False(file.ReadLook(Catalog).Markings["Head"].ContainsKey("FacialHair"));
    }

    [Fact]
    public void Over_the_limit_the_earliest_markings_go()
    {
        var look = new CharacterLook
        {
            Species = "Human",
            Markings = new() { ["Head"] = new() { ["Hair"] = [new("HairShort", [Rgba.White]), new("HairLong", [Rgba.White])] } },
        };

        var valid = CharacterRules.EnsureValidLook(look, Catalog.Species["Human"], Catalog);

        Assert.Equal("HairLong", Assert.Single(valid.Markings["Head"]["Hair"]).Id);
    }

    // The game capitalises only the name's first letter and the start of its last run of word
    // characters, and leaves inner spaces alone.
    [Theory]
    [InlineData("  zoë   o'brien-smith  ", "Upstream", "Zo   o'brien-Smith")]
    [InlineData("zoë o'brien", "AccentedLatin", "Zoë o'Brien")]
    [InlineData("Dr. Ada Lovelace, the first", "AccentedLatin", "Dr. Ada Lovelace, the First")]
    public void Names_follow_the_forks_rule(string name, string rule, string expected)
    {
        var nameRule = rule == "Upstream" ? NameRule.Upstream : NameRule.AccentedLatin;

        Assert.Equal(expected, CharacterRules.CheckName(name, nameRule));
    }

    [Fact]
    public void Exported_files_carry_the_forks_label()
    {
        var file = CharacterFile.Parse(OldFile);

        file.LabelFor(KnownForks.Find("deltav")!);

        Assert.Equal("delta-v", CharacterFile.Parse(file.ToYaml()).ForkId);
    }

    [Fact]
    public void Files_without_a_profile_are_refused()
    {
        Assert.Throws<FormatException>(() => CharacterFile.Parse("forkId: x\nversion: 2\n"));
        Assert.Throws<FormatException>(() => CharacterFile.Parse("{ not: [ yaml"));
    }
}
