using Paperdoll.Core.Characters;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;

namespace Paperdoll.Core.Tests;

/// <summary>Starlight-style roundstart cybernetics on made-up fork "e": the parts, their points, attached parts and the game's check.</summary>
public class CyberneticsTests
{
    // A made-up character as Starlight's lobby exports it, with a steel leg.
    private const string WithLeg = """
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
          age: 52
          species: Human
          name: Oren Vasquez
          cybernetics:
          - SteelLeg
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
        version: 1
        forkId: e
        ...

        """;

    private static async Task<EditorSession> Opened(string yaml, ForkInfo? fork = null)
    {
        var editor = TestForks.Session();
        await editor.LoadForkAsync(fork ?? TestForks.E, update: false, ct: TestContext.Current.CancellationToken);
        editor.Open(yaml.ReplaceLineEndings("\n"));
        return editor;
    }

    private static string WithList(params string[] ids) =>
        WithLeg.Replace("  cybernetics:\n  - SteelLeg\n", ids.Length == 0 ? "" : "  cybernetics:\n" + string.Concat(ids.Select(id => $"  - {id}\n")));

    private static string WithSpecies(string yaml, string species) => yaml.Replace("species: Human", $"species: {species}");

    [Fact]
    public void Starlight_has_cybernetics()
    {
        Assert.True(KnownForks.Find("starlight")!.Extras.HasFlag(ProfileExtras.Cybernetics));
        Assert.False(KnownForks.Find("deltav")!.Extras.HasFlag(ProfileExtras.Cybernetics));
    }

    [Fact]
    public async Task The_parts_are_the_ones_the_game_offers()
    {
        await using var editor = await Opened(WithLeg);
        var parts = editor.Content!.Characters.Cybernetics;

        Assert.Equal(["GlowEye", "GripArm", "GripHand", "SteelLeg"], parts.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(("grip arm", 2, CyberneticType.Limb), (parts["GripArm"].Name, parts["GripArm"].Cost, parts["GripArm"].Type));
        Assert.Equal(["GripHand"], parts["GripArm"].AttachedParts);
        Assert.Equal(CyberneticType.Organ, parts["GlowEye"].Type);
        Assert.Empty(parts["SteelLeg"].AttachedParts);
        Assert.Equal("grip arm", editor.EntityName("GripArm"));
    }

    [Fact]
    public async Task Each_species_has_its_capacity()
    {
        await using var editor = await Opened(WithLeg);
        var species = editor.Content!.Characters.Species;

        Assert.Equal(3, species["Human"].CyberwareCapacity);
        Assert.Equal(6, species["Synth"].CyberwareCapacity);
        Assert.Equal(0, species["Slime"].CyberwareCapacity);
    }

    [Fact]
    public async Task A_file_with_cybernetics_comes_out_byte_for_byte()
    {
        await using var editor = await Opened(WithLeg);

        Assert.Empty(editor.LastFixes);
        Assert.Equal(WithLeg.ReplaceLineEndings("\n"), editor.Export());
    }

    [Fact]
    public async Task A_file_without_the_key_gets_none_written()
    {
        var plain = WithList();
        await using var editor = await Opened(plain);

        Assert.Equal(plain.ReplaceLineEndings("\n"), editor.Export());
        Assert.DoesNotContain(Cybernetics.Key, editor.Export());
    }

    [Fact]
    public async Task Removing_the_last_part_leaves_an_empty_list()
    {
        await using var editor = await Opened(WithLeg);

        editor.RemoveCybernetic("SteelLeg");

        Assert.Contains("  cybernetics: []\n", editor.Export());
    }

    [Fact]
    public async Task Installing_a_part_brings_its_attached_parts_and_their_points()
    {
        await using var editor = await Opened(WithSpecies(WithList(), "Synth"));

        Assert.Empty(editor.InstallCybernetic("GripArm"));
        Assert.Equal(["GripArm", "GripHand"], editor.File!.Cybernetics);
        Assert.Contains("  cybernetics:\n  - GripArm\n  - GripHand\n", editor.Export());
        Assert.Equal(3, Cybernetics.Cost(editor.File.Cybernetics, editor.Content!.Characters.Cybernetics));

        Assert.Empty(editor.InstallCybernetic("SteelLeg"));
        Assert.Equal(6, Cybernetics.Cost(editor.File.Cybernetics, editor.Content.Characters.Cybernetics));
    }

    [Fact]
    public async Task An_attached_part_already_installed_is_not_added_twice()
    {
        await using var editor = await Opened(WithSpecies(WithList("GripHand"), "Synth"));

        editor.InstallCybernetic("GripArm");

        Assert.Equal(["GripHand", "GripArm"], editor.File!.Cybernetics);
        Assert.Null(editor.WhyNotRemove("GripArm"));
        Assert.NotNull(editor.WhyNotRemove("GripHand"));
    }

    [Fact]
    public async Task An_install_over_the_species_points_is_refused()
    {
        await using var editor = await Opened(WithLeg);

        var reason = editor.WhyNotInstall("GripArm");
        var fixes = editor.InstallCybernetic("GripArm");

        Assert.Equal("Needs 3 points with the parts it brings; 0 of 3 left.", reason);
        Assert.Equal(reason, Assert.Single(fixes).Message);
        Assert.Equal(["SteelLeg"], editor.File!.Cybernetics);
    }

    [Fact]
    public async Task An_attached_part_cannot_be_removed_on_its_own()
    {
        await using var editor = await Opened(WithSpecies(WithList("GripArm", "GripHand"), "Synth"));

        var fixes = editor.RemoveCybernetic("GripHand");

        Assert.Equal("Comes attached to the grip arm; remove that instead.", Assert.Single(fixes).Message);
        Assert.Equal(["GripArm", "GripHand"], editor.File!.Cybernetics);

        editor.RemoveCybernetic("GripArm");
        Assert.Empty(editor.File.Cybernetics);
    }

    [Fact]
    public async Task Unknown_parts_are_dropped()
    {
        await using var editor = await Opened(WithList("SteelLeg", "PlainArm", "DeepLeg", "CyberBase"));

        Assert.Equal(["SteelLeg"], editor.File!.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Field == Cybernetics.Key && f.Message.Contains("PlainArm, DeepLeg, CyberBase"));
    }

    [Fact]
    public async Task Organ_parts_are_dropped()
    {
        await using var editor = await Opened(WithList("GlowEye", "SteelLeg"));

        Assert.Equal(["SteelLeg"], editor.File!.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Field == Cybernetics.Key && f.Message.Contains("glow eye"));
    }

    [Fact]
    public async Task Parts_listed_twice_are_merged_and_counted_once()
    {
        await using var editor = await Opened(WithList("SteelLeg", "SteelLeg"));

        Assert.Equal(["SteelLeg"], editor.File!.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Message == "Cybernetics listed twice were merged.");
    }

    [Fact]
    public async Task Parts_over_the_species_points_are_all_removed()
    {
        await using var editor = await Opened(WithList("SteelLeg", "GripHand"));

        Assert.Empty(editor.File!.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Message == "The cybernetics cost 4 points, more than Human's 3, so the game removes them all.");
    }

    [Fact]
    public async Task A_valid_list_keeps_its_order()
    {
        var yaml = WithSpecies(WithList("SteelLeg", "GripHand", "GripArm"), "Synth");
        await using var editor = await Opened(yaml);

        Assert.Empty(editor.LastFixes);
        Assert.Equal(yaml.ReplaceLineEndings("\n"), editor.Export());
    }

    [Fact]
    public async Task Changing_species_checks_the_parts_against_the_new_points()
    {
        await using var editor = await Opened(WithSpecies(WithList("GripArm", "GripHand", "SteelLeg"), "Synth"));
        Assert.Equal(3, editor.File!.Cybernetics.Count);

        editor.ChangeSpecies("Human");

        Assert.Empty(editor.File.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Field == Cybernetics.Key && f.Message.Contains("Human's 3"));

        editor.ChangeSpecies("Synth");
        editor.InstallCybernetic("GripHand");
        editor.ChangeSpecies("Slime");
        Assert.Empty(editor.File.Cybernetics);
        Assert.Contains(editor.LastFixes, f => f.Message == "Slime cannot start with cybernetics, so the game removes them all.");
    }

    [Fact]
    public async Task Forks_without_cybernetics_leave_the_list_alone()
    {
        await using var editor = await Opened(WithList("Unknown").Replace("forkId: e", "forkId: d"), TestForks.D);

        Assert.Equal(["Unknown"], editor.File!.Cybernetics);
    }
}
