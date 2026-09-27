using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Tests;

/// <summary>Sessions over two small made-up forks held in memory: "a" has lizards and scars, "b" has neither.</summary>
public class SessionTests
{
    private const string Body = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: Doll
        - type: entity
          id: Doll
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
          - type: InitialBody
            organs:
              Torso: OrganTorso
        - type: entity
          id: OrganTorso
          components:
          - type: Organ
            category: Torso
          - type: VisualOrganMarkings
            markingData:
              layers: [ Chest ]
              group: Body
        - type: markingsGroup
          id: Body
          limits:
            enum.HumanoidVisualLayers.Chest: { limit: 2 }
        """;

    private const string LizardsAndScars = """
        - type: species
          id: Lizard
          name: species-name-lizard
          roundStart: true
          dollPrototype: Doll
        - type: marking
          id: Scar
          bodyPart: Chest
          sprites: [ { sprite: Mobs/m.rsi, state: scar } ]
        """;

    public static readonly ForkInfo A = new("a", "Fork A", "o/a", "main", AppearanceModel.New, false, ["a"]);
    public static readonly ForkInfo B = new("b", "Fork B", "o/b", "main", AppearanceModel.New, false, ["b"]);

    public static EditorSession Session() => new(new MemoryForkStore()
        .Add("a", "Resources/Prototypes/body.yml", Body)
        .Add("a", "Resources/Prototypes/extra.yml", LizardsAndScars)
        .Add("b", "Resources/Prototypes/body.yml", Body));

    // A lizard with a scar, made in fork A.
    private static async Task<EditorSession> LizardInA()
    {
        var session = Session();
        await session.LoadForkAsync(A, update: false, ct: TestContext.Current.CancellationToken);
        session.Edit(f => f.Name = "Kept Person");
        session.ChangeSpecies("Lizard");
        session.AddMarking("Torso", "Chest", "Scar");
        return session;
    }

    [Fact]
    public async Task Switching_forks_and_back_restores_what_the_other_fork_lacked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = await LizardInA();

        await session.LoadForkAsync(B, update: false, ct: ct);
        Assert.Equal("Human", session.Look!.Species);
        Assert.True(session.ChangedBySwitch);
        Assert.Contains("Lizard", session.WorkingCopy());

        await session.LoadForkAsync(A, update: false, ct: ct);
        Assert.Equal("Lizard", session.Look!.Species);
        Assert.Equal("Scar", Assert.Single(session.Look.Markings["Torso"]["Chest"]).Id);
        Assert.False(session.ChangedBySwitch);
    }

    [Fact]
    public async Task An_edit_after_a_switch_keeps_what_the_switch_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = await LizardInA();

        await session.LoadForkAsync(B, update: false, ct: ct);
        session.Edit(f => f.Name = "Renamed Person");
        await session.LoadForkAsync(A, update: false, ct: ct);

        Assert.Equal("Renamed Person", session.File!.Name);
        Assert.Equal("Human", session.Look!.Species);
    }

    [Fact]
    public async Task A_file_that_fails_to_open_leaves_the_open_character_alone()
    {
        await using var session = await LizardInA();
        var bad = CharacterFile.Parse(session.Export());
        bad.Name = "Other Person";
        // A key the game's files never have: a list where a job id belongs.
        bad.Profile.Children[new YamlScalarNode("_jobPriorities")] = new YamlMappingNode { { new YamlSequenceNode(new YamlScalarNode("a")), new YamlScalarNode("High") } };

        Assert.ThrowsAny<Exception>(() => session.Open(bad.ToYaml()));

        Assert.Equal("Kept Person", session.File!.Name);
        Assert.Equal("Lizard", session.Look!.Species);
    }
}
