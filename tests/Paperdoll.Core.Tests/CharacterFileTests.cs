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
    internal const string Yaml = """
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
          id: Bands
          bodyPart: Chest
          sprites:
          - { sprite: Mobs/body.rsi, state: a }
          - { sprite: Mobs/body.rsi, state: b }
          - { sprite: Mobs/body.rsi, state: c }
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
    public void A_file_using_aliases_is_refused_at_once()
    {
        // Each level's key repeats the one before twice: eight levels take seconds to read.
        var lines = new List<string> { "a0: &a0 [x, y]" };
        for (var i = 1; i < 12; i++)
            lines.Add($"a{i}: &a{i} [{{? *a{i - 1} : 1}}, {{? *a{i - 1} : 2}}]");
        var text = "profile:\n  name: x\n" + string.Join("\n", lines.Select(l => "  " + l)) + "\n";
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<FormatException>(() => CharacterFile.Parse(text));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void A_file_nested_thousands_deep_is_refused_without_crashing()
    {
        var text = "profile:\n  junk: " + new string('[', 5000) + new string(']', 5000) + "\n";

        Assert.Throws<FormatException>(() => CharacterFile.Parse(text));
    }

    [Fact]
    public void A_file_saved_on_windows_reads_the_same_and_is_written_the_same_everywhere()
    {
        // The game on Windows ends its lines with \r\n.
        var windows = CharacterFile.Parse(OldFile.ReplaceLineEndings("\r\n"));

        Assert.Equal(CharacterFile.Parse(OldFile.ReplaceLineEndings("\n")).ToYaml(), windows.ToYaml());
        Assert.DoesNotContain('\r', windows.ToYaml());
    }

    [Fact]
    public async Task A_file_far_larger_than_any_export_is_refused_before_it_is_read()
    {
        var text = OldFile + "#" + new string('x', CharacterFile.MaxLength) + "\n";

        Assert.Throws<FormatException>(() => CharacterFile.Parse(text));
        // Read from disk or the file picker, it stops once past the limit.
        using var stream = new EndlessStream();
        await Assert.ThrowsAsync<FormatException>(() => CharacterFile.ReadTextAsync(stream, TestContext.Current.CancellationToken));
        Assert.InRange(stream.Position, CharacterFile.MaxLength, CharacterFile.MaxLength * 2L);
    }

    [Fact]
    public void A_file_the_yaml_reader_trips_on_is_refused_as_unreadable()
    {
        // A list left open at the very end: the reader fails with an error of its own.
        var text = OldFile.Replace("version: 1", "version: 1\nextra: [");

        Assert.Throws<FormatException>(() => CharacterFile.Parse(text + "["));
    }

    [Fact]
    public void Broken_character_pairs_anywhere_in_a_file_are_dropped_as_it_is_read()
    {
        // In a name the game does not know, and in its value. (Written as escapes, the reader
        // refuses them itself.)
        var text = OldFile.Replace("version: 1", "version: 1\nextra\uD800Key: x\uDC00y");

        var file = CharacterFile.Parse(text);

        Assert.Contains("extraKey: xy", file.ToYaml());
    }

    // Gives a byte for every one asked, without end.
    private sealed class EndlessStream : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)'a');
            _position += count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData(" Null ")]
    [InlineData("\t")]
    public void Text_the_game_would_read_as_null_is_quoted(string text)
    {
        var file = CharacterFile.Parse(OldFile);

        file.FlavorText = text;
        var again = CharacterFile.Parse(file.ToYaml());

        Assert.Contains("flavorText: \"", file.ToYaml());
        Assert.Equal(text, again.FlavorText);
    }

    [Fact]
    public void Broken_character_pairs_are_dropped_so_the_file_can_be_written()
    {
        var file = CharacterFile.Parse(OldFile);

        file.FlavorText = "A\uD800B\uD83D\uDE00";

        Assert.Equal("AB\uD83D\uDE00", CharacterFile.Parse(file.ToYaml()).FlavorText);
    }

    [Fact]
    public void Descriptions_are_cut_at_the_forks_own_length()
    {
        var text = new string('a', 800);
        var upstream = CharacterFile.Parse(OldFile);
        upstream.FlavorText = text;
        var euphoria = CharacterFile.Parse(OldFile);
        euphoria.FlavorText = text;

        CharacterRules.EnsureValid(upstream, Catalog, Fork);
        CharacterRules.EnsureValid(euphoria, Catalog, Fork with { MaxFlavorTextLength = 1024 });

        Assert.Equal(512, upstream.FlavorText!.Length);
        Assert.Equal(text, euphoria.FlavorText);
        Assert.Equal(1024, KnownForks.Find("euphoria")!.MaxFlavorTextLength);
    }

    [Theory]
    [InlineData("plain", false, false, false, true)]
    [InlineData("[b]bold[/b] ", true, false, false, true)]
    [InlineData("stray [ bracket ", false, true, false, true)]
    [InlineData("stray [ bracket", false, true, false, false)]
    [InlineData("[b]bold[/b] stray [ bracket", true, true, false, false)]
    [InlineData("\\[OOC\\] note", false, true, false, false)]
    [InlineData("waves \\o/", false, false, true, false)]
    public void The_description_message_names_each_thing_done_to_it(string start, bool markup, bool brackets, bool backslashes, bool cut)
    {
        var file = CharacterFile.Parse(OldFile);
        file.FlavorText = start + (cut ? new string('a', 600) : "");

        var fix = CharacterRules.EnsureValid(file, Catalog, Fork).Single(f => f.Field == "flavorText");

        Assert.Equal(markup, fix.Message.Contains("Markup", StringComparison.Ordinal));
        Assert.Equal(brackets, fix.Message.Contains("parentheses", StringComparison.Ordinal));
        Assert.Equal(backslashes, fix.Message.Contains("Backslashes", StringComparison.Ordinal));
        Assert.Equal(cut, fix.Message.Contains("cut to 512", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MarkingColorRepair.RepeatLast, "#00FF00")]
    [InlineData(MarkingColorRepair.RepeatFirst, "#FF0000")]
    [InlineData(MarkingColorRepair.AllWhite, "#FFFFFF")]
    public void A_marking_that_gained_sprites_gets_its_colours_as_the_fork_gives_them(MarkingColorRepair repair, string added)
    {
        var file = CharacterFile.Parse(OldFile);
        CharacterRules.EnsureValid(file, Catalog, Fork);
        var look = file.ReadLook(Catalog);
        // A marking with three sprites saved with two colours, as before it gained one.
        var twoColours = new MarkingEntry("Bands", [Rgba.Parse("#FF0000"), Rgba.Parse("#00FF00")]);
        var markings = look.Markings.ToDictionary(o => o.Key, o => o.Value.ToDictionary(l => l.Key, l => l.Value.ToList()));
        markings["Torso"]["Chest"] = [twoColours];

        var valid = CharacterRules.EnsureValidLook(new CharacterLook { Species = look.Species, Sex = look.Sex, SkinColor = look.SkinColor, EyeColor = look.EyeColor, Markings = markings },
            Catalog.Species["Human"], Catalog, colorRepair: repair);

        var colors = valid.Markings["Torso"]["Chest"].Single(m => m.Id == "Bands").Colors;
        Assert.Equal(3, colors.Count);
        Assert.Equal(Rgba.Parse(added), colors[2]);
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
    public void Exports_are_laid_out_as_the_game_writes_them()
    {
        var file = CharacterFile.Parse(OldFile);
        CharacterRules.EnsureValid(file, Catalog, Fork);
        var yaml = file.ToYaml();

        Assert.Contains("- markingColor:", yaml);
        Assert.DoesNotContain("- markingId:", yaml);
        Assert.EndsWith("...\n", yaml);

        var created = CharacterFile.CreateNew("wizards").ToYaml();
        Assert.True(created.IndexOf("version:", StringComparison.Ordinal) < created.IndexOf("profile:", StringComparison.Ordinal));
        Assert.True(created.IndexOf("profile:", StringComparison.Ordinal) < created.IndexOf("forkId:", StringComparison.Ordinal));
    }

    [Fact]
    public void Spawn_choices_take_the_game_defaults_and_fallbacks()
    {
        var missing = CharacterFile.Parse(OldFile);
        CharacterRules.EnsureValid(missing, Catalog, Fork);
        Assert.Equal("None", missing.SpawnPriority);
        Assert.Equal("SpawnAsOverflow", missing.PreferenceUnavailable);

        var odd = CharacterFile.Parse(OldFile.Replace("  sex: Male", "  sex: Male\n  spawnPriority: Moon\n  preferenceUnavailable: Sulk"));
        var fixes = CharacterRules.EnsureValid(odd, Catalog, Fork);
        Assert.Equal("None", odd.SpawnPriority);
        Assert.Equal("StayInLobby", odd.PreferenceUnavailable);
        Assert.Contains(fixes, f => f.Field == "spawnPriority");

        var kept = CharacterFile.Parse(OldFile.Replace("  sex: Male", "  sex: Male\n  spawnPriority: Cryosleep"));
        CharacterRules.EnsureValid(kept, Catalog, Fork);
        Assert.Equal("Cryosleep", kept.SpawnPriority);
    }

    [Fact]
    public void Organs_that_take_markings_are_written_even_when_empty()
    {
        var file = CharacterFile.Parse(OldFile.Replace("hair: HairShort", "hair: HairNone").Replace("facialHair: Beard", "facialHair: None"));

        CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Contains("Head: {}", file.ToYaml());
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

    [Fact]
    public void Species_without_a_range_take_the_forks_default_heights()
    {
        var euphoria = CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(Yaml))]), (0.7f, 1.25f));
        var file = File("Human", "0.72");

        CharacterRules.EnsureValid(file, euphoria, DeltaVLike);

        Assert.Equal(0.72f, CharacterSize.ReadHeight(file, DeltaVLike));
        Assert.Equal((0.9f, 1.1f), (euphoria.Species["Small"].MinHeight, euphoria.Species["Small"].MaxHeight));
        Assert.Equal(0.8f, Catalog.Species["Human"].MinHeight);
    }

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

        Assert.Equal("1.1", file.GetValue(CharacterSize.DeltaVHeightKey));
        Assert.Contains(fixes, f => f.Field == "height");
    }

    [Fact]
    public void A_missing_height_is_written_as_1_without_a_message()
    {
        var file = File("Human", null);

        var fixes = CharacterRules.EnsureValid(file, Catalog, DeltaVLike);

        Assert.Equal("1", file.GetValue(CharacterSize.DeltaVHeightKey));
        Assert.DoesNotContain(fixes, f => f.Field == "height");
    }

    [Fact]
    public void Forks_without_heights_leave_the_key_alone()
    {
        var file = File("Human", "5");

        CharacterRules.EnsureValid(file, Catalog, Upstream);

        Assert.Equal("5", file.GetValue(CharacterSize.DeltaVHeightKey));
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
        - type: species
          id: Ipc
          name: species-name-ipc
          roundStart: true
          dollPrototype: Doll
          naming: FirstDashLast
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
        strings.Add("names-lizard-first-1 = Hisses\nnames-lizard-last-1 = Scales\nnamepreset-lastfirst = {$last} {$first}\nnamepreset-firstlast = {$first} {$last}\nnamepreset-firstdashfirst = {$first1}-{$first2}\n");
        return (new NameGenerator(index, strings, new Random(1)), CharacterCatalog.Build(index));
    }

    [Fact]
    public void Uses_the_species_naming_pattern_and_datasets()
    {
        var (names, catalog) = Build();

        Assert.Equal("Scales Hisses", names.Next(catalog.Species["Lizard"], "Male"));
    }

    [Fact]
    public void Euphorias_first_dash_last_puts_a_last_name_after_the_dash()
    {
        var (names, catalog) = Build();

        Assert.Equal("Hisses-Stone", names.Next(catalog.Species["Ipc"], "Male"));
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

public class VoiceTests
{
    // The test species plus upstream's voices: Human names none (so gets the game's defaults), Moth
    // has one, and Slime has a different default for each sex.
    private const string Voices = """
        - type: species
          id: Moth
          name: species-name-moth
          roundStart: true
          dollPrototype: AppearanceHuman
          defaultSoundsBySex: [ UnisexMoth, UnisexMoth, UnisexMoth ]
          voices: [ UnisexMoth ]
        - type: species
          id: Slime
          name: species-name-slime
          roundStart: true
          dollPrototype: AppearanceHuman
          sexes: [ Male, Female ]
          defaultSoundsBySex: [ SlimeMale, SlimeFemale, SlimeUnsexed ]
          voices: [ SlimeMale, SlimeFemale, SlimeUnsexed ]
        - type: emoteSounds
          id: MaleHuman
          voiceSelectorName: humanoid-profile-editor-voice-masculine
        - type: emoteSounds
          id: UnisexMoth
          voiceSelectorName: humanoid-profile-editor-voice-neutral
        """;

    private static readonly CharacterCatalog Catalog = CharacterCatalog.Build(PrototypeIndex.Load(
        [new PrototypeSource("h.yml", Encoding.UTF8.GetBytes(CharacterFileTests.Yaml + "\n" + Voices))]));

    private static readonly ForkInfo Fork = new("test", "Test", "o/r", "main", AppearanceModel.New, false, []);

    private static CharacterFile File(string species, string sex, string? voice) => CharacterFile.Parse($"""
        version: 2
        profile:
          name: Test Person
          species: {species}
          sex: {sex}
          gender: Male
          age: 30
        {(voice == null ? "" : $"  voice: {voice}")}
          appearance:
            skinColor: '#FFDA93FF'
            eyeColor: '#000000FF'
            markings: {"{}"}
        forkId: wizards-testing
        """);

    [Fact]
    public void Species_without_voices_get_the_game_defaults()
    {
        var human = Catalog.Species["Human"];

        Assert.True(Catalog.HasVoices);
        Assert.Equal(["MaleHuman", "FemaleHuman"], human.Voices);
        Assert.Equal("FemaleHuman", human.DefaultVoice("Female"));
        Assert.Equal("humanoid-profile-editor-voice-neutral", Catalog.VoiceNames["UnisexMoth"]);
    }

    [Fact]
    public void A_voice_the_species_cannot_use_becomes_its_default()
    {
        var file = File("Moth", "Male", "MaleHuman");

        var fixes = CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Equal("UnisexMoth", file.Voice);
        Assert.Contains(fixes, f => f.Field == "voice");
    }

    [Fact]
    public void A_saved_voice_the_species_can_use_is_kept()
    {
        var file = File("Moth", "Male", "UnisexMoth");

        var fixes = CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Equal("UnisexMoth", file.Voice);
        Assert.DoesNotContain(fixes, f => f.Field == "voice");
    }

    [Fact]
    public void A_file_with_no_voice_reads_as_MaleHuman()
    {
        var female = File("Human", "Female", null);

        CharacterRules.EnsureValid(female, Catalog, Fork);

        // MaleHuman is a human voice, so it stays, even for a female character.
        Assert.Equal("MaleHuman", female.Voice);
    }

    [Fact]
    public void The_voice_follows_the_sex_as_written_before_the_sex_is_fixed()
    {
        var file = File("Slime", "Unsexed", "UnisexMoth");

        CharacterRules.EnsureValid(file, Catalog, Fork);

        Assert.Equal("Male", file.Sex);
        Assert.Equal("SlimeUnsexed", file.Voice);
    }

    [Fact]
    public void Forks_without_voices_leave_the_file_alone()
    {
        var catalog = CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("h.yml", Encoding.UTF8.GetBytes(CharacterFileTests.Yaml))]));
        var file = File("Human", "Male", null);

        CharacterRules.EnsureValid(file, catalog, Fork);

        Assert.False(catalog.HasVoices);
        Assert.Null(file.Voice);
    }
}
