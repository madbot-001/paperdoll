using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Prototypes;

namespace Paperdoll.Core.Tests;

public class ForkExtrasTests
{
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: Doll
        - type: species
          id: Golem
          name: species-name-golem
          roundStart: true
          dollPrototype: Doll
          customName: false
        - type: entity
          id: Doll
        - type: reagent
          id: Water
          name: reagent-name-water
          group: Elements
        - type: reagent
          id: Ethanol
          name: reagent-name-ethanol
        """;

    private static readonly PrototypeIndex Prototypes = PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(Yaml))]);
    private static readonly CharacterCatalog Catalog = CharacterCatalog.Build(Prototypes);

    private static readonly ForkInfo EuphoriaLike = new("eu", "Eu", "o/r", "main", AppearanceModel.New, false, [])
    {
        Extras = ProfileExtras.CustomSpeciesName | ProfileExtras.Records | ProfileExtras.Allergies,
    };

    private static CharacterFile File(string species = "Human", string extra = "") => CharacterFile.Parse(
        $"version: 2\nprofile:\n  name: Test Person\n  species: {species}\n  age: 30\n  sex: Male\n  gender: Male\n{extra}"
        + "  appearance:\n    skinColor: '#FFFFFFFF'\n    eyeColor: '#000000FF'\n    markings: {}\nforkId: euphoria\n");

    [Fact]
    public void Missing_records_are_written_exactly_as_the_game_writes_its_defaults()
    {
        var file = File();

        var fixes = CharacterRules.EnsureValid(file, Catalog, EuphoriaLike);

        Assert.Contains("""
              cosmaticDriftCharacterRecords:
                employmentEntries: []
                securityEntries: []
                medicalEntries: []
                postmortemInstructions: Return home
                drugAllergies: None
                allergies: None
                identifyingFeatures: ""
                hasWorkAuthorization: True
                emergencyContactName: ""
                weight: 70
                height: 170
              cosmaticDriftAllergies: {}
            """.ReplaceLineEndings("\n"), file.ToYaml());
        Assert.DoesNotContain(fixes, f => f.Field == CharacterRecords.Key);
    }

    [Fact]
    public void Records_are_fitted_to_the_games_limits()
    {
        var file = File();
        CharacterRules.EnsureValid(file, Catalog, EuphoriaLike);
        CharacterRecords.SetNumber(file, "height", 900);
        CharacterRecords.SetText(file, "identifyingFeatures", new string('x', 70));
        CharacterRecords.SetEntries(file, "medicalEntries", [new RecordEntry("Checkup", "Dr. Test", new string('y', 9000))]);

        var fixes = CharacterRules.EnsureValid(file, Catalog, EuphoriaLike);

        Assert.Equal(800, CharacterRecords.Number(file, "height"));
        Assert.Equal(64, CharacterRecords.Text(file, "identifyingFeatures").Length);
        Assert.Equal(8192, Assert.Single(CharacterRecords.Entries(file, "medicalEntries")).Description.Length);
        Assert.Contains(fixes, f => f.Field == CharacterRecords.Key);
        // Game order: description, involved, title.
        var yaml = file.ToYaml();
        Assert.True(yaml.IndexOf("description:", StringComparison.Ordinal) < yaml.IndexOf("involved:", StringComparison.Ordinal));
        Assert.True(yaml.IndexOf("involved:", StringComparison.Ordinal) < yaml.IndexOf("title:", StringComparison.Ordinal));
    }

    [Fact]
    public void Records_missing_a_field_get_its_default_in_the_games_order()
    {
        var file = File(extra: "  cosmaticDriftCharacterRecords:\n    weight: 80\n    height: 180\n");

        CharacterRules.EnsureValid(file, Catalog, EuphoriaLike);

        Assert.Equal(80, CharacterRecords.Number(file, "weight"));
        Assert.Equal("Return home", CharacterRecords.Text(file, "postmortemInstructions"));
        var yaml = file.ToYaml();
        Assert.True(yaml.IndexOf("employmentEntries", StringComparison.Ordinal) < yaml.IndexOf("weight: 80", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_species_names_lose_markup_are_cut_and_need_a_species_that_allows_them()
    {
        var file = File(extra: "  customspeciename: '[color=red]Half[/color] Something With A Very Long Name Indeed'\n");
        CharacterRules.EnsureValid(file, Catalog, EuphoriaLike);
        Assert.Equal("Half Something With A Very Long ", file.GetValue(CustomSpeciesName.Key));

        var golem = File("Golem", "  customspeciename: Rock\n");
        var fixes = CharacterRules.EnsureValid(golem, Catalog, EuphoriaLike);
        Assert.Equal("", golem.GetValue(CustomSpeciesName.Key));
        Assert.Contains(fixes, f => f.Field == CustomSpeciesName.Key);
    }

    [Fact]
    public void Allergies_are_saved_as_reagent_and_amount()
    {
        var file = File();
        Allergies.Write(file, [("Water", 0.5f), ("Ethanol", 100f)]);

        var again = CharacterFile.Parse(file.ToYaml());

        Assert.Equal([("Water", 0.5f), ("Ethanol", 100f)], Allergies.Read(again));
        Assert.Contains("Water: 0.5", again.ToYaml());
        Assert.Equal("Extreme", Allergies.StrengthName(100f));
        Assert.Equal(["Elements", "Unknown"], Allergies.Reagents(Prototypes).OrderBy(r => r.Id).Select(r => r.Group).Reverse());
    }

    [Fact]
    public void Forks_without_the_extras_leave_the_file_alone()
    {
        var file = File();

        CharacterRules.EnsureValid(file, Catalog, new ForkInfo("up", "Up", "o/r", "main", AppearanceModel.New, false, []));

        Assert.DoesNotContain(CharacterRecords.Key, file.ToYaml());
        Assert.DoesNotContain(Allergies.Key, file.ToYaml());
        Assert.DoesNotContain(CustomSpeciesName.Key, file.ToYaml());
    }
}
