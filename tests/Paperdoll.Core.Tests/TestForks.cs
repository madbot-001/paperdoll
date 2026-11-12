using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Five small made-up forks held in memory: "a" has lizards and scars, "b" has neither, "c" has a
/// job, an antagonist, a trait, a species that takes no markings, a species with a one-slider skin
/// tone, one whose skin is held to a range, colour names and both profile extras that add tabs.
/// "d" is on the old appearance model, with hair, two silicon voices and one other, and the
/// character info extras. "e" is "d" with Starlight's records.
/// </summary>
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

    private const string JobAntagTrait = """
        - type: job
          id: Passenger
          name: job-name-passenger
          startingGear: PassengerGear
        - type: startingGear
          id: PassengerGear
          equipment:
            id: PassengerPDA
        - type: antag
          id: Thief
          name: roles-antag-thief-name
          setPreference: true
        - type: traitCategory
          id: Speech
          name: trait-category-speech
        - type: trait
          id: Stutter
          name: trait-stutter
          category: Speech
          cost: 1
        """;

    private const string Blob = """
        - type: species
          id: Blob
          name: species-name-blob
          roundStart: true
          dollPrototype: BlobDoll
        - type: entity
          id: BlobDoll
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
          - type: InitialBody
            organs:
              Torso: OrganBlob
        - type: entity
          id: OrganBlob
          components:
          - type: Organ
            category: Torso
        """;

    // Skin set by one slider, as upstream's humans have it.
    private const string Toned = """
        - type: species
          id: Toned
          name: species-name-toned
          roundStart: true
          dollPrototype: Doll
          skinColoration: Toned
        - type: skinColoration
          id: Toned
          strategy: !type:HumanTonedSkinColoration {}
        - type: species
          id: Dim
          name: species-name-dim
          roundStart: true
          dollPrototype: Doll
          skinColoration: HalfBright
        - type: skinColoration
          id: HalfBright
          strategy: !type:ClampedHsvColoration
            value: [0.5, 1]
        """;

    // Made-up words for the colour names the engine's strings give.
    private const string ColorNames = """
        color-hue-chroma-lightness = {$lightness} {$chroma} {$hue}
        color-hue-chroma = {$chroma} {$hue}
        color-hue-lightness = {$lightness} {$hue}
        color-very-dark = deep
        color-dark = dim
        color-light = bright
        color-very-light = glowing
        color-mixed-hue = {$a}-{$b}
        color-pale = washed
        color-gray-adjective = dull
        color-strong = vivid
        color-pink = rose
        color-red = scarlet
        color-orange = amber
        color-yellow = lemon
        color-green = leaf
        color-cyan = teal
        color-blue = sky
        color-purple = violet
        color-brown = umber
        color-white = snow
        color-gray = ash
        color-black = coal
        """;

    // An old-model human with hair and facial hair, and text-to-speech voices.
    private const string OldBody = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: OldDoll
          sprites: OldSprites
          markingLimits: OldLimits
        - type: entity
          id: OldDoll
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
            - map: [ "enum.HumanoidVisualLayers.Eyes" ]
            - map: [ "enum.HumanoidVisualLayers.Hair" ]
            - map: [ "enum.HumanoidVisualLayers.FacialHair" ]
        - type: speciesBaseSprites
          id: OldSprites
          sprites:
            Chest: OldTorso
            Eyes: OldEyes
            Hair: OldAnyMarking
            FacialHair: OldAnyMarking
        - type: humanoidBaseSprite
          id: OldTorso
          baseSprite: { sprite: Mobs/old.rsi, state: torso }
        - type: humanoidBaseSprite
          id: OldEyes
          baseSprite: { sprite: Mobs/old.rsi, state: eyes }
          matchSkin: false
        - type: humanoidBaseSprite
          id: OldAnyMarking
        - type: markingPoints
          id: OldLimits
          points:
            Hair: { points: 1, required: false }
            FacialHair: { points: 1, required: false }
        - type: marking
          id: HairShort
          bodyPart: Hair
          markingCategory: Hair
          sprites: [ { sprite: Mobs/old.rsi, state: hair } ]
        - type: marking
          id: Beard
          bodyPart: FacialHair
          markingCategory: FacialHair
          sprites: [ { sprite: Mobs/old.rsi, state: beard } ]
        - type: voice
          id: Hum
          voice: 1
          name: tts-voice-hum
          sex: Unsexed
          silicon: true
        - type: voice
          id: Chirp
          voice: 2
          name: tts-voice-chirp
          sex: Unsexed
          silicon: true
        - type: voice
          id: Alto
          voice: 3
          name: tts-voice-alto
          sex: Female
        """;

    public static readonly ForkInfo A = new("a", "Fork A", "o/a", "main", AppearanceModel.New, false, ["a"]);
    public static readonly ForkInfo B = new("b", "Fork B", "o/b", "main", AppearanceModel.New, false, ["b"]);

    public static readonly ForkInfo C = A with { Id = "c", Name = "Fork C", Extras = ProfileExtras.Records | ProfileExtras.Allergies, ServerForkIds = ["c"] };

    public static readonly ForkInfo D = new("d", "Fork D", "o/d", "main", AppearanceModel.Old, true, ["d"])
    {
        Extras = ProfileExtras.IcInfo | ProfileExtras.OocInfo | ProfileExtras.SiliconVoice | ProfileExtras.Glowing,
    };

    public static readonly ForkInfo E = D with
    {
        Id = "e", Name = "Fork E", ServerForkIds = ["e"],
        Extras = D.Extras | ProfileExtras.Records,
        RecordFormat = Profiles.RecordFormat.Starlight,
    };

    public static EditorSession Session() => new(Store());

    public static MemoryForkStore Store() => new MemoryForkStore()
        .Add("a", "Resources/Prototypes/body.yml", Body)
        .Add("a", "Resources/Prototypes/extra.yml", LizardsAndScars)
        .Add("a", "Resources/Textures/Mobs/m.rsi/meta.json", """{"version":1,"size":{"x":32,"y":32},"states":[{"name":"scar"}]}""")
        .Add("b", "Resources/Prototypes/body.yml", Body)
        .Add("c", "Resources/Prototypes/body.yml", Body)
        .Add("c", "Resources/Prototypes/roles.yml", JobAntagTrait)
        .Add("c", "Resources/Prototypes/blob.yml", Blob)
        .Add("c", "Resources/Prototypes/toned.yml", Toned)
        .Add("c", "Resources/Locale/en-US/color.ftl", ColorNames)
        .Add("d", "Resources/Prototypes/body.yml", OldBody)
        .Add("d", "Resources/Locale/en-US/voices.ftl", "tts-voice-hum = Low Hum\ntts-voice-chirp = Bright Chirp\n")
        .Add("e", "Resources/Prototypes/body.yml", OldBody)
        .Add("e", "Resources/Locale/en-US/voices.ftl", "tts-voice-hum = Low Hum\ntts-voice-chirp = Bright Chirp\n");
}
