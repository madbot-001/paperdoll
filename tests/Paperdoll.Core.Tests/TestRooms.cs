using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Made-up fork "r", with the things a demo room draws: tiles (one throwing edge sprites, one with
/// no picture), a wall that smooths, a crate of several layers, a railing drawn over mobs, a sofa
/// of four directions, a chair that keeps upright, a lamp that snaps to quarter turns, two decals,
/// things that the game's visualizers change at round start (a door, a vending machine, a jukebox,
/// lamps), a giant crate, lights and what blocks them, and jobs shown as a red, animated doll and
/// as a statue that moves only facing diagonally. Every picture of a room is listed but only held
/// once fetched. Apart from the fork, a doll that wears vests with unshaded layers (<see cref="Wearing"/>).
/// </summary>
public static class TestRooms
{
    public static readonly ForkInfo Fork = new("r", "Fork R", "o/r", "main", AppearanceModel.New, false, ["r"]);

    public static readonly SKColor Red = new(200, 30, 30), Green = new(30, 200, 30), Blue = new(30, 30, 200), Yellow = new(220, 220, 30), Purple = new(120, 30, 120);
    public static readonly SKColor White = new(255, 255, 255), Grey = new(120, 120, 120);

    /// <summary>A room using everything: a crate west of where the character stands, the railing south of it, a decal east.</summary>
    public const string Room = """
        id: bar
        name: Test bar
        jobs: [ Clerk ]
        stand: "1,1"
        legend:
          f: FloorTest
          u: FloorUnknown
        tiles:
        - "fff"
        - "fff"
        - "ffu"
        entities:
        - { proto: TestCrate, pos: "0.5,1.5" }
        - { proto: TestRailing, pos: "1.5,0.5" }
        - { proto: NotInThisFork, pos: "2.5,2.5" }
        decals:
        - { id: TestPaint, pos: "2,1", color: "#FF8000" }
        - { id: NotADecal, pos: "0,0" }
        """;

    /// <summary>
    /// A room lit by a lamp that draws nothing, west of where the character stands, with the crate,
    /// whose glow layer is unshaded, two squares east of it.
    /// </summary>
    public const string LitRoom = """
        id: bar
        name: Lit bar
        jobs: [ Clerk ]
        stand: "1,1"
        legend:
          f: FloorTest
        tiles:
        - "ffff"
        - "ffff"
        - "ffff"
        entities:
        - { proto: TestTorch, pos: "0.5,1.5" }
        - { proto: TestCrate, pos: "3.5,1.5" }
        """;

    public const string Prototypes = """
        - type: department
          id: Service
          roles: [ Clerk, Porter ]
        - type: department
          id: Command
          weight: 100
          roles: [ Boss, Porter ]
        - type: job
          id: Clerk
          name: job-name-clerk
          jobPreviewEntity: TestDoll
        - type: job
          id: Porter
          name: job-name-porter
        - type: job
          id: Boss
          name: job-name-boss
        - type: job
          id: Sculptor
          name: job-name-sculptor
          jobPreviewEntity: TestStatue
        - type: tile
          id: FloorTest
          sprite: /Textures/Tiles/test.png
        - type: tile
          id: FloorGrass
          sprite: /Textures/Tiles/grass.png
          edgeSpritePriority: 1
          edgeSprites:
            North: /Textures/Tiles/grass_edge.png
            East: /Textures/Tiles/grass_edge.png
            SouthWest: /Textures/Tiles/grass_corner.png
        - type: tile
          id: FloorUnknown
          sprite: /Textures/Tiles/missing.png
        - type: entity
          id: TestDoll
          components:
          - type: Sprite
            sprite: Mobs/doll.rsi
            state: doll
        - type: entity
          id: TestAnchored
          abstract: true
          components:
          - type: Transform
            anchored: true
        - type: entity
          id: TestWall
          parent: TestAnchored
          components:
          - type: Sprite
            sprite: Structures/wall.rsi
            drawdepth: Walls
          - type: IconSmooth
            key: walls
            base: wall
        - type: entity
          id: TestCrate
          parent: TestAnchored
          components:
          - type: Sprite
            sprite: Structures/crate.rsi
            layers:
            - state: crate
            - state: glow
              shader: unshaded
            - state: hidden
              visible: false
            - texture: Structures/sticker.png
              offset: "0.25,0.25"
              scale: "0.5,0.5"
              rotation: 90
              color: "#00FF00"
            - state: nowhere
        - type: entity
          id: TestRailing
          components:
          - type: Sprite
            sprite: Structures/railing.rsi
            state: railing
            drawdepth: Mobs
            offset: "0,0.5"
        - type: entity
          id: TestChair
          components:
          - type: Sprite
            sprite: Structures/chair.rsi
            state: chair
            noRot: true
        - type: entity
          id: TestSofa
          components:
          - type: Sprite
            sprite: Structures/chair.rsi
            state: chair
        - type: entity
          id: TestLamp
          components:
          - type: Sprite
            sprite: Structures/railing.rsi
            state: railing
            snapCardinals: true
        - type: entity
          id: TestHidden
          components:
          - type: Sprite
            sprite: Structures/crate.rsi
            state: crate
            visible: false
        - type: entity
          id: TestGiant
          components:
          - type: Sprite
            sprite: Structures/crate.rsi
            state: crate
            scale: "1000,1000"
        - type: entity
          id: TestStatue
          components:
          - type: Sprite
            sprite: Mobs/statue.rsi
            state: statue
        - type: entity
          id: TestDoor
          components:
          - type: Door
          - type: Airlock
            openUnlitVisible: true
          - type: Weldable
          - type: WiresVisuals
          - type: Sprite
            sprite: Structures/door.rsi
            layers:
            - state: opening
              map: [ "enum.DoorVisualLayers.Base" ]
            - state: deny_unlit
              visible: false
              map: [ "enum.DoorVisualLayers.BaseUnlit" ]
            - state: welded
              map: [ "enum.WeldableLayers.BaseWelded" ]
            - state: bolted_unlit
              map: [ "enum.DoorVisualLayers.BaseBolted" ]
            - state: emergency_unlit
              map: [ "enum.DoorVisualLayers.BaseEmergencyAccess" ]
            - state: panel_open
              map: [ "enum.WiresVisualLayers.MaintenancePanel" ]
            - state: sparks
              map: [ "enum.DoorVisualLayers.BaseEmagging" ]
        - type: entity
          id: TestVend
          components:
          - type: WiresVisuals
          - type: VendingMachineVisuals
            offState: off
            normalState: normal-unshaded
          - type: Sprite
            sprite: Structures/vend.rsi
            layers:
            - state: broken
              map: [ "enum.VendingMachineVisualLayers.Base" ]
            - state: off
              map: [ "enum.VendingMachineVisualLayers.BaseUnshaded" ]
            - state: deny
              visible: false
              map: [ "enum.VendingMachineVisualLayers.Screen" ]
            - state: panel
              map: [ "enum.WiresVisualLayers.MaintenancePanel" ]
        - type: entity
          id: TestJukebox
          components:
          - type: Jukebox
            onState: on
            offState: off
          - type: Sprite
            sprite: Structures/jukebox.rsi
            layers:
            - state: off
              map: [ "enum.JukeboxVisualLayers.Base" ]
        - type: entity
          id: TestLocker
          components:
          - type: EntityStorageVisuals
            stateBaseClosed: sec
            stateDoorClosed: sec_door
          - type: Sprite
            sprite: Structures/crate.rsi
            layers:
            - state: generic
              map: [ "enum.StorageVisualLayers.Base" ]
            - state: generic_door
              map: [ "enum.StorageVisualLayers.Door" ]
        - type: entity
          id: TestLockerNoDoor
          components:
          - type: EntityStorageVisuals
          - type: Sprite
            sprite: Structures/crate.rsi
            layers:
            - state: generic
              map: [ "enum.StorageVisualLayers.Base" ]
            - state: generic_door
              map: [ "enum.StorageVisualLayers.Door" ]
        - type: entity
          id: TestLamp2
          components:
          - type: PointLight
            color: "#0000FF"
            radius: 3
            energy: 0.5
          - type: PoweredLight
            hasLampOnSpawn: TestBulb
          - type: PoweredLightVisuals
            spriteStateMap:
              On: lit
          - type: Sprite
            sprite: Structures/lamp.rsi
            layers:
            - state: empty
              map: [ "enum.PoweredLightLayers.Base" ]
            - state: glow
              visible: false
              map: [ "enum.PoweredLightLayers.Glow" ]
        - type: entity
          id: TestLampEmpty
          parent: TestLamp2
          components:
          - type: PoweredLight
            hasLampOnSpawn: null
        - type: entity
          id: TestLampOff
          parent: TestLamp2
          components:
          - type: PoweredLight
            on: false
        - type: entity
          id: TestLampBroken
          parent: TestLamp2
          components:
          - type: PoweredLight
            hasLampOnSpawn: TestBulbBroken
        - type: entity
          id: TestBulb
          components:
          - type: LightBulb
            color: "#FF8000"
            lightRadius: 6
            lightEnergy: 2
        - type: entity
          id: TestBulbBroken
          parent: TestBulb
          components:
          - type: LightBulb
            startingState: Broken
        - type: entity
          id: TestTorch
          components:
          - type: PointLight
            radius: 5
        - type: entity
          id: TestTorchOff
          parent: TestTorch
          components:
          - type: PointLight
            enabled: false
        - type: entity
          id: TestFloodLight
          parent: TestTorch
          components:
          - type: PointLight
            radius: 40
            castShadows: false
        - type: entity
          id: TestFloodCaster
          parent: TestTorch
          components:
          - type: PointLight
            radius: 40
        - type: entity
          id: TestFlatLight
          parent: TestTorch
          components:
          - type: PointLight
            castShadows: false
        - type: entity
          id: TestEmergencyLight
          parent: TestTorch
          components:
          - type: EmergencyLight
        - type: entity
          id: TestConeLight
          parent: TestTorch
          components:
          - type: PointLight
            lightMask: TestCone
            autoRot: true
        - type: lightMask
          id: TestCone
          lightCones:
          - direction: 0
            innerWidth: 30
            outerWidth: 60
        - type: entity
          id: TestBlock
          components:
          - type: Occluder
        - type: entity
          id: TestOpenDoor
          parent: TestDoor
          components:
          - type: Door
            state: Open
            openSpriteState: ajar
            openDrawDepth: Overdoors
          - type: Occluder
        - type: entity
          id: TestClosedDoor
          parent: TestDoor
          components:
          - type: Occluder
        - type: entity
          id: TestPanelOnly
          components:
          - type: Sprite
            sprite: Structures/crate.rsi
            layers:
            - state: panel
              map: [ "enum.WiresVisualLayers.MaintenancePanel" ]
        - type: decal
          id: TestMark
          sprite:
            sprite: Decals/marks.rsi
            state: mark
        - type: decal
          id: TestPaint
          sprite: /Textures/Decals/paint.png
        """;

    // A doll in grey with a slot for a vest; vests in green with a yellow strip across, drawn
    // unshaded or not, and one with a blue patch drawn shaded over the strip's west end.
    private const string Dolls = """
        - type: species
          id: Human
          name: species-name-human
          roundStart: true
          dollPrototype: LitDoll
        - type: entity
          id: LitDoll
          components:
          - type: Sprite
            layers:
            - map: [ "enum.HumanoidVisualLayers.Chest" ]
            - map: [ "outerClothing" ]
          - type: InitialBody
            organs:
              Torso: LitTorso
        - type: entity
          id: LitTorso
          components:
          - type: Organ
            category: Torso
          - type: VisualOrgan
            layer: enum.HumanoidVisualLayers.Chest
            data:
              sprite: Mobs/body.rsi
              state: body
        - type: entity
          id: GlowVest
          components:
          - type: Clothing
            sprite: Clothing/vest.rsi
            clothingVisuals:
              outerClothing:
              - state: vest
              - state: strip
                shader: unshaded
        - type: entity
          id: PlainVest
          components:
          - type: Clothing
            sprite: Clothing/vest.rsi
            clothingVisuals:
              outerClothing:
              - state: vest
              - state: strip
        - type: entity
          id: PatchedVest
          components:
          - type: Clothing
            sprite: Clothing/vest.rsi
            clothingVisuals:
              outerClothing:
              - state: vest
              - state: strip
                shader: unshaded
              - state: patch
        """;

    public static readonly SKColor Vest = new(30, 200, 30), VestStrip = new(220, 220, 30), VestPatch = new(30, 30, 200);

    /// <summary>The made-up doll wearing a vest (<c>GlowVest</c>, <c>PlainVest</c> or <c>PatchedVest</c>), facing south.</summary>
    public static SKBitmap Wearing(string vest)
    {
        var index = PrototypeIndex.Load([new PrototypeSource("dolls.yml", Encoding.UTF8.GetBytes(Dolls))]);
        var textures = new MemoryTextures(new Dictionary<string, byte[]>
        {
            ["Mobs/body.rsi/meta.json"] = Encoding.UTF8.GetBytes(Meta("Body made up", ("body", 1))),
            ["Mobs/body.rsi/body.png"] = Solid(32, 32, new SKColor(200, 200, 200)),
            ["Clothing/vest.rsi/meta.json"] = Encoding.UTF8.GetBytes(Meta("Vest made up", ("vest", 1), ("strip", 1), ("patch", 1))),
            ["Clothing/vest.rsi/vest.png"] = Png(32, 32, (x, y) => x is >= 8 and < 24 && y is >= 8 and < 24 ? Vest : SKColors.Transparent),
            ["Clothing/vest.rsi/strip.png"] = Png(32, 32, (x, y) => x is >= 8 and < 24 && y is 12 or 13 ? VestStrip : SKColors.Transparent),
            ["Clothing/vest.rsi/patch.png"] = Png(32, 32, (x, y) => x is >= 8 and < 12 && y is 12 or 13 ? VestPatch : SKColors.Transparent),
        });
        var renderer = new PaperdollRenderer(CharacterCatalog.Build(index), index, textures);
        return renderer.Render(new CharacterLook { Species = "Human" }, outfit: new Dictionary<string, string> { ["outerClothing"] = vest });
    }

    /// <summary>An RSI's meta.json: each state with its directions and, when given, one direction's frame delays.</summary>
    public static string Meta(string copyright, params (string Name, int Directions)[] states) =>
        $$"""{"version":1,"license":"CC-BY-SA-3.0","copyright":"{{copyright}}","size":{"x":32,"y":32},"states":[{{string.Join(",", states.Select(s => $"{{\"name\":\"{s.Name}\",\"directions\":{s.Directions}}}"))}}]}""";

    /// <summary>A PNG of the given size, each pixel from the function.</summary>
    public static byte[] Png(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, pixel(x, y));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A PNG of solid 32-pixel frames side by side, one colour each.</summary>
    public static byte[] Strip(params SKColor[] frames) =>
        Png(32 * frames.Length, 32, (x, _) => frames[x / 32]);

    /// <summary>A solid PNG.</summary>
    public static byte[] Solid(int width, int height, SKColor color) => Png(width, height, (_, _) => color);

    /// <summary>The paths of the room's pictures as the store holds them, under Resources/Textures.</summary>
    public const string Textures = "Resources/Textures/";

    public static MemoryForkStore AddRoomFork(this MemoryForkStore store)
    {
        store.Add("r", "Resources/Prototypes/body.yml", TestForks.Body)
            .Add("r", "Resources/Prototypes/room.yml", Prototypes)
            // The doll is a job's own body, read with the fork: red, in two frames that play.
            .Add("r", Textures + "Mobs/doll.rsi/meta.json",
                """{"version":1,"license":"CC0-1.0","copyright":"Doll made up","size":{"x":32,"y":32},"states":[{"name":"doll","delays":[[0.1,0.1]]}]}""")
            .Add("r", Textures + "Mobs/doll.rsi/doll.png", Strip(Red, Red))
            // A body of eight directions that moves only facing diagonally.
            .Add("r", Textures + "Mobs/statue.rsi/meta.json",
                """{"version":1,"license":"CC0-1.0","copyright":"Statue made up","size":{"x":32,"y":32},"states":[{"name":"statue","directions":8,"delays":[[1],[1],[1],[1],[0.1,0.1],[0.1,0.1],[0.1,0.1],[0.1,0.1]]}]}""")
            .Add("r", Textures + "Mobs/statue.rsi/statue.png", Strip(Enumerable.Repeat(Grey, 12).ToArray()));
        var walls = Enumerable.Range(0, 8).Select(i => ($"wall{i}", 4)).ToArray();
        store.AddUnfetched("r", Textures + "Structures/wall.rsi/meta.json", Meta("Wall made up", walls));
        foreach (var (state, _) in walls)
            store.AddUnfetched("r", Textures + $"Structures/wall.rsi/{state}.png", Strip(Grey, Grey, Grey, Grey));
        return store
            .AddUnfetched("r", Textures + "Structures/crate.rsi/meta.json", Meta("Crate made up", ("crate", 1), ("glow", 1), ("hidden", 1)))
            .AddUnfetched("r", Textures + "Structures/crate.rsi/crate.png", Strip(Green))
            .AddUnfetched("r", Textures + "Structures/crate.rsi/glow.png", Png(32, 32, (x, y) => x < 4 && y < 4 ? White : SKColors.Transparent))
            .AddUnfetched("r", Textures + "Structures/crate.rsi/hidden.png", Strip(Purple))
            // In the crate's folder, but nothing draws it: never downloaded.
            .AddUnfetched("r", Textures + "Structures/crate.rsi/unused.png", Strip(Purple))
            .AddUnfetched("r", Textures + "Structures/sticker.png", Solid(16, 16, White))
            .AddUnfetched("r", Textures + "Structures/attributions.yml", """
                - files: [ "sticker.png" ]
                  license: "CC0-1.0"
                  copyright: "Sticker made up"
                """)
            .AddUnfetched("r", Textures + "Structures/railing.rsi/meta.json", Meta("Railing made up", ("railing", 1)))
            .AddUnfetched("r", Textures + "Structures/railing.rsi/railing.png", Strip(Yellow))
            // Four directions, each its own colour: south red, north green, east blue, west yellow.
            .AddUnfetched("r", Textures + "Structures/chair.rsi/meta.json", Meta("Chair made up", ("chair", 4)))
            .AddUnfetched("r", Textures + "Structures/chair.rsi/chair.png", Strip(Red, Green, Blue, Yellow))
            .AddUnfetched("r", Textures + "Decals/marks.rsi/meta.json", Meta("Mark made up", ("mark", 1)))
            .AddUnfetched("r", Textures + "Decals/marks.rsi/mark.png", Strip(White))
            .AddUnfetched("r", Textures + "Decals/paint.png", Solid(32, 32, White))
            .AddUnfetched("r", Textures + "Tiles/test.png", Strip(Blue))
            // Upstream names some folders' credit lists attribution.yml.
            .AddUnfetched("r", Textures + "Tiles/attribution.yml", """
                - files: [ "test.png" ]
                  license: "CC0-1.0"
                  copyright: "Tile made up"
                """)
            .AddUnfetched("r", Textures + "Tiles/grass.png", Strip(Green))
            .AddUnfetched("r", Textures + "Tiles/grass_edge.png", Png(32, 32, (_, y) => y < 8 ? Yellow : SKColors.Transparent))
            .AddUnfetched("r", Textures + "Tiles/grass_corner.png", Solid(32, 32, Purple));
    }
}
