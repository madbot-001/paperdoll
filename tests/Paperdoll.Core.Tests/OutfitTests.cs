using System.Text;
using Paperdoll.Core.Outfits;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class OutfitTests
{
    private const string Yaml = """
        - type: job
          id: Passenger
          name: job-name-passenger
          startingGear: PassengerGear
        - type: job
          id: Borg
          name: job-name-borg
          setPreference: false
        - type: startingGear
          id: PassengerGear
          equipment:
            id: PassengerPDA
            ears: HeadsetGrey
        - type: roleLoadout
          id: JobPassenger
          groups: [ Jumpsuit, Mask, Hat ]
        - type: loadoutGroup
          id: Jumpsuit
          name: loadout-group-jumpsuit
          loadouts: [ SeniorSuit, GreySuit, BlueSuit ]
        - type: loadoutGroup
          id: Mask
          name: loadout-group-mask
          minLimit: 0
          defaultSelected: 1
          loadouts: [ VoxMask ]
        - type: loadoutGroup
          id: Hat
          name: loadout-group-hat
          minLimit: 0
          loadouts: [ Beret ]
        - type: loadoutEffectGroup
          id: Senior
          effects:
          - !type:JobRequirementLoadoutEffect
            requirement: {}
        - type: loadout
          id: SeniorSuit
          effects:
          - !type:GroupLoadoutEffect
            proto: Senior
          equipment:
            jumpsuit: SuitSenior
        - type: loadout
          id: GreySuit
          equipment:
            jumpsuit: SuitGrey
            id: LoadoutPDA
        - type: loadout
          id: BlueSuit
          equipment:
            jumpsuit: SuitBlue
        - type: loadout
          id: VoxMask
          effects:
          - !type:SpeciesLoadoutEffect
            species: [ Vox ]
          equipment:
            mask: BreathMask
        - type: loadout
          id: Beret
          equipment:
            head: HatBeret
        """;

    private static OutfitCatalog Build() =>
        OutfitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("o.yml", Encoding.UTF8.GetBytes(Yaml))]));

    [Fact]
    public void Defaults_skip_loadouts_that_need_playtime_or_another_species()
    {
        var outfits = Build();

        var human = outfits.LoadoutFor("Passenger", "Human", saved: null);
        var vox = outfits.LoadoutFor("Passenger", "Vox", saved: null);

        Assert.Equal(["GreySuit"], human.Groups.Single(g => g.Group == "Jumpsuit").Loadouts);
        Assert.Empty(human.Groups.Single(g => g.Group == "Mask").Loadouts);
        Assert.Equal(["VoxMask"], vox.Groups.Single(g => g.Group == "Mask").Loadouts);
        Assert.Empty(human.Groups.Single(g => g.Group == "Hat").Loadouts);
    }

    [Fact]
    public void Saved_groups_are_kept_and_the_rest_get_defaults()
    {
        var outfits = Build();
        var saved = new Dictionary<string, IReadOnlyList<string>> { ["Jumpsuit"] = ["BlueSuit"], ["Hat"] = ["Beret"] };

        var loadout = outfits.LoadoutFor("Passenger", "Human", saved);

        Assert.Equal(["BlueSuit"], loadout.Groups.Single(g => g.Group == "Jumpsuit").Loadouts);
        Assert.Equal(["Beret"], loadout.Groups.Single(g => g.Group == "Hat").Loadouts);
    }

    [Fact]
    public void The_jobs_gear_goes_on_first_and_loadouts_only_fill_empty_slots()
    {
        var outfits = Build();

        var outfit = outfits.OutfitFor("Passenger", outfits.LoadoutFor("Passenger", "Human", null));

        Assert.Equal("PassengerPDA", outfit["id"]);
        Assert.Equal("SuitGrey", outfit["jumpsuit"]);
        Assert.Equal("HeadsetGrey", outfit["ears"]);
    }

    [Fact]
    public void Species_and_playtime_checks_are_reported()
    {
        var outfits = Build();

        Assert.Equal(LoadoutCheck.WrongSpecies, outfits.Check(outfits.Loadouts["VoxMask"], "Human"));
        Assert.Equal(LoadoutCheck.ServerChecks, outfits.Check(outfits.Loadouts["SeniorSuit"], "Human"));
        Assert.Equal(LoadoutCheck.Allowed, outfits.Check(outfits.Loadouts["GreySuit"], "Human"));
        Assert.DoesNotContain(outfits.SelectableJobs(), j => j.Id == "Borg");
    }
}

public class ClothingResolverTests
{
    private const string Yaml = """
        - type: entity
          id: ClothingBase
          abstract: true
          components:
          - type: Clothing
        - type: entity
          id: SuitGrey
          parent: ClothingBase
          components:
          - type: Sprite
            sprite: Clothing/suit.rsi
        - type: entity
          id: SuitPrefixed
          parent: ClothingBase
          components:
          - type: Sprite
            sprite: Clothing/suit.rsi
          - type: Clothing
            equippedPrefix: rolled
        - type: entity
          id: Coat
          components:
          - type: Sprite
            sprite: Clothing/coat.rsi
          - type: Clothing
            clothingVisuals:
              outerClothing:
              - state: body
              - state: trim
                color: "#FF0000"
        - type: entity
          id: Helmet
          components:
          - type: Sprite
            sprite: Clothing/helmet.rsi
          - type: Clothing
          - type: HideLayerClothing
            slots: [ Hair, HeadTop ]
        """;

    private static readonly Dictionary<string, RsiMeta> Metas = new()
    {
        ["Clothing/suit.rsi"] = Meta("equipped-INNERCLOTHING", "equipped-INNERCLOTHING-vox", "rolled-equipped-INNERCLOTHING"),
        ["Clothing/coat.rsi"] = Meta("body", "trim"),
        ["Clothing/helmet.rsi"] = Meta("equipped-HELMET"),
    };

    private static RsiMeta Meta(params string[] states) => RsiMeta.Parse(Encoding.UTF8.GetBytes(
        $$"""{"size":{"x":32,"y":32},"states":[{{string.Join(',', states.Select(s => $$"""{"name":"{{s}}"}"""))}}]}"""));

    private static ClothingResolver Build() => new(
        PrototypeIndex.Load([new PrototypeSource("c.yml", Encoding.UTF8.GetBytes(Yaml))]),
        rsi => Metas.GetValueOrDefault(rsi));

    [Fact]
    public void Uses_the_equipped_state_for_the_slot_and_a_species_version_when_there_is_one()
    {
        var resolver = Build();

        var human = resolver.Resolve("SuitGrey", "jumpsuit", "human")!;
        var vox = resolver.Resolve("SuitGrey", "jumpsuit", "vox")!;

        Assert.Equal("equipped-INNERCLOTHING", human.Layers.Single().Sprite.State);
        Assert.False(human.Layers.Single().SpeciesSpecific);
        Assert.Equal("equipped-INNERCLOTHING-vox", vox.Layers.Single().Sprite.State);
        Assert.True(vox.Layers.Single().SpeciesSpecific);
    }

    [Fact]
    public void Applies_the_equipped_prefix()
    {
        Assert.Equal("rolled-equipped-INNERCLOTHING", Build().Resolve("SuitPrefixed", "jumpsuit", null)!.Layers.Single().Sprite.State);
    }

    [Fact]
    public void Clothing_visuals_give_several_coloured_layers()
    {
        var coat = Build().Resolve("Coat", "outerClothing", null)!;

        Assert.Equal(["body", "trim"], coat.Layers.Select(l => l.Sprite.State));
        Assert.Equal(Rgba.Parse("#FF0000"), coat.Layers[1].Color);
    }

    [Fact]
    public void Lists_the_body_layers_an_item_hides()
    {
        Assert.Equal(["Hair", "HeadTop"], Build().Resolve("Helmet", "head", null)!.HiddenLayers.Order());
    }

    [Fact]
    public void An_item_without_a_sprite_for_the_slot_draws_nothing()
    {
        Assert.Empty(Build().Resolve("Helmet", "jumpsuit", null)!.Layers);
    }
}

public class OutfitEditingTests
{
    private const string File = """
        forkId: x
        version: 2
        profile:
          name: Ann Bee
          species: Human
          _jobPriorities:
            Passenger: High
            Chef: Medium
            Wizard: High
          _loadouts:
            JobPassenger:
              selectedLoadouts:
                Jumpsuit:
                - prototype: GreySuit
                  colorOverride: '#123456'
                Hat: []
        """;

    [Fact]
    public void Setting_high_moves_the_old_high_to_medium_and_never_removes()
    {
        var file = Profiles.CharacterFile.Parse(File);

        file.SetJobPriority("Chef", "High");
        file.SetJobPriority("Wizard", "Never");

        Assert.Equal("High", file.JobPriorities["Chef"]);
        Assert.Equal("Medium", file.JobPriorities["Passenger"]);
        Assert.False(file.JobPriorities.ContainsKey("Wizard"));
        Assert.Equal("Chef", file.HighPriorityJob);
    }

    [Fact]
    public void Writing_a_group_keeps_other_groups_and_the_fields_of_kept_entries()
    {
        var file = Profiles.CharacterFile.Parse(File);

        file.SetLoadoutGroup("JobPassenger", "Jumpsuit", ["GreySuit", "BlueSuit"]);
        file.SetLoadoutGroup("JobChef", "Hat", ["ChefHat"]);
        var again = Profiles.CharacterFile.Parse(file.ToYaml());

        Assert.Equal(["GreySuit", "BlueSuit"], again.Loadouts["JobPassenger"]["Jumpsuit"]);
        Assert.Empty(again.Loadouts["JobPassenger"]["Hat"]);
        Assert.Equal(["ChefHat"], again.Loadouts["JobChef"]["Hat"]);
        Assert.Contains("'#123456'", again.ToYaml());
    }

    [Fact]
    public void The_rules_keep_known_jobs_and_one_high()
    {
        var outfits = OutfitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("j.yml", Encoding.UTF8.GetBytes(
            "- type: job\n  id: Passenger\n- type: job\n  id: Chef\n- type: species\n  id: Human\n  name: x\n  roundStart: true\n  dollPrototype: D\n- type: entity\n  id: D\n"))]));
        var catalog = Characters.CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(
            "- type: species\n  id: Human\n  name: x\n  roundStart: true\n  dollPrototype: D\n- type: entity\n  id: D\n"))]));
        var file = Profiles.CharacterFile.Parse(File.Replace("Chef: Medium", "Chef: High"));

        var fixes = Profiles.CharacterRules.EnsureValid(file, catalog,
            new Forks.ForkInfo("t", "T", "o/r", "main", Forks.AppearanceModel.New, false, []), outfits: outfits);

        Assert.Equal("High", file.JobPriorities["Passenger"]);
        Assert.Equal("Medium", file.JobPriorities["Chef"]);
        Assert.False(file.JobPriorities.ContainsKey("Wizard"));
        Assert.Contains(fixes, f => f.Field == "jobs");
    }

    [Fact]
    public void The_rules_keep_only_antagonists_players_can_choose()
    {
        var outfits = OutfitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("a.yml", Encoding.UTF8.GetBytes("""
            - type: antag
              id: Traitor
              name: roles-antag-syndicate-agent-name
              objective: roles-antag-syndicate-agent-objective
              setPreference: true
              requirements:
              - !type:OverallPlaytimeRequirement
                time: 3600
            - type: antag
              id: Thief
              setPreference: true
            - type: antag
              id: Zombie
              setPreference: false
            """))]));
        var catalog = Characters.CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(
            "- type: species\n  id: Human\n  name: x\n  roundStart: true\n  dollPrototype: D\n- type: entity\n  id: D\n"))]));
        var file = Profiles.CharacterFile.Parse(File.Replace("profile:", "profile:\n  _antagPreferences: [ Traitor, Zombie, Wizard, Thief, Traitor ]"));

        var fixes = Profiles.CharacterRules.EnsureValid(file, catalog,
            new Forks.ForkInfo("t", "T", "o/r", "main", Forks.AppearanceModel.New, false, []), outfits: outfits);

        Assert.Equal(["Traitor", "Thief"], file.AntagPreferences);
        Assert.True(outfits.Antags["Traitor"].HasRequirements);
        Assert.Contains(fixes, f => f.Field == "antags" && f.Message.Contains("Zombie") && f.Message.Contains("Wizard"));
    }
}

public class SpawnGearTests
{
    // A job whose gear puts a flash in the backpack; loadouts for the backpack and a survival box.
    private const string Yaml = """
        - type: job
          id: Janitor
          startingGear: JanitorGear
        - type: startingGear
          id: JanitorGear
          equipment:
            id: JanitorPDA
            jumpsuit: JobSuit
          storage:
            back: [ Flash ]
        - type: roleLoadout
          id: JobJanitor
          groups: [ Backpack, Survival, Hat ]
        - type: loadoutGroup
          id: Backpack
          loadouts: [ CommonBackpack ]
        - type: loadoutGroup
          id: Survival
          loadouts: [ EmergencyOxygen ]
        - type: loadoutGroup
          id: Hat
          loadouts: [ JanitorHat ]
        - type: loadout
          id: CommonBackpack
          equipment: { back: ClothingBackpack }
        - type: loadout
          id: EmergencyOxygen
          storage:
            back: [ BoxSurvival ]
        - type: loadout
          id: JanitorHat
          equipment: { head: Cap, jumpsuit: LoadoutSuit }
          inhand: [ Mop, Bucket, Sign ]
          storage:
            head: [ Coin ]
        - type: loadout
          id: Plushie
          inhand: [ PlushieLizard ]
          groupBy: plushies
        - type: loadout
          id: Placard
          dummyEntity: PlacardSign
          equipment: { head: Cap }
        - type: startingGear
          id: TwoItemGear
          equipment: { head: Cap, neck: Scarf }
        - type: loadout
          id: CapOverGear
          startingGear: TwoItemGear
          equipment: { eyes: Glasses }
        - type: entity
          id: ClothingBackpack
          components:
          - type: Storage
        - type: entity
          id: BoxSurvival
          components:
          - type: Storage
          - type: StorageFill
            contents:
            - id: OxygenTank
            - id: Glowstick
              amount: 2
              prob: 0.5
        - type: entity
          id: Cap
        - type: entity
          id: JaniBelt
          components:
          - type: Storage
          - type: EntityTableContainerFill
            containers:
              storagebase: !type:AllSelector
                children:
                - !type:NestedSelector
                  tableId: Soaps
                - id: Spray
                - id: Grenade
                  amount: 2
                  prob: 0.5
        - type: entityTable
          id: Soaps
          table: !type:GroupSelector
            children:
            - id: SoapGreen
            - id: SoapBlue
        """;

    private static readonly OutfitCatalog Outfits =
        OutfitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("g.yml", Encoding.UTF8.GetBytes(Yaml))]));

    [Fact]
    public void Loadouts_go_on_before_the_jobs_gear_and_fill_the_backpack_in_that_order()
    {
        var gear = Outfits.GearAtSpawn("Janitor", Outfits.LoadoutFor("Janitor", "Human", null));

        Assert.Equal("ClothingBackpack", gear.Worn["back"].Entity);
        // The loadout's jumpsuit goes on first; the job's own is left out.
        Assert.Equal(new GearItem("LoadoutSuit", "JanitorHat"), gear.Worn["jumpsuit"]);
        Assert.Equal(new GearItem("JanitorPDA", null), gear.Worn["id"]);
        Assert.Equal(["BoxSurvival", "Flash"], gear.Stored["back"].Select(i => i.Entity));
        // Two hands; the cap cannot hold a coin.
        Assert.Equal(["Mop", "Bucket"], gear.InHand.Select(i => i.Entity));
        Assert.False(gear.Stored.ContainsKey("head"));
        Assert.Equal([new FillItem("OxygenTank", 1, 1f), new FillItem("Glowstick", 2, 0.5f)], Outfits.FillOf("BoxSurvival"));
    }

    [Fact]
    public void Loadouts_are_pictured_and_named_after_their_one_item_as_the_lobby_does()
    {
        string? Picture(string id) => Outfits.PictureOf(Outfits.Loadouts[id]);
        string? Named(string id) => Outfits.NamedAfter(Outfits.Loadouts[id]);

        Assert.Equal("ClothingBackpack", Picture("CommonBackpack"));
        Assert.Equal("BoxSurvival", Picture("EmergencyOxygen"));
        Assert.Equal("PlushieLizard", Named("Plushie"));
        Assert.Equal("PlacardSign", Named("Placard"));
        Assert.Null(Picture("JanitorHat"));
        // Starting gear with two items: the picture falls back to the loadout's own item, the name does not.
        Assert.Equal("Glasses", Picture("CapOverGear"));
        Assert.Null(Named("CapOverGear"));
        Assert.Equal("plushies", Outfits.Loadouts["Plushie"].GroupBy);
        Assert.Null(Outfits.Loadouts["Placard"].GroupBy);
    }

    [Fact]
    public void Entity_table_fills_open_nested_tables_and_mark_groups()
    {
        var fill = Outfits.FillOf("JaniBelt");

        Assert.Equal(["SoapGreen", "SoapBlue", "Spray", "Grenade"], fill.Select(f => f.Entity));
        Assert.True(fill[0].OneOf && fill[1].OneOf);
        Assert.False(fill[2].OneOf);
        Assert.Equal((2, 0.5f), (fill[3].Amount, fill[3].Chance));
    }

    [Fact]
    public void Items_for_a_bag_are_lost_when_the_bag_comes_later()
    {
        var loadout = new RoleLoadout("JobJanitor", [("Survival", ["EmergencyOxygen"]), ("Backpack", ["CommonBackpack"])]);

        var gear = Outfits.GearAtSpawn("Janitor", loadout);

        Assert.Equal(["Flash"], gear.Stored["back"].Select(i => i.Entity));
    }
}

public class LoadoutRulesTests
{
    private const string Yaml = """
        - type: job
          id: Borg
        - type: job
          id: Janitor
        - type: roleLoadout
          id: JobBase
          abstract: true
          groups: [ Trinkets ]
        - type: roleLoadout
          id: JobJanitor
          parent: JobBase
          groups: [ JanitorHat, JanitorSuit ]
        - type: roleLoadout
          id: JobBorg
          canCustomizeName: true
          groups: []
        - type: job
          id: Mime
        - type: roleLoadout
          id: JobMime
          groups: [ BaseHats, JanitorSuit ]
        - type: loadoutGroup
          id: BaseHats
          abstract: true
          loadouts: [ Beret, Cap ]
        - type: loadoutGroup
          id: JanitorHat
          parent: BaseHats
          minLimit: 0
          loadouts: [ PurpleCap, Cap ]
        - type: loadoutGroup
          id: JanitorSuit
          loadouts: [ VeteranSuit, JanitorSuit ]
        - type: loadoutGroup
          id: Trinkets
          minLimit: 0
          maxLimit: 2
          loadouts: [ Lighter, Coin, Ring ]
        - type: loadout
          id: Beret
        - type: loadout
          id: Cap
        - type: loadout
          id: PurpleCap
        - type: loadout
          id: VeteranSuit
          effects:
          - !type:JobRequirementLoadoutEffect {}
        - type: loadout
          id: JanitorSuit
        - type: loadout
          id: Lighter
        - type: loadout
          id: Coin
        - type: loadout
          id: Ring
        - type: species
          id: Human
          name: x
          roundStart: true
          dollPrototype: D
        - type: entity
          id: D
        """;

    private static readonly PrototypeIndex Index = PrototypeIndex.Load([new PrototypeSource("l.yml", Encoding.UTF8.GetBytes(Yaml))]);
    private static readonly OutfitCatalog Outfits = OutfitCatalog.Build(Index);
    private static readonly Characters.CharacterCatalog Catalog = Characters.CharacterCatalog.Build(Index);
    private static readonly Forks.ForkInfo Fork = new("t", "T", "o/r", "main", Forks.AppearanceModel.New, false, []);

    private static Profiles.CharacterFile File(string loadouts) => Profiles.CharacterFile.Parse(
        "version: 2\nprofile:\n  name: Test Person\n  species: Human\n  age: 30\n  sex: Male\n  gender: Male\n"
        + "  appearance:\n    skinColor: '#FFFFFFFF'\n    eyeColor: '#000000FF'\n    markings: {}\n" + loadouts + "forkId: t\n");

    [Fact]
    public void Groups_and_role_loadouts_add_their_parents_after_their_own()
    {
        Assert.Equal(["PurpleCap", "Cap", "Beret"], Outfits.Groups["JanitorHat"].Loadouts);
        Assert.Equal(["JanitorHat", "JanitorSuit", "Trinkets"], Outfits.RoleLoadouts["JobJanitor"]);
        Assert.False(Outfits.Groups.ContainsKey("BaseHats"));
    }

    [Fact]
    public void A_role_listing_an_abstract_group_needs_no_fix()
    {
        const string loadouts = """
              _loadouts:
                JobMime:
                  role: JobMime
                  entityName: null
                  selectedLoadouts:
                    JanitorSuit:
                    - prototype: JanitorSuit

            """;
        var file = File(loadouts);

        var fixes = Profiles.CharacterRules.EnsureValid(file, Catalog, Fork, outfits: Outfits);

        Assert.DoesNotContain(fixes, f => f.Field == "loadouts");
        Assert.Contains(loadouts.TrimEnd(), file.ToYaml());
    }

    [Fact]
    public void Loadouts_are_checked_as_the_game_checks_them()
    {
        var file = File("""
              _loadouts:
                JobJanitor:
                  selectedLoadouts:
                    JanitorHat:
                    - prototype: Beret
                    - prototype: Cap
                    Trinkets:
                    - prototype: Lighter
                    - prototype: Coin
                    - prototype: Ring
                    Pets:
                    - prototype: Cat
                JobBorg:
                  entityName: '  a very long borg name that goes on and on and on  '
                  selectedLoadouts: {}
                  role: JobBorg
                JobWizard:
                  selectedLoadouts: {}

            """);

        var fixes = Profiles.CharacterRules.EnsureValid(file, Catalog, Fork, outfits: Outfits);
        var saved = file.Loadouts["JobJanitor"];

        // Over the limit the last go; a missing required group gets the first loadout that needs no playtime.
        Assert.Equal(["Beret"], saved["JanitorHat"]);
        Assert.Equal(["Lighter", "Coin"], saved["Trinkets"]);
        Assert.Equal(["JanitorSuit"], saved["JanitorSuit"]);
        Assert.False(saved.ContainsKey("Pets"));
        Assert.False(file.Loadouts.ContainsKey("JobWizard"));
        Assert.Contains("role: JobJanitor", file.ToYaml());
        // As the game does it: trimmed, then cut, which can leave a space at the end.
        Assert.Equal("a very long borg name that goes ", file.RoleName("JobBorg"));
        Assert.Contains(fixes, f => f.Field == "loadouts");
    }

    [Fact]
    public void A_new_jobs_loadout_is_written_as_the_game_writes_it()
    {
        var file = File("");
        file.SetLoadoutGroup("JobJanitor", "JanitorHat", ["Cap"]);
        Profiles.CharacterRules.EnsureValid(file, Catalog, Fork, outfits: Outfits);

        Assert.Contains("""
              _loadouts:
                JobJanitor:
                  entityName: null
                  selectedLoadouts:
                    JanitorHat:
                    - prototype: Cap
                    JanitorSuit:
                    - prototype: JanitorSuit
                    Trinkets: []
                  role: JobJanitor
            """.ReplaceLineEndings("\n"), file.ToYaml());

        var again = Profiles.CharacterFile.Parse(file.ToYaml());
        Assert.Empty(Profiles.CharacterRules.EnsureValid(again, Catalog, Fork, outfits: Outfits));
        Assert.Equal(file.ToYaml(), again.ToYaml());
    }

    [Fact]
    public void Euphoria_saves_each_chosen_item_with_its_customisation_fields()
    {
        var euphoriaLike = Fork with { Extras = Forks.ProfileExtras.ItemCustomization };
        var file = File("");
        file.SetLoadoutGroup("JobJanitor", "JanitorHat", ["Cap"]);
        Profiles.CharacterRules.EnsureValid(file, Catalog, euphoriaLike, outfits: Outfits);

        Assert.Contains("""
                    JanitorHat:
                    - colorOverride: null
                      descriptionOverride: null
                      nameOverride: null
                      prototype: Cap
            """.ReplaceLineEndings("\n"), file.ToYaml());

        file.SetLoadoutCustomization("JobJanitor", "JanitorHat", new Profiles.LoadoutEntry("Cap", "Lucky cap", null, "#336699FF"));
        var again = Profiles.CharacterFile.Parse(file.ToYaml());
        Assert.Equal(new Profiles.LoadoutEntry("Cap", "Lucky cap", null, "#336699FF"), Assert.Single(again.LoadoutEntries("JobJanitor", "JanitorHat")));
        // Forks without it keep plain entries.
        var plain = File("");
        plain.SetLoadoutGroup("JobJanitor", "JanitorHat", ["Cap"]);
        Profiles.CharacterRules.EnsureValid(plain, Catalog, Fork, outfits: Outfits);
        Assert.DoesNotContain("colorOverride", plain.ToYaml());
    }

    [Fact]
    public void Loadout_colours_are_solid_and_not_too_dark()
    {
        Assert.Equal(Rendering.Rgba.Parse("#FFC0CB"), Editing.EditorSession.CustomizationColor(Rendering.Rgba.Parse("#11223380")));
        var dark = Editing.EditorSession.CustomizationColor(Rendering.Rgba.Parse("#050505"));
        Assert.Equal(0.25f, dark.ToHsl().L, 0.01f);
        Assert.Equal(Rendering.Rgba.Parse("#3366AA"), Editing.EditorSession.CustomizationColor(Rendering.Rgba.Parse("#3366AA")));
    }
}
