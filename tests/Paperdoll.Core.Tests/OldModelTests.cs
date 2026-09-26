using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>Species on the old appearance model: reading, the game's rules, writing and drawing.</summary>
public class OldModelTests
{
    // Two old-model species over one doll. Human: a per-sex torso and head, arms, eyes, and hair
    // and horn layers that only hold markings; the horn layer shows none on Human. Points: one
    // hair, one facial hair, two for both arms together, one chest, one head top with a default.
    // Slime: only markings made for it, and chest markings take the skin colour at half alpha.
    private const string Yaml = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: MobHumanDummy
          sprites: MobHumanSprites
          markingLimits: MobHumanMarkingLimits
        - type: species
          id: Slime
          name: species-name-slime
          roundStart: true
          dollPrototype: MobHumanDummy
          sprites: MobSlimeSprites
          markingLimits: MobSlimeMarkingLimits
        - type: entity
          id: MobHumanDummy
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
            - map: [ "enum.HumanoidVisualLayers.Head" ]
            - map: [ "enum.HumanoidVisualLayers.Eyes" ]
            - map: [ "enum.HumanoidVisualLayers.RArm" ]
            - map: [ "enum.HumanoidVisualLayers.LArm" ]
            - map: [ "enum.HumanoidVisualLayers.Hair" ]
            - map: [ "enum.HumanoidVisualLayers.HeadTop" ]
          - type: HumanoidAppearance
            species: Human
            hideLayersOnEquip: [ Hair, HeadTop ]
        - type: speciesBaseSprites
          id: MobHumanSprites
          sprites:
            Chest: MobHumanTorso
            Head: MobHumanHead
            Eyes: MobHumanoidEyes
            RArm: MobHumanRArm
            LArm: MobHumanLArm
            Hair: MobHumanoidAnyMarking
            FacialHair: MobHumanoidAnyMarking
            HeadTop: MobHumanoidNoMarkings
        - type: speciesBaseSprites
          id: MobSlimeSprites
          sprites:
            Chest: MobSlimeTorso
            Head: MobHumanHead
            Hair: MobHumanoidAnyMarking
        - type: humanoidBaseSprite
          id: MobHumanTorso
          baseSprite: { sprite: Mobs/human.rsi, state: torso_m }
        - type: humanoidBaseSprite
          id: MobHumanTorsoMale
          baseSprite: { sprite: Mobs/human.rsi, state: torso_m }
        - type: humanoidBaseSprite
          id: MobHumanTorsoFemale
          baseSprite: { sprite: Mobs/human.rsi, state: torso_f }
        - type: humanoidBaseSprite
          id: MobHumanHead
          baseSprite: { sprite: Mobs/human.rsi, state: head }
        - type: humanoidBaseSprite
          id: MobHumanoidEyes
          baseSprite: { sprite: Mobs/human.rsi, state: eyes }
          matchSkin: false
        - type: humanoidBaseSprite
          id: MobHumanRArm
          baseSprite: { sprite: Mobs/human.rsi, state: r_arm }
        - type: humanoidBaseSprite
          id: MobHumanLArm
          baseSprite: { sprite: Mobs/human.rsi, state: l_arm }
        - type: humanoidBaseSprite
          id: MobHumanoidAnyMarking
        - type: humanoidBaseSprite
          id: MobHumanoidNoMarkings
          allowsMarkings: false
        - type: humanoidBaseSprite
          id: MobSlimeTorso
          baseSprite: { sprite: Mobs/human.rsi, state: torso_m }
          markingsMatchSkin: true
          layerAlpha: 0.5
        - type: markingPoints
          id: MobHumanMarkingLimits
          points:
            Hair: { points: 1, required: false }
            FacialHair: { points: 1, required: false }
            Arms: { points: 2, required: false }
            Chest: { points: 1, required: false }
            HeadTop: { points: 1, required: false, defaultMarkings: [ Horns ] }
        - type: markingPoints
          id: MobSlimeMarkingLimits
          onlyWhitelisted: true
          points:
            Hair: { points: 1, required: false }
            Chest: { points: 1, required: false }
        - type: marking
          id: HairShort
          bodyPart: Hair
          markingCategory: Hair
          sprites: [ { sprite: Mobs/hair.rsi, state: short } ]
        - type: marking
          id: HairSlime
          bodyPart: Hair
          markingCategory: Hair
          speciesRestriction: [ Slime ]
          sprites: [ { sprite: Mobs/hair.rsi, state: slime } ]
        - type: marking
          id: Beard
          bodyPart: FacialHair
          markingCategory: FacialHair
          sexRestriction: Male
          sprites: [ { sprite: Mobs/hair.rsi, state: beard } ]
        - type: marking
          id: BandRight
          bodyPart: RArm
          markingCategory: Arms
          sprites: [ { sprite: Mobs/arms.rsi, state: band_r } ]
        - type: marking
          id: SleeveRight
          bodyPart: RArm
          markingCategory: Arms
          sprites: [ { sprite: Mobs/arms.rsi, state: sleeve_r } ]
        - type: marking
          id: BandLeft
          bodyPart: LArm
          markingCategory: Arms
          sprites: [ { sprite: Mobs/arms.rsi, state: band_l } ]
        - type: marking
          id: Stripes
          bodyPart: Chest
          markingCategory: Chest
          sprites:
          - { sprite: Mobs/chest.rsi, state: a }
          - { sprite: Mobs/chest.rsi, state: b }
        - type: marking
          id: Bra
          bodyPart: Chest
          markingCategory: Chest
          sexRestriction: Female
          sprites: [ { sprite: Mobs/chest.rsi, state: bra } ]
        - type: marking
          id: SlimeSpots
          bodyPart: Chest
          markingCategory: Chest
          speciesRestriction: [ Slime ]
          sprites: [ { sprite: Mobs/chest.rsi, state: spots } ]
        - type: marking
          id: Horns
          bodyPart: HeadTop
          markingCategory: HeadTop
          sprites: [ { sprite: Mobs/horns.rsi, state: horns } ]
        """;

    private static readonly PrototypeIndex Index = PrototypeIndex.Load([new PrototypeSource("old.yml", Encoding.UTF8.GetBytes(Yaml))]);
    private static readonly CharacterCatalog Catalog = CharacterCatalog.Build(Index);
    private static readonly ForkInfo Fork = new("oldtest", "Old test", "o/r", "main", AppearanceModel.Old, true, ["oldtest"]);

    // A made-up character in the shape the old model's export writes.
    private const string File = """
        profile:
          preferenceUnavailable: SpawnAsOverflow
          spawnPriority: None
          appearance:
            markings:
            - markingId: BandRight
              visible: True
              markingColor:
              - '#112233FF'
            - markingId: BandLeft
              visible: True
              markingColor:
              - '#445566FF'
            - markingId: Stripes
              visible: True
              markingColor:
              - '#AA0000FF'
              - '#00AA00FF'
            skinColor: '#C8A080FF'
            eyeColor: '#304050FF'
            facialHairColor: '#221100FF'
            facialHair: Beard
            hairColor: '#221100FF'
            hair: HairShort
          gender: Male
          sex: Male
          age: 31
          species: Human
          flavorText: ""
          name: Tomas Reyes
        version: 1
        forkId: oldtest
        """;

    private static CharacterFile Checked(string text, out IReadOnlyList<RuleFix> fixes)
    {
        var file = CharacterFile.Parse(text);
        fixes = CharacterRules.EnsureValid(file, Catalog, Fork);
        return file;
    }

    [Fact]
    public void Each_marking_category_becomes_an_organ_holding_the_layers_it_draws_on()
    {
        var human = Catalog.Species["Human"];

        Assert.NotNull(human.Old);
        Assert.Equal(["LArm", "RArm"], human.Organs.Single(o => o.Category == "Arms").MarkingLayers.Order());
        Assert.Equal(["Hair"], human.Organs.Single(o => o.Category == "Hair").MarkingLayers);
        // Slime takes only markings made for it.
        Assert.Equal(["HairSlime"], Catalog.Markings.Values.Where(m => Catalog.Species["Slime"].Old!.Offers(m, "Slime")).Select(m => m.Id).Where(id => id.StartsWith("Hair")));
        Assert.Equal(["Hair", "HeadTop"], human.Old.HideOnEquip);
    }

    [Fact]
    public void Chest_and_head_have_per_sex_sprites_where_the_fork_has_them()
    {
        var chest = Catalog.Species["Human"].Old!.Layer("Chest")!;

        Assert.Equal("torso_f", chest.ForSex("Female").Sprite!.Value.State);
        Assert.Equal("torso_m", chest.ForSex("Unsexed").Sprite!.Value.State);
        // No MobHumanHeadFemale: the plain head is used.
        Assert.Equal("head", Catalog.Species["Human"].Old!.Layer("Head")!.ForSex("Female").Sprite!.Value.State);
    }

    [Fact]
    public void A_valid_file_comes_back_unchanged()
    {
        var file = Checked(File, out var fixes);

        Assert.Empty(fixes);
        Assert.Equal(File.TrimEnd() + "\n...\n", file.ToYaml().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Markings_are_read_by_category_with_hair_in_its_own()
    {
        var look = CharacterFile.Parse(File).ReadLook(Catalog);

        Assert.Equal("HairShort", Assert.Single(look.Markings["Hair"]["Hair"]).Id);
        Assert.Equal(Rgba.Parse("#221100"), look.Markings["Hair"]["Hair"][0].Colors[0]);
        Assert.Equal("BandRight", Assert.Single(look.Markings["Arms"]["RArm"]).Id);
        Assert.Equal("BandLeft", Assert.Single(look.Markings["Arms"]["LArm"]).Id);
        Assert.Equal(2, look.Markings["Chest"]["Chest"][0].Colors.Count);
    }

    [Fact]
    public void A_category_keeps_as_many_markings_as_its_points_first_ones_first()
    {
        var text = File.Replace("""
                - markingId: Stripes
            """, """
                - markingId: SleeveRight
                  visible: True
                  markingColor:
                  - '#FFFFFFFF'
                - markingId: Stripes
            """);

        var look = Checked(text, out var fixes).ReadLook(Catalog);

        Assert.Equal(["BandRight"], look.Markings["Arms"]["RArm"].Select(m => m.Id));
        Assert.Equal(["BandLeft"], look.Markings["Arms"]["LArm"].Select(m => m.Id));
        Assert.Contains(fixes, f => f.Message.Contains("Arms takes 2"));
    }

    [Fact]
    public void Unknown_hair_becomes_none_and_unknown_markings_go()
    {
        var text = File.Replace("hair: HairShort", "hair: HairGone").Replace("markingId: BandLeft", "markingId: BandGone");

        var file = Checked(text, out var fixes);
        var yaml = file.ToYaml();

        Assert.Contains("hair: HairBald", yaml);
        Assert.Contains("hairColor: '#221100FF'", yaml);
        Assert.DoesNotContain("BandGone", yaml);
        Assert.Contains(fixes, f => f.Field == "hair");
        Assert.Contains(fixes, f => f.Message.Contains("BandGone"));
    }

    [Fact]
    public void Colours_that_do_not_match_the_sprites_are_reset_to_white()
    {
        var text = File.Replace("""
                  - '#AA0000FF'
                  - '#00AA00FF'
            """, """
                  - '#AA0000FF'
            """);

        var look = Checked(text, out _).ReadLook(Catalog);

        Assert.Equal([Rgba.White, Rgba.White], look.Markings["Chest"]["Chest"][0].Colors);
    }

    [Fact]
    public void Markings_for_other_species_or_another_sex_go_but_hair_is_left_alone()
    {
        var text = File.Replace("markingId: Stripes", "markingId: SlimeSpots").Replace("sex: Male", "sex: Female");

        var look = Checked(text, out var fixes).ReadLook(Catalog);

        Assert.False(look.Markings.ContainsKey("Chest"));
        // The game does not check hair against species or sex; it just does not draw it.
        Assert.Equal("Beard", look.Markings["FacialHair"]["FacialHair"][0].Id);
        Assert.Contains(fixes, f => f.Message.Contains("SlimeSpots"));
    }

    [Fact]
    public void Markings_on_layers_that_match_the_skin_take_its_colour()
    {
        var text = File.Replace("species: Human", "species: Slime").Replace("markingId: Stripes", "markingId: SlimeSpots")
            .Replace("""
                  - '#AA0000FF'
                  - '#00AA00FF'
            """, """
                  - '#AA0000FF'
            """);

        var look = Checked(text, out _).ReadLook(Catalog);

        Assert.Equal(Rgba.Parse("#C8A080").WithAlpha(0.5f).ToHex(), Assert.Single(look.Markings["Chest"]["Chest"]).Colors[0].ToHex());
        // Slime takes only its own markings, so the arm bands go.
        Assert.False(look.Markings.ContainsKey("Arms"));
    }

    [Fact]
    public void Changing_one_marking_rewrites_only_its_entry()
    {
        var file = CharacterFile.Parse(File);
        var look = file.ReadLook(Catalog);
        look.Markings["Arms"]["LArm"][0] = look.Markings["Arms"]["LArm"][0] with { Colors = [Rgba.Parse("#0000FF")] };

        file.WriteLook(look, Catalog);

        var expected = File.Replace("'#445566FF'", "'#0000FFFF'");
        Assert.Equal(expected.TrimEnd() + "\n...\n", file.ToYaml().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Removing_hair_writes_the_bald_style_and_keeps_its_colour()
    {
        var file = CharacterFile.Parse(File);
        var look = file.ReadLook(Catalog);
        look.Markings.Remove("Hair");

        file.WriteLook(look, Catalog);

        Assert.Contains("hair: HairBald", file.ToYaml());
        Assert.Contains("hairColor: '#221100FF'", file.ToYaml());
        Assert.Equal(Rgba.Parse("#221100"), file.OldHairColor(facial: false));
    }

    [Fact]
    public void A_new_character_is_written_in_the_old_shape()
    {
        var file = CharacterFile.CreateNew("oldtest");

        file.WriteLook(new CharacterLook { Species = "Human", SkinColor = Rgba.Parse("#C8A080"), EyeColor = Rgba.Parse("#304050") }, Catalog);

        Assert.True(file.IsOldModel);
        Assert.Equal("1", file.Version);
        var appearance = CharacterFile.Parse(file.ToYaml()).ReadLook(Catalog);
        Assert.Empty(appearance.Markings);
        Assert.Contains("""
                markings: []
                skinColor: '#C8A080FF'
                eyeColor: '#304050FF'
                facialHairColor: '#000000FF'
                facialHair: FacialHairShaved
                hairColor: '#000000FF'
                hair: HairBald
            """, file.ToYaml().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_new_model_file_is_converted_for_an_old_model_species()
    {
        var file = CharacterFile.CreateNew("oldtest");
        file.WriteLook(new CharacterLook
        {
            Species = "Human",
            Markings = new() { ["Head"] = new() { ["Hair"] = [new MarkingEntry("HairShort", [Rgba.Parse("#123456")])] } },
        });

        var look = file.ReadLook(Catalog);
        file.WriteLook(look, Catalog);

        Assert.Equal("HairShort", look.Markings["Hair"]["Hair"][0].Id);
        Assert.Contains("hair: HairShort", file.ToYaml());
        Assert.Contains("hairColor: '#123456FF'", file.ToYaml());
        Assert.Equal("1", file.Version);
    }

    // Drawing.

    private static PaperdollRenderer Renderer()
    {
        var textures = new Dictionary<string, byte[]>
        {
            ["Mobs/human.rsi/meta.json"] = Meta("torso_m", "torso_f", "head", "eyes", "r_arm", "l_arm"),
            ["Mobs/hair.rsi/meta.json"] = Meta("short", "slime", "beard"),
            ["Mobs/arms.rsi/meta.json"] = Meta("band_r", "sleeve_r", "band_l"),
            ["Mobs/chest.rsi/meta.json"] = Meta("a", "b", "bra", "spots"),
            ["Mobs/horns.rsi/meta.json"] = Meta("horns"),
        };
        foreach (var (rsi, meta) in textures.ToList())
        {
            foreach (var state in System.Text.Json.JsonDocument.Parse(meta).RootElement.GetProperty("states").EnumerateArray())
                textures[rsi.Replace("meta.json", state.GetProperty("name").GetString() + ".png")] = Pixel();
        }
        return new PaperdollRenderer(Catalog, Index, new MemoryTextures(textures));
    }

    private static byte[] Meta(params string[] states) => Encoding.UTF8.GetBytes(
        $$"""{"version":1,"license":"CC0-1.0","copyright":"test","size":{"x":32,"y":32},"states":[{{string.Join(',', states.Select(s => $$"""{"name":"{{s}}","directions":1}"""))}}]}""");

    private static byte[] Pixel()
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.SetPixel(0, 0, SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static List<string> States(CharacterLook look) =>
        Renderer().Layers(look).Select(l => l.Sprite.State!).ToList();

    [Fact]
    public void Base_layers_follow_the_doll_with_the_per_sex_torso_and_markings_above_their_layer()
    {
        var look = CharacterFile.Parse(File.Replace("sex: Male", "sex: Female")).ReadLook(Catalog);

        // Female: the female torso; the beard is male only and not drawn; the horn layer shows no
        // markings, so its default is not drawn either.
        Assert.Equal(["torso_f", "a", "b", "head", "eyes", "r_arm", "band_r", "l_arm", "band_l", "short"], States(look));
    }

    [Fact]
    public void Later_markings_on_a_layer_go_beneath_earlier_ones()
    {
        var look = CharacterFile.Parse(File).ReadLook(Catalog);
        look.Markings["Arms"]["RArm"].Insert(0, new MarkingEntry("SleeveRight", [Rgba.White]));
        look.Markings["Arms"].Remove("LArm");

        var states = States(look);

        Assert.Equal(["r_arm", "band_r", "sleeve_r"], states.Skip(states.IndexOf("r_arm")).Take(3));
    }

    [Fact]
    public void Base_layers_take_the_skin_colour_and_eyes_the_eye_colour()
    {
        var look = CharacterFile.Parse(File).ReadLook(Catalog);

        var layers = Renderer().Layers(look);

        Assert.Equal(Rgba.Parse("#C8A080"), layers.Single(l => l.Sprite.State == "torso_m").Color);
        Assert.Equal(Rgba.Parse("#304050"), layers.Single(l => l.Sprite.State == "eyes").Color);
        Assert.Equal(Rgba.Parse("#221100"), layers.Single(l => l.Sprite.State == "short").Color);
    }

    [Fact]
    public void Hair_the_species_cannot_have_is_not_drawn()
    {
        var look = CharacterFile.Parse(File.Replace("hair: HairShort", "hair: HairSlime")).ReadLook(Catalog);

        Assert.DoesNotContain("slime", States(look));
    }
}
