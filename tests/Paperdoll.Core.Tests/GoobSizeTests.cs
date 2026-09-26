using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Prototypes;

namespace Paperdoll.Core.Tests;

/// <summary>Goob's height and width (<see cref="SizeRule.HeightAndWidth"/>).</summary>
public class GoobSizeTests
{
    // Human takes the defaults; Tall has its own ranges, sizes and a round body of radius 0.35
    // and density 185 (71.2 kg at size 1).
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
        - type: species
          id: Tall
          name: species-name-tall
          roundStart: true
          prototype: MobTall
          minHeight: 1.0
          maxHeight: 1.5
          defaultHeight: 1.25
          minWidth: 0.9
          maxWidth: 1.2
          defaultWidth: 1.05
          sizeRatio: 1.3
          averageHeight: 200
          averageWidth: 50
        - type: entity
          id: MobTall
          components:
          - type: Fixtures
            fixtures:
              fix1:
                shape: !type:PhysShapeCircle
                  radius: 0.35
                density: 185
        """;

    private static readonly CharacterCatalog Catalog = CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(Yaml))]));
    private static readonly ForkInfo GoobLike = new("goobish", "Goobish", "o/r", "main", AppearanceModel.Old, true, []) { SizeRule = SizeRule.HeightAndWidth };

    private static CharacterFile File(string species, string? height, string? width) => CharacterFile.Parse(
        "profile:\n  preferenceUnavailable: SpawnAsOverflow\n  spawnPriority: None\n  appearance:\n    markings: {}\n    skinColor: '#FFFFFFFF'\n    eyeColor: '#000000FF'\n"
        + (width != null ? $"  width: {width}\n" : "") + (height != null ? $"  height: {height}\n" : "")
        + $"  gender: Male\n  sex: Male\n  age: 30\n  species: {species}\n  flavorText: \"\"\n  name: Ann Bee\nversion: 2\nforkId: goobish\n");

    [Fact]
    public void Species_read_their_size_fields_with_goobs_defaults()
    {
        var human = Catalog.Species["Human"];
        var tall = Catalog.Species["Tall"];

        Assert.Equal((0.8f, 1.2f, 0.85f, 1.15f, 1.2f), (human.MinHeight, human.MaxHeight, human.MinWidth, human.MaxWidth, human.SizeRatio));
        Assert.Equal((176.1f, 40f), (human.AverageHeight, human.AverageWidth));
        Assert.Null(human.Mass);
        Assert.Equal((1.25f, 1.05f, 1.3f), (tall.DefaultHeight, tall.DefaultWidth, tall.SizeRatio));
        Assert.Equal(71.2f, tall.Mass!.Value, 0.1f);
    }

    [Fact]
    public void Sizes_in_range_are_left_exactly_as_written()
    {
        var file = File("Tall", "1.0523432", "1.1");
        var before = file.ToYaml();

        var fixes = CharacterRules.EnsureValid(file, Catalog, GoobLike);

        Assert.Empty(fixes);
        Assert.Equal(before, file.ToYaml());
    }

    [Fact]
    public void Sizes_out_of_range_are_clamped_without_rounding()
    {
        var file = File("Tall", "1.7", "0.8512");

        var fixes = CharacterRules.EnsureValid(file, Catalog, GoobLike);

        Assert.Equal("1.5", file.GetValue("height"));
        Assert.Equal("0.9", file.GetValue("width"));
        Assert.Contains(fixes, f => f.Field == "height");
        Assert.Contains(fixes, f => f.Field == "width");
        // Goob does not keep the pair within its ratio on import, only in the lobby's sliders.
        Assert.Empty(CharacterRules.EnsureValid(File("Tall", "1.5", "0.9"), Catalog, GoobLike));
    }

    [Fact]
    public void Missing_sizes_become_the_smallest_as_the_game_reads_them()
    {
        var file = File("Tall", null, null);

        var fixes = CharacterRules.EnsureValid(file, Catalog, GoobLike);

        Assert.Equal("1", file.GetValue("height"));
        Assert.Equal("0.9", file.GetValue("width"));
        Assert.Equal(2, fixes.Count(f => f.Field is "height" or "width"));
    }

    [Theory]
    // Moving height to 1.5 with width 0.9 (ratio 1.67, past 1.3): width follows to 1.5 / 1.3.
    [InlineData(1.5f, 0.9f, true, 1.5f, 1.1538f)]
    // Moving width to 0.9 with height 1.5: height follows to 0.9 * 1.3.
    [InlineData(1.5f, 0.9f, false, 1.17f, 0.9f)]
    // Within the ratio, nothing is pulled.
    [InlineData(1.2f, 1.0f, true, 1.2f, 1.0f)]
    public void The_sliders_keep_height_and_width_within_the_ratio(float height, float width, bool heightMoved, float expectedHeight, float expectedWidth)
    {
        var (h, w) = CharacterSize.KeepRatio(height, width, Catalog.Species["Tall"], heightMoved);

        Assert.Equal(expectedHeight, h, 0.001f);
        Assert.Equal(expectedWidth, w, 0.001f);
    }

    [Fact]
    public void The_sprite_is_scaled_by_width_across_and_height_up()
    {
        var file = File("Tall", "1.4", "0.95");

        Assert.Equal((0.95f, 1.4f), CharacterSize.SpriteScale(GoobLike, Catalog.Species["Tall"], file));
        Assert.Equal((1.05f, 1.25f), CharacterSize.DefaultScale(GoobLike, Catalog.Species["Tall"]));
    }

    [Fact]
    public void Centimetres_and_weight_are_shown_as_goobs_lobby_shows_them()
    {
        var tall = Catalog.Species["Tall"];

        Assert.Equal((250, 55), CharacterSize.Centimetres(tall, 1.25f, 1.1f));
        // 71.2 kg times the mean of height and width, cut to whole kilograms.
        Assert.Equal(83, CharacterSize.Kilograms(tall, 1.25f, 1.1f));
        // Without a simple body shape the lobby shows a flat 71.
        Assert.Equal(71, CharacterSize.Kilograms(Catalog.Species["Human"], 1.2f, 1.1f));
    }
}
