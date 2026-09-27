using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Tests;

/// <summary>Two small made-up forks held in memory: "a" has lizards and scars, "b" has neither.</summary>
public static class TestForks
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

    public static EditorSession Session() => new(Store());

    public static MemoryForkStore Store() => new MemoryForkStore()
        .Add("a", "Resources/Prototypes/body.yml", Body)
        .Add("a", "Resources/Prototypes/extra.yml", LizardsAndScars)
        .Add("a", "Resources/Textures/Mobs/m.rsi/meta.json", """{"version":1,"size":{"x":32,"y":32},"states":[{"name":"scar"}]}""")
        .Add("b", "Resources/Prototypes/body.yml", Body);
}
