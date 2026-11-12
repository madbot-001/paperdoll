using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;

namespace Paperdoll.Core.Tests;

/// <summary>Starlight's character records on made-up fork "e": admin entries, its limits and defaults, and its field order.</summary>
public class StarlightRecordsTests
{
    // A made-up character as Starlight's lobby exports it, records included, fields sorted by name, last first.
    private const string WithRecords = """
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
          gender: Female
          sex: Female
          age: 40
          species: Human
          name: Ines Marlow
          cosmaticDriftCharacterRecords:
            weight: 61
            securityEntries: []
            postmortemInstructions: Cremate
            medicalEntries:
            - title: Broken wrist
              involved: Dr. Avery
              description: Set and splinted.
            identifyingFeatures: Burn scar on the left hand
            height: 158
            hasWorkAuthorization: True
            employmentEntries: []
            emergencyContactName: Paul Marlow
            drugAllergies: None
            allergies: Pollen
            adminEntries:
            - title: Name change
              involved: Ines Marlow
              description: Formerly Ines Dale.
        version: 1
        forkId: e
        ...

        """;

    private static async Task<EditorSession> Opened(string yaml)
    {
        var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.E, update: false, ct: TestContext.Current.CancellationToken);
        editor.Open(yaml.ReplaceLineEndings("\n"));
        return editor;
    }

    private static string Records(string yaml) => yaml[yaml.IndexOf(CharacterRecords.Key, StringComparison.Ordinal)..yaml.IndexOf("version:", StringComparison.Ordinal)];

    [Fact]
    public void Starlight_and_the_Cosmatic_Drift_forks_keep_their_own_records()
    {
        Assert.Equal(RecordFormat.Starlight, KnownForks.Find("starlight")!.RecordFormat);
        Assert.True(KnownForks.Find("starlight")!.Extras.HasFlag(ProfileExtras.Records));
        Assert.Equal(RecordFormat.CosmaticDrift, KnownForks.Find("deltav")!.RecordFormat);
        Assert.Equal(RecordFormat.CosmaticDrift, KnownForks.Find("euphoria")!.RecordFormat);
        Assert.DoesNotContain("adminEntries", RecordFormat.CosmaticDrift.EntryLists);
    }

    [Fact]
    public async Task A_file_with_records_comes_out_byte_for_byte()
    {
        await using var editor = await Opened(WithRecords);

        Assert.Empty(editor.LastFixes);
        Assert.Equal(WithRecords.ReplaceLineEndings("\n"), editor.Export());
        Assert.Equal("Formerly Ines Dale.", Assert.Single(CharacterRecords.Entries(editor.File!, "adminEntries", RecordFormat.Starlight)).Description);
    }

    [Fact]
    public async Task New_records_take_Starlights_defaults_sorted_by_name_last_first()
    {
        await using var editor = await Opened(WithRecords);
        editor.Edit(f => f.Profile.Children.Remove(new YamlDotNet.RepresentationModel.YamlScalarNode(CharacterRecords.Key)));

        Assert.Equal("""
            cosmaticDriftCharacterRecords:
                weight: 74
                securityEntries: []
                postmortemInstructions: Return home
                medicalEntries: []
                identifyingFeatures: ""
                height: 164
                hasWorkAuthorization: True
                employmentEntries: []
                emergencyContactName: ""
                drugAllergies: None
                allergies: None
                adminEntries: []

            """.ReplaceLineEndings("\n"), Records(editor.Export()));
    }

    [Fact]
    public async Task Missing_fields_follow_the_ones_the_file_has_which_keep_their_place()
    {
        var partial = WithRecords.Replace("    adminEntries:\n    - title: Name change\n      involved: Ines Marlow\n      description: Formerly Ines Dale.\n", "")
            .Replace("    weight: 61\n", "");
        await using var editor = await Opened(partial);

        var records = Records(editor.Export());

        Assert.StartsWith("cosmaticDriftCharacterRecords:\n    securityEntries: []\n", records);
        Assert.EndsWith("    allergies: Pollen\n    weight: 74\n    adminEntries: []\n", records);
    }

    [Fact]
    public async Task Entry_descriptions_stop_at_4096_and_entries_are_written_title_first()
    {
        await using var editor = await Opened(WithRecords);

        editor.SetRecordEntries("adminEntries", [new RecordEntry("Note", "Staff", new string('z', 5000))]);

        var entry = Assert.Single(CharacterRecords.Entries(editor.File!, "adminEntries", RecordFormat.Starlight));
        Assert.Equal(4096, entry.Description.Length);
        Assert.Contains(editor.LastFixes, f => f.Field == CharacterRecords.Key && f.Message.Contains("up to 4096"));
        var records = Records(editor.Export());
        Assert.Contains("  - title: Note\n      involved: Staff\n      description: zzz", records);
    }

    [Fact]
    public async Task Out_of_range_height_and_a_missing_weight_take_Starlights_values()
    {
        await using var editor = await Opened(WithRecords.Replace("    height: 158\n", "    height: 999\n").Replace("    weight: 61\n", "    weight: heavy\n"));

        Assert.Equal(800, CharacterRecords.Number(editor.File!, "height", RecordFormat.Starlight));
        Assert.Equal(74, CharacterRecords.Number(editor.File!, "weight", RecordFormat.Starlight));
    }
}
