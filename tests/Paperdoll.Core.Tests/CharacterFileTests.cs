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

public class CharacterSizeTests
{
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: Doll
        - type: species
          id: Small
          name: species-name-small
          roundStart: true
          dollPrototype: Doll
          baseScale: 0.8, 0.8
          minHeight: 0.9
          maxHeight: 1.1
        - type: entity
          id: Doll
        """;

    private static readonly CharacterCatalog Catalog =
        CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(Yaml))]));

    private static readonly ForkInfo DeltaVLike = new("dv", "DV", "o/r", "main", AppearanceModel.New, false, [])
    {
        SizeRule = SizeRule.SpeciesScaleTimesHeight,
    };

    private static readonly ForkInfo Upstream = new("up", "Up", "o/r", "main", AppearanceModel.New, false, []);

    private static CharacterFile File(string species, string? height) => CharacterFile.Parse(
        $"forkId: x\nversion: 2\nprofile:\n  name: Ann Bee\n  species: {species}\n  age: 30\n  sex: Male\n  gender: Male\n"
        + (height != null ? $"  cosmaticDriftCharacterHeight: {height}\n" : "")
        + "  appearance:\n    skinColor: '#FFFFFFFF'\n    eyeColor: '#000000FF'\n    markings: {}\n");

    [Fact]
    public void Reads_species_scale_and_height_range()
    {
        var small = Catalog.Species["Small"];

        Assert.Equal((0.8f, 0.8f), small.BaseScale);
        Assert.Equal(0.9f, small.MinHeight);
        Assert.Equal(1.2f, Catalog.Species["Human"].MaxHeight);
    }

    [Fact]
    public void Scale_is_species_scale_times_height_where_the_fork_has_heights()
    {
        var file = File("Small", "1.05");

        Assert.Equal(0.84f, CharacterSize.SpriteScale(DeltaVLike, Catalog.Species["Small"], file).X, 3);
        Assert.Equal((1f, 1f), CharacterSize.SpriteScale(Upstream, Catalog.Species["Small"], file));
    }

    [Fact]
    public void Height_is_rounded_and_kept_in_range()
    {
        var file = File("Small", "1.3456");

        var fixes = CharacterRules.EnsureValid(file, Catalog, DeltaVLike);

        Assert.Equal("1.1", file.GetValue(CharacterSize.HeightKey));
        Assert.Contains(fixes, f => f.Field == "height");
    }

    [Fact]
    public void A_missing_height_is_written_as_1_without_a_message()
    {
        var file = File("Human", null);

        var fixes = CharacterRules.EnsureValid(file, Catalog, DeltaVLike);

        Assert.Equal("1", file.GetValue(CharacterSize.HeightKey));
        Assert.DoesNotContain(fixes, f => f.Field == "height");
    }

    [Fact]
    public void Forks_without_heights_leave_the_key_alone()
    {
        var file = File("Human", "5");

        CharacterRules.EnsureValid(file, Catalog, Upstream);

        Assert.Equal("5", file.GetValue(CharacterSize.HeightKey));
    }
}

public class NameGeneratorTests
{
    private const string Yaml = """
        - type: species
          id: Lizard
          name: species-name-lizard
          roundStart: true
          dollPrototype: Doll
          naming: LastFirst
          maleFirstNames: LizardFirst
          femaleFirstNames: LizardFirst
          lastNames: LizardLast
        - type: species
          id: Odd
          name: species-name-odd
          roundStart: true
          dollPrototype: Doll
          naming: SomethingNew
          maleFirstNames: LizardFirst
          femaleFirstNames: LizardFirst
          lastNames: PlainLast
        - type: entity
          id: Doll
        - type: localizedDataset
          id: LizardFirst
          values:
            prefix: names-lizard-first-
            count: 1
        - type: localizedDataset
          id: LizardLast
          values:
            prefix: names-lizard-last-
            count: 1
        - type: dataset
          id: PlainLast
          values: [ Stone ]
        """;

    private static (NameGenerator, CharacterCatalog) Build()
    {
        var index = PrototypeIndex.Load([new PrototypeSource("n.yml", Encoding.UTF8.GetBytes(Yaml))]);
        var strings = new Locale.FluentStrings();
        strings.Add("names-lizard-first-1 = Hisses\nnames-lizard-last-1 = Scales\nnamepreset-lastfirst = {$last} {$first}\nnamepreset-firstlast = {$first} {$last}\n");
        return (new NameGenerator(index, strings, new Random(1)), CharacterCatalog.Build(index));
    }

    [Fact]
    public void Uses_the_species_naming_pattern_and_datasets()
    {
        var (names, catalog) = Build();

        Assert.Equal("Scales Hisses", names.Next(catalog.Species["Lizard"], "Male"));
    }

    [Fact]
    public void Unknown_namings_fall_back_to_first_and_last_and_plain_datasets_work()
    {
        var (names, catalog) = Build();

        Assert.Equal("Hisses Stone", names.Next(catalog.Species["Odd"], "Female"));
    }

    [Fact]
    public void An_empty_name_gets_a_random_one_from_the_rules()
    {
        var (names, catalog) = Build();
        var file = CharacterFile.Parse("forkId: x\nversion: 2\nprofile:\n  name: ''\n  species: Lizard\n  age: 30\n  sex: Male\n  gender: Male\n  appearance:\n    markings: {}\n");
        var fork = new ForkInfo("t", "T", "o/r", "main", AppearanceModel.New, false, []);

        var fixes = CharacterRules.EnsureValid(file, catalog, fork, names.Next);

        Assert.Equal("Scales Hisses", file.Name);
        Assert.Contains(fixes, f => f.Field == "name");
    }
}
