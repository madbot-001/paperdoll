using System.Text;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Traits;

namespace Paperdoll.Core.Tests;

public class TraitTests
{
    private const string Yaml = """
        - type: traitCategory
          id: Speech
          name: trait-category-speech
          maxTraitPoints: 2
        - type: traitCategory
          id: Accents
          name: trait-category-accents
          priority: 30
          maxTraits: 1
          maxPoints: 5
        - type: traitCategory
          id: Mental
          name: trait-category-mental
        - type: trait
          id: Stutter
          name: trait-stutter
          category: Speech
          cost: 1
        - type: trait
          id: Lisp
          name: trait-lisp
          category: Speech
          cost: 2
        - type: trait
          id: Uncategorised
          name: trait-free
        - type: trait
          id: Scottish
          name: trait-scottish
          category: Accents
          cost: 1
        - type: trait
          id: French
          name: trait-french
          category: Accents
          cost: 1
        - type: trait
          id: Mute
          name: trait-mute
          category: Accents
          usesSlots: false
          conflicts: [ Stutter ]
        - type: trait
          id: LightSensitive
          name: trait-light
          category: Mental
          conditions:
          - !type:IsSpeciesCondition
            species: Shadekin
        - type: trait
          id: NotKitsune
          name: trait-notkitsune
          category: Mental
          conditions:
          - !type:OneOfSpeciesCondition
            invert: true
            species: [ Kitsune ]
        - type: trait
          id: Upgrade
          name: trait-upgrade
          category: Mental
          conditions:
          - !type:TraitDependencyCondition
            requires: [ Stutter ]
        - type: trait
          id: Chef
          name: trait-chef
          category: Mental
          conditions:
          - !type:AnyOfCondition
            conditions:
            - !type:HasJobCondition
              job: Chef
            - !type:InDepartmentCondition
              department: Service
        - type: trait
          id: Bodily
          name: trait-bodily
          category: Mental
          conditions:
          - !type:HasCompCondition
            component: Hands
        """;

    private static TraitCatalog Build() => TraitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("t.yml", Encoding.UTF8.GetBytes(Yaml))]));

    private static TraitContext Context(string species = "Human", string? job = null, string? department = null, params string[] selected) =>
        new(species, job, department, selected);

    [Fact]
    public void Negative_costs_read_the_same_in_every_language_setting()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Arabic and similar settings write the minus sign differently, so "-1" fails to parse in them.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var traits = TraitCatalog.Build(PrototypeIndex.Load([new PrototypeSource("n.yml", Encoding.UTF8.GetBytes("""
                - type: trait
                  id: Hardy
                  name: trait-hardy
                  cost: -1
                """))]));

            Assert.Equal(-1, traits.Traits["Hardy"].Cost);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Old_einstein_engines_ids_are_repaired()
    {
        Assert.Equal("AnimalFriend", TraitCatalog.NormalizeId("{Prototype: AnimalFriend}"));
        Assert.Equal("Stutter", TraitCatalog.NormalizeId("Stutter"));
    }

    [Fact]
    public void The_game_keeps_traits_in_order_while_category_points_last()
    {
        var traits = Build();

        Assert.Equal(["Stutter", "Uncategorised"], traits.Valid(["Stutter", "Lisp", "Uncategorised", "Unknown"], TraitRules.Upstream));
    }

    [Fact]
    public void Delta_v_style_drops_traits_without_a_category()
    {
        Assert.Equal(["Stutter"], Build().Valid(["Stutter", "Uncategorised"], TraitRules.DeltaV));
    }

    [Fact]
    public void Species_conditions_block_or_allow()
    {
        var traits = Build();

        Assert.Equal(TraitAvailability.Blocked, traits.Evaluate(traits.Traits["LightSensitive"], Context("Human")).Availability);
        Assert.Equal(TraitAvailability.Available, traits.Evaluate(traits.Traits["LightSensitive"], Context("Shadekin")).Availability);
        Assert.Equal(TraitAvailability.Blocked, traits.Evaluate(traits.Traits["NotKitsune"], Context("Kitsune")).Availability);
        Assert.Equal(TraitAvailability.Available, traits.Evaluate(traits.Traits["NotKitsune"], Context("Human")).Availability);
    }

    [Fact]
    public void Dependencies_any_of_and_server_only_conditions()
    {
        var traits = Build();

        Assert.Equal(TraitAvailability.Blocked, traits.Evaluate(traits.Traits["Upgrade"], Context()).Availability);
        Assert.Equal(TraitAvailability.Available, traits.Evaluate(traits.Traits["Upgrade"], Context(selected: "Stutter")).Availability);
        Assert.Equal(TraitAvailability.Available, traits.Evaluate(traits.Traits["Chef"], Context(job: "Janitor", department: "Service")).Availability);
        Assert.Equal(TraitAvailability.Blocked, traits.Evaluate(traits.Traits["Chef"], Context(job: "Captain", department: "Command")).Availability);
        Assert.Equal(TraitAvailability.Depends, traits.Evaluate(traits.Traits["Bodily"], Context()).Availability);
    }

    [Fact]
    public void Limits_conflicts_and_slots()
    {
        var traits = Build();

        Assert.Contains("conflicts", traits.WhyNot(traits.Traits["Mute"], Context(selected: "Stutter"), TraitRules.DeltaV));
        Assert.Contains("at most 1", traits.WhyNot(traits.Traits["French"], Context(selected: "Scottish"), TraitRules.DeltaV));
        // Mute uses no slot, so the category's one-trait limit does not stop it.
        Assert.Null(traits.WhyNot(traits.Traits["Mute"], Context(selected: "Scottish"), TraitRules.DeltaV));
        Assert.Contains("points", traits.WhyNot(traits.Traits["Lisp"], Context(selected: "Stutter"), TraitRules.Upstream));
        Assert.Contains("in all", traits.WhyNot(traits.Traits["Stutter"], Context(selected: "Scottish"), new TraitRules(TraitStyle.DeltaV, 1, null)));
    }

    [Fact]
    public void The_rules_repair_and_prune_a_files_traits()
    {
        var file = Profiles.CharacterFile.Parse("""
            forkId: x
            version: 2
            profile:
              name: Ann Bee
              species: Human
              _traitPreferences:
              - '{Prototype: Stutter}'
              - Lisp
              - AnimalFriend
            """);
        var catalog = Characters.CharacterCatalog.Build(PrototypeIndex.Load([new PrototypeSource("s.yml", Encoding.UTF8.GetBytes(
            "- type: species\n  id: Human\n  name: x\n  roundStart: true\n  dollPrototype: D\n- type: entity\n  id: D\n"))]));

        var fixes = Profiles.CharacterRules.EnsureValid(file, catalog,
            new Forks.ForkInfo("t", "T", "o/r", "main", Forks.AppearanceModel.New, false, []), traits: Build());

        Assert.Equal(["Stutter"], file.TraitPreferences);
        Assert.Equal(2, fixes.Count(f => f.Field == "traits"));
    }
}
