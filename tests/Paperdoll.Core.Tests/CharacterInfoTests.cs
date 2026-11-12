using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;

namespace Paperdoll.Core.Tests;

/// <summary>Starlight-style character info on made-up fork "d": the info texts, the description's two names, glowing flags and silicon voices.</summary>
public class CharacterInfoTests
{
    // A made-up character as an old-model fork's lobby exports it, without any of the info keys.
    private const string Plain = """
        profile:
          preferenceUnavailable: SpawnAsOverflow
          spawnPriority: None
          appearance:
            markings: []
            skinColor: '#C8A080FF'
            eyeColor: '#304050FF'
            facialHairColor: '#221100FF'
            facialHair: FacialHairShaved
            hairColor: '#221100FF'
            hair: HairShort
          gender: Male
          sex: Male
          age: 31
          species: Human
          name: Tomas Reyes
        version: 1
        forkId: d
        ...

        """;

    private static async Task<EditorSession> Opened(string yaml)
    {
        var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.D, update: false, ct: TestContext.Current.CancellationToken);
        editor.Open(yaml.ReplaceLineEndings("\n"));
        return editor;
    }

    private static string WithProfileLines(params string[] lines) =>
        Plain.Replace("  name: Tomas Reyes\n", "  name: Tomas Reyes\n" + string.Concat(lines.Select(l => $"  {l}\n")));

    [Fact]
    public async Task A_file_without_the_info_keys_comes_out_byte_for_byte()
    {
        await using var editor = await Opened(Plain);

        Assert.Equal(Plain.ReplaceLineEndings("\n"), editor.Export());
    }

    [Fact]
    public async Task Each_info_text_is_saved_under_the_games_key()
    {
        await using var editor = await Opened(Plain);

        editor.Edit(f =>
        {
            f.SetValue(CharacterInfo.PersonalityDescription, "Calm.");
            f.SetValue(CharacterInfo.ExploitableInfo, "Owes money.");
            f.SetValue(CharacterInfo.Secrets, "Hates the cold.");
            f.SetValue(CharacterInfo.PersonalNotes, "Play quietly.");
            f.SetValue(CharacterInfo.OocNotes, "Ask before drama.");
        });
        var yaml = editor.Export();

        Assert.Contains("  personalityDescription: Calm.\n", yaml);
        Assert.Contains("  exploitableInfo: Owes money.\n", yaml);
        Assert.Contains("  secrets: Hates the cold.\n", yaml);
        Assert.Contains("  personalNotes: Play quietly.\n", yaml);
        Assert.Contains("  oOCNotes: Ask before drama.\n", yaml);
        Assert.DoesNotContain("oocNotes", yaml);
    }

    [Fact]
    public async Task Clearing_an_info_text_saves_it_empty()
    {
        await using var editor = await Opened(WithProfileLines("secrets: Hates the cold."));

        editor.Edit(f => f.SetValue(CharacterInfo.Secrets, ""));

        Assert.Contains("  secrets: \"\"\n", editor.Export());
    }

    [Fact]
    public async Task The_physical_description_is_saved_under_both_names()
    {
        await using var editor = await Opened(Plain);

        editor.Edit(f => f.PhysicalDescription = "Tall.");
        var yaml = editor.Export();

        Assert.Contains("  physicalDescription: Tall.\n", yaml);
        Assert.Contains("  flavorText: Tall.\n", yaml);
    }

    [Fact]
    public async Task When_the_two_names_disagree_both_take_flavorText_as_the_game_reads_it()
    {
        await using var editor = await Opened(WithProfileLines("physicalDescription: Short.", "flavorText: Tall."));

        Assert.Equal("Tall.", editor.File!.PhysicalDescription);
        Assert.Equal("Tall.", editor.File.GetValue("physicalDescription"));
        Assert.Equal("Tall.", editor.File.FlavorText);
        Assert.Contains(editor.LastFixes, f => f.Field == "physicalDescription");
    }

    [Fact]
    public async Task A_description_under_one_name_is_copied_to_the_other()
    {
        await using var editor = await Opened(WithProfileLines("physicalDescription: Short."));

        Assert.Equal("Short.", editor.File!.FlavorText);
        Assert.Equal("Short.", editor.File.GetValue("physicalDescription"));
    }

    [Fact]
    public async Task The_description_rule_cleans_and_cuts_both_names()
    {
        var limit = TestForks.D.MaxFlavorTextLength;
        await using var editor = await Opened(Plain);

        editor.Edit(f => f.PhysicalDescription = "[color=red]" + new string('x', limit + 10) + "[/color]");

        Assert.Equal(new string('x', limit), editor.File!.FlavorText);
        Assert.Equal(new string('x', limit), editor.File.GetValue("physicalDescription"));
        var fix = Assert.Single(editor.LastFixes, f => f.Field == "flavorText");
        Assert.Contains("Markup", fix.Message);
        Assert.Contains($"cut to {limit}", fix.Message);
    }

    [Fact]
    public async Task Forks_without_the_info_keep_the_description_under_flavorText_alone()
    {
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: TestContext.Current.CancellationToken);
        editor.NewCharacter("Human");

        editor.Edit(f => f.FlavorText = "Tall.");

        Assert.Null(editor.File!.GetValue("physicalDescription"));
    }

    [Fact]
    public async Task Glowing_flags_are_written_only_once_turned_on()
    {
        await using var editor = await Opened(Plain);

        editor.Edit(f => f.SetGlowing(CharacterInfo.HairGlowing, false));
        Assert.Equal(Plain.ReplaceLineEndings("\n"), editor.Export());

        editor.Edit(f => f.SetGlowing(CharacterInfo.EyeGlowing, true));
        Assert.Contains("    eyeGlowing: True\n", editor.Export());
        Assert.True(editor.File!.IsGlowing(CharacterInfo.EyeGlowing));

        editor.Edit(f => f.SetGlowing(CharacterInfo.EyeGlowing, false));
        Assert.Contains("    eyeGlowing: False\n", editor.Export());
        Assert.False(editor.File.IsGlowing(CharacterInfo.EyeGlowing));
    }

    [Fact]
    public async Task Silicon_voices_are_the_voice_prototypes_marked_silicon()
    {
        await using var editor = await Opened(Plain);

        var voices = editor.Content!.Characters.SiliconVoices;

        Assert.Equal([("Hum", "tts-voice-hum"), ("Chirp", "tts-voice-chirp")], voices);
        Assert.Equal("Low Hum", editor.Content.Strings["tts-voice-hum"]);

        editor.Edit(f => f.SetValue(CharacterInfo.SiliconVoice, "Chirp"));
        Assert.Contains("  siliconVoice: Chirp\n", editor.Export());
    }

    [Fact]
    public async Task Forks_without_voice_prototypes_have_no_silicon_voices()
    {
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.C, update: false, ct: TestContext.Current.CancellationToken);

        Assert.Empty(editor.Content!.Characters.SiliconVoices);
    }
}
