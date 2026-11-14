using System.Numerics;
using System.Text;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Rooms;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>Room files, and the rooms read from them.</summary>
public class RoomFileTests
{
    private const string Good = """
        id: test
        name: Test room
        departments: [ Service ]
        jobs: [ Clerk ]
        stand: "1,0"
        legend:
          a: FloorA
          b: FloorB
        tiles:
        - "ab."
        - " ba"
        entities:
        - { proto: Thing, pos: "1.5,0.5", rot: 90 }
        - { proto: Other, pos: "0.25,1.75" }
        decals:
        - { id: Mark, pos: "1,1", color: "#FF0000", angle: 180, z: 2 }
        - { id: Plain, pos: "0,0" }
        """;

    [Fact]
    public void A_room_file_is_read_with_its_rows_from_north_to_south()
    {
        var room = Room.TryParse(Good, out var problem);

        Assert.Null(problem);
        Assert.NotNull(room);
        Assert.Equal(("test", "Test room", 3, 2, (1, 0)), (room.Id, room.Name, room.Width, room.Height, room.Stand));
        Assert.Equal(["Service"], room.Departments);
        Assert.Equal(["Clerk"], room.Jobs);
        // A file from before jobsIfNoDepartment reads as naming none.
        Assert.Empty(room.JobsIfNoDepartment);
        Assert.Equal(["Porter", "Boss"], Room.TryParse(Good.Replace("jobs: [ Clerk ]", "jobs: [ Clerk ]\njobsIfNoDepartment: [ Porter, Boss ]"), out _)!.JobsIfNoDepartment);
        // The first row is the north one; a space or a dot is no tile.
        Assert.Equal(["FloorA", "FloorB", null, null, "FloorB", "FloorA"],
            new[] { (0, 1), (1, 1), (2, 1), (0, 0), (1, 0), (2, 0) }.Select(s => room.TileAt(s.Item1, s.Item2)));
        Assert.Equal(new RoomEntity("Thing", 1.5f, 0.5f, Math.PI / 2), room.Entities[0]);
        Assert.Equal(new RoomEntity("Other", 0.25f, 1.75f, 0), room.Entities[1]);
        Assert.Equal(new RoomDecal("Mark", 1, 1, new Rgba(1, 0, 0), Math.PI, 2), room.Decals[0]);
        Assert.Equal(new RoomDecal("Plain", 0, 0, Rgba.White, 0, 0), room.Decals[1]);
    }

    [Theory]
    [InlineData("id: test", "id: [", null)]
    [InlineData("id: test", "- a list\n- of things\nid: test", null)]
    [InlineData("name: Test room", "name: A\nname: B", null)]
    [InlineData("id: test", "", "has no id")]
    [InlineData("legend:", "legends:", "has no legend")]
    [InlineData("- \"ab.\"", "- \"ac.\"", "'c', which the legend does not name")]
    [InlineData("stand: \"1,0\"", "stand: \"0,0\"", "is not on a tile")]
    [InlineData("stand: \"1,0\"", "stand: \"1.5,0\"", "is not a tile's")]
    [InlineData("{ proto: Thing,", "{ prot: Thing,", "entity 1 has no proto")]
    [InlineData("pos: \"1.5,0.5\"", "pos: \"here\"", "has pos 'here'")]
    [InlineData("rot: 90", "rot: east", "not a number of degrees")]
    [InlineData("color: \"#FF0000\"", "color: \"nope\"", "has colour 'nope'")]
    [InlineData("z: 2", "z: top", "has z 'top'")]
    [InlineData("tiles:", "tiles: none\nrows:", "tiles is not a list of text")]
    [InlineData("jobs: [ Clerk ]", "jobs: [ Clerk ]\njobsIfNoDepartment: { Clerk: 1 }", "jobsIfNoDepartment is not a list of text")]
    [InlineData("jobs: [ Clerk ]", "jobs: [ Clerk ]\njobsIfNoDepartment: [ [ Clerk ] ]", "jobsIfNoDepartment is not a list of text")]
    [InlineData("- \"ab.\"", "- \"ab.ababababababababababababababab\"", "the room is 33 by 2 tiles; it may be at most 32 by 32")]
    [InlineData("pos: \"1.5,0.5\"", "pos: \"1000.5,1000.5\"", "entity 1 (Thing) is at 1000.5,1000.5, more than 2 tiles outside the room")]
    [InlineData("pos: \"1,1\"", "pos: \"-2.5,0\"", "decal 1 (Mark) is at -2.5,0, more than 2 tiles outside the room")]
    public void A_broken_room_file_gives_the_reason_and_no_room(string from, string to, string? reason)
    {
        Assert.Contains(from, Good);
        var room = Room.TryParse(Good.Replace(from, to), out var problem);

        Assert.Null(room);
        Assert.NotNull(problem);
        if (reason != null)
            Assert.Contains(reason, problem);
    }

    [Fact]
    public void A_room_may_hold_at_most_1024_entities_and_as_many_decals()
    {
        string With(string key, int count) => Good.Replace(key + ":", key + ":\n" + string.Concat(Enumerable.Repeat(key == "entities"
            ? "- { proto: Thing, pos: \"0.5,0.5\" }\n" : "- { id: Mark, pos: \"0,0\" }\n", count)), StringComparison.Ordinal);

        Assert.NotNull(Room.TryParse(With("entities", 1022), out _));
        Assert.Null(Room.TryParse(With("entities", 1023), out var entities));
        Assert.Equal("it has 1025 entities; it may have at most 1024", entities);
        Assert.Null(Room.TryParse(With("decals", 1023), out var decals));
        Assert.Equal("it has 1025 decals; it may have at most 1024", decals);
    }

    private static string Folder(string text)
    {
        var folder = Directory.CreateTempSubdirectory("paperdoll-rooms-").FullName;
        File.WriteAllText(Path.Combine(folder, "bar.yml"), text);
        return folder;
    }

    [Fact]
    public void The_built_in_bar_is_read_and_a_user_file_of_the_same_name_replaces_it()
    {
        var builtIn = RoomCatalog.Load(null);
        Assert.Equal("Bar", builtIn.Rooms["bar"].Name);
        Assert.Empty(builtIn.Problems);

        var folder = Folder(TestRooms.Room);
        try
        {
            var catalog = RoomCatalog.Load(folder);
            Assert.Equal("Test bar", catalog.Rooms["bar"].Name);
            Assert.Empty(catalog.Problems);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_broken_user_file_is_reported_and_the_built_in_room_used()
    {
        var folder = Folder("id: bar\nname: Mine\n");
        try
        {
            var catalog = RoomCatalog.Load(folder);
            Assert.Equal("Bar", catalog.Rooms["bar"].Name);
            var problem = Assert.Single(catalog.Problems);
            Assert.Equal($"{Path.Combine(folder, "bar.yml")} could not be read: it has no legend. The built-in bar.yml is used.", problem);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static Room Named(string id, string fields) =>
        Room.TryParse($"id: {id}\n{fields}\nstand: \"0,0\"\nlegend: {{ f: FloorTest }}\ntiles: [ \"f\" ]\n", out _)!;

    [Fact]
    public void A_job_gets_the_room_naming_it_then_one_for_its_department_then_the_hallway()
    {
        var prototypes = PrototypeIndex.Load([new PrototypeSource("room.yml", Encoding.UTF8.GetBytes(TestRooms.Prototypes))]);
        var rooms = new[]
        {
            Named("hallway", ""), Named("shop", "jobs: [ Clerk ]"), Named("lounge", "departments: [ Service ]"), Named("office", "departments: [ Command ]"),
            Named("attic", "jobsIfNoDepartment: [ Boss, Loner ]"),
        };
        var catalog = new RoomCatalog(rooms.ToDictionary(r => r.Id), []);

        // The Clerk's own room wins over its department's.
        Assert.Equal("shop", catalog.For("Clerk", prototypes).Id);
        // The Porter is in Service and in Command, which weighs more, as the game sorts departments.
        Assert.Equal("office", catalog.For("Porter", prototypes).Id);
        // A department's room wins over one taking the job only when its departments have none.
        Assert.Equal("office", catalog.For("Boss", prototypes).Id);
        Assert.Equal("attic", catalog.For("Loner", prototypes).Id);
        Assert.Equal("hallway", catalog.For("Nobody", prototypes).Id);
        Assert.Equal("hallway", catalog.For(null, prototypes).Id);
    }

    // Made-up departments under upstream's ids and Delta-V's, and the jobs in them.
    private const string Departments = """
        - type: department
          id: Command
          weight: 100
          roles: [ Boss, Overseer ]
        - type: department
          id: Security
          weight: 20
          roles: [ Guard, Boss ]
        - type: department
          id: Justice
          roles: [ Judge ]
        - type: department
          id: Medical
          roles: [ Healer ]
        - type: department
          id: Science
          roles: [ Thinker ]
        - type: department
          id: Epistemics
          roles: [ Mystic ]
        - type: department
          id: Engineering
          roles: [ Fixer, Overseer ]
        - type: department
          id: Cargo
          roles: [ Hauler ]
        - type: department
          id: Logistics
          roles: [ Mover ]
        - type: department
          id: Service
          roles: [ Server, Chef, Janitor, Lawyer, Chaplain, Librarian ]
        - type: department
          id: Civilian
          weight: -10
          roles: [ Wanderer ]
        """;

    [Fact]
    public void The_built_in_rooms_suit_each_department_and_the_jobs_they_name()
    {
        var prototypes = PrototypeIndex.Load([new PrototypeSource("departments.yml", Encoding.UTF8.GetBytes(Departments))]);
        var catalog = RoomCatalog.Load(null);
        string RoomOf(string? job) => catalog.For(job, prototypes).Id;

        // Heads go to the bridge, Command weighing most.
        Assert.Equal(["bridge", "bridge"], new[] { "Boss", "Overseer" }.Select(RoomOf));
        Assert.Equal(["security", "medical", "lab", "engine", "cargo", "bar"], new[] { "Guard", "Healer", "Thinker", "Fixer", "Hauler", "Server" }.Select(RoomOf));
        // Upstream keeps the chaplain and the librarian in Service.
        Assert.Equal(["bar", "bar"], new[] { "Chaplain", "Librarian" }.Select(RoomOf));
        // Delta-V's names for the security, science and cargo departments.
        Assert.Equal(["security", "lab", "cargo"], new[] { "Judge", "Mystic", "Mover" }.Select(RoomOf));
        // Jobs a room names, over their department's room.
        Assert.Equal(["kitchen", "hallway", "security"], new[] { "Chef", "Janitor", "Lawyer" }.Select(RoomOf));
        // A department no room names, a job in none, and no job: the hallway.
        Assert.Equal(["hallway", "hallway", "hallway"], new[] { "Wanderer", "Nobody", null }.Select(RoomOf));

        // A fork with no Service department, its service jobs in Civilian with the Passenger.
        var civilian = PrototypeIndex.Load([new PrototypeSource("departments.yml", Encoding.UTF8.GetBytes("""
            - type: department
              id: Command
              weight: 100
              roles: [ HeadOfPersonnel ]
            - type: department
              id: Civilian
              roles: [ Passenger, Bartender, Botanist, Chaplain, Clown, Librarian, Mime, Musician, Reporter, Chef, Janitor, Lawyer, HeadOfPersonnel ]
            """))]);
        string CivilianRoomOf(string job) => catalog.For(job, civilian).Id;
        Assert.Equal(["bar", "bar", "bar", "bar", "bar", "bar", "bar", "bar"],
            new[] { "Bartender", "Botanist", "Chaplain", "Clown", "Librarian", "Mime", "Musician", "Reporter" }.Select(CivilianRoomOf));
        Assert.Equal(["hallway", "kitchen", "hallway", "security", "bridge"], new[] { "Passenger", "Chef", "Janitor", "Lawyer", "HeadOfPersonnel" }.Select(CivilianRoomOf));

        // The same, with the chaplain and the librarian moved to Epistemics, which has a room.
        var epistemics = PrototypeIndex.Load([new PrototypeSource("departments.yml", Encoding.UTF8.GetBytes("""
            - type: department
              id: Epistemics
              roles: [ Chaplain, Librarian ]
            - type: department
              id: Civilian
              roles: [ Passenger, Bartender, Botanist, Clown, Mime ]
            """))]);
        Assert.Equal(["lab", "lab", "bar", "bar", "hallway"], new[] { "Chaplain", "Librarian", "Bartender", "Mime", "Passenger" }.Select(j => catalog.For(j, epistemics).Id));
    }

    // Light fixtures a room lights itself with.
    private static readonly string[] Lamps = ["Poweredlight", "PoweredSmallLight"];

    [Fact]
    public void Each_built_in_room_is_whole_with_a_door_lamps_and_a_clear_tile_to_stand_on()
    {
        var assembly = typeof(RoomCatalog).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Paperdoll.Core.Rooms.", StringComparison.Ordinal) && n.EndsWith(".yml", StringComparison.Ordinal)).ToList();
        Assert.Equal(["bar", "bridge", "cargo", "engine", "hallway", "kitchen", "lab", "medical", "security"],
            resources.Select(n => n["Paperdoll.Core.Rooms.".Length..^".yml".Length]).Order(StringComparer.Ordinal));
        foreach (var resource in resources)
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var room = Room.TryParse(reader.ReadToEnd(), out var problem);
            Assert.True(room != null, $"{resource}: {problem}");
            // Each file holds the room its name says, so no room hides another.
            Assert.Equal(resource, $"Paperdoll.Core.Rooms.{room.Id}.yml");
            Assert.Contains(room.Entities, e => e.Proto.StartsWith("Airlock", StringComparison.Ordinal));
            Assert.Contains(room.Entities, e => Lamps.Contains(e.Proto));
            // Everything stands within the room's own squares.
            Assert.All(room.Entities, e => Assert.True(e.X >= 0 && e.Y >= 0 && e.X < room.Width && e.Y < room.Height, $"{room.Id}: {e.Proto} at {e.X},{e.Y}"));
            Assert.All(room.Decals, d => Assert.True(d.X >= 0 && d.Y >= 0 && d.X < room.Width && d.Y < room.Height, $"{room.Id}: {d.Id} at {d.X},{d.Y}"));
            // Only a carpet lies where the character stands.
            var (x, y) = room.Stand;
            Assert.All(room.Entities.Where(e => (int)MathF.Floor(e.X) == x && (int)MathF.Floor(e.Y) == y), e => Assert.StartsWith("Carpet", e.Proto));
        }
    }
}

/// <summary>What a demo room draws, from made-up fork "r".</summary>
public class RoomDrawingTests
{
    private static readonly PrototypeIndex Prototypes = PrototypeIndex.Load([new PrototypeSource("room.yml", Encoding.UTF8.GetBytes(TestRooms.Prototypes))]);

    private static Room Parse(string text) => Room.TryParse(text, out var problem) ?? throw new InvalidDataException(problem);

    // A room of test floor, rows from the north, with these entities and decals.
    private static Room Floor(int width, int height, string entities, string decals = "") => Parse(
        "id: test\nstand: \"0,0\"\nlegend: { f: FloorTest }\ntiles:\n"
        + string.Concat(Enumerable.Repeat($"- \"{new string('f', width)}\"\n", height))
        + $"entities:\n{entities}\ndecals:\n{decals}\n");

    private static async Task<(EditorSession Session, MemoryForkStore Store)> ForkR()
    {
        var store = TestForks.Store();
        var session = new EditorSession(store);
        await session.LoadForkAsync(TestRooms.Fork, update: false, ct: TestContext.Current.CancellationToken);
        return (session, store);
    }

    [Fact]
    public void Sprite_layers_keep_offset_scale_rotation_colour_visibility_texture_and_shader()
    {
        var crate = SpriteInfo.Of(Prototypes, "TestCrate")!;

        Assert.Equal("Structures/crate.rsi", crate.Rsi);
        Assert.Equal(5, crate.Layers.Count);
        Assert.Equal(("Structures/crate.rsi", "crate", true, false), (crate.Layers[0].Rsi, crate.Layers[0].State, crate.Layers[0].Visible, crate.Layers[0].Unshaded));
        Assert.Equal(("glow", "unshaded", true), (crate.Layers[1].State, crate.Layers[1].Shader, crate.Layers[1].Unshaded));
        Assert.False(crate.Layers[2].Visible);
        var sticker = crate.Layers[3];
        Assert.Equal((null, "Structures/sticker.png"), (sticker.State, sticker.Texture));
        Assert.Equal((0.25f, 0.25f, 0.5f, 0.5f), (sticker.OffsetX, sticker.OffsetY, sticker.ScaleX, sticker.ScaleY));
        Assert.Equal(Math.PI / 2, sticker.Rotation, 5);
        Assert.Equal(new Rgba(0, 1, 0), sticker.Color);

        var railing = SpriteInfo.Of(Prototypes, "TestRailing")!;
        Assert.Equal((DrawDepths.Mobs, 0f, 0.5f), (railing.DrawDepth, railing.OffsetX, railing.OffsetY));
        // With no layers, the sprite's own state makes the one layer.
        Assert.Equal("railing", Assert.Single(railing.Layers).State);
        Assert.True(SpriteInfo.Of(Prototypes, "TestChair")!.NoRot);
        Assert.True(SpriteInfo.Of(Prototypes, "TestLamp")!.SnapCardinals);
        Assert.False(SpriteInfo.Of(Prototypes, "TestHidden")!.Visible);
        var wall = SpriteInfo.Of(Prototypes, "TestWall")!;
        Assert.Equal((-2, 0), (wall.DrawDepth, wall.Layers.Count));
        Assert.Null(SpriteInfo.Of(Prototypes, "NotInThisFork"));
    }

    [Fact]
    public void Rsi_direction_follows_the_angle_and_the_state_s_directions()
    {
        Assert.Equal(
            [Direction.South, Direction.East, Direction.North, Direction.West, Direction.South],
            new[] { 0, Math.PI / 2, Math.PI, 3 * Math.PI / 2, 2 * Math.PI - 0.01 }.Select(a => SpriteMath.ForState(4, SpriteMath.Positive(a))));
        Assert.Equal(Direction.SouthEast, SpriteMath.ForState(8, Math.PI / 4));
        Assert.Equal(Direction.NorthWest, SpriteMath.ForState(8, 5 * Math.PI / 4));
        Assert.Equal(Direction.South, SpriteMath.ForState(1, Math.PI));
    }

    // The angle, in degrees, that a frame's rows run at in the world: 0 when upright.
    private static double RowAngle(DrawItem item)
    {
        var along = Vector2.Transform(new Vector2(item.Width, 0), item.Transform) - Vector2.Transform(Vector2.Zero, item.Transform);
        return Math.Round(SpriteMath.Positive(Math.Atan2(along.Y, along.X)) * 180 / Math.PI, 3) % 360;
    }

    [Fact]
    public async Task A_turned_entity_shows_the_frame_it_faces_upright_unless_its_state_has_one_direction()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var room = Floor(4, 2, """
            - { proto: TestSofa, pos: "0.5,0.5", rot: 90 }
            - { proto: TestChair, pos: "1.5,0.5", rot: 90 }
            - { proto: TestLamp, pos: "2.5,0.5", rot: 100 }
            - { proto: TestRailing, pos: "3.5,0.5", rot: 100 }
            - { proto: TestSofa, pos: "0.5,1.5", rot: 180 }
            """);
        var scene = await session.RoomAsync(room, TestContext.Current.CancellationToken);
        DrawItem Item(int index) => scene.Entities.Single(e => e.Index == index).Items[0];

        // Four directions: the east frame, turned back upright.
        Assert.Equal((Direction.East, 0.0), (Item(0).Direction, RowAngle(Item(0))));
        Assert.Equal((Direction.North, 0.0), (Item(4).Direction, RowAngle(Item(4))));
        // noRot: the frame the angle gives, never turned.
        Assert.Equal((Direction.East, 0.0), (Item(1).Direction, RowAngle(Item(1))));
        // One direction: the frame turns with the entity, or only by what is past the nearest
        // quarter turn with snapCardinals.
        Assert.Equal((Direction.South, 100.0), (Item(3).Direction, RowAngle(Item(3))));
        Assert.Equal((Direction.South, 10.0), (Item(2).Direction, RowAngle(Item(2))));
        // Turned about the entity's centre: still on its square.
        Assert.Equal(new Box(0, 0, 1, 1), Round(Item(0).Bounds));
    }

    private static Box Round(Box box) => new(MathF.Round(box.MinX, 3), MathF.Round(box.MinY, 3), MathF.Round(box.MaxX, 3), MathF.Round(box.MaxY, 3));

    [Fact]
    public void A_wall_alone_shows_bare_corners_and_one_with_a_neighbour_joins_it()
    {
        var plan = RoomPlan.Make(Floor(4, 1, """
            - { proto: TestWall, pos: "0.5,0.5" }
            - { proto: TestWall, pos: "2.5,0.5" }
            - { proto: TestWall, pos: "3.5,0.5" }
            - { proto: TestCrate, pos: "1.5,0.5" }
            """), Prototypes);
        string Corners(int index) => string.Join(" ", plan.Entities.Single(e => e.Index == index).Layers.Select(l => $"{l.State}/{l.DirOffset}"));

        // South-east, north-east, north-west and south-west, each from its own direction's frame.
        // The crate beside the first wall smooths with nothing.
        Assert.Equal("wall0/None wall0/CounterClockwise wall0/Flip wall0/Clockwise", Corners(0));
        // A wall to the east fills the eastern corners: clockwise of north-east, counter-clockwise of south-east.
        Assert.Equal("wall1/None wall4/CounterClockwise wall0/Flip wall0/Clockwise", Corners(1));
        Assert.Equal("wall0/None wall0/CounterClockwise wall1/Flip wall4/Clockwise", Corners(2));
        Assert.All(plan.Entities.Single(e => e.Index == 0).Layers, l => Assert.Equal("Structures/wall.rsi", l.Rsi));
    }

    // The layers of the entity at a place in the room file, as "state/dirOffset", bottom to top.
    private static string Layers(RoomPlan plan, int index) =>
        string.Join(" ", plan.Entities.Single(e => e.Index == index).Layers.Select(l => $"{l.State}/{l.DirOffset}"));

    // Only the states, for entities whose layers all have the same direction offsets.
    private static string States(RoomPlan plan, int index) =>
        string.Join(" ", plan.Entities.Single(e => e.Index == index).Layers.Select(l => l.State));

    [Fact]
    public void A_newer_form_wall_picks_its_corner_states_alone_in_a_run_at_a_corner_and_at_a_junction()
    {
        // A run of three along the south row, one alone in the south-east, an L and a T-junction further north.
        var plan = RoomPlan.Make(Floor(7, 4, """
            - { proto: TestNewWall, pos: "0.5,0.5" }
            - { proto: TestNewWall, pos: "1.5,0.5" }
            - { proto: TestNewWall, pos: "2.5,0.5" }
            - { proto: TestNewWall, pos: "6.5,0.5" }
            - { proto: TestNewWall, pos: "0.5,2.5" }
            - { proto: TestNewWall, pos: "1.5,2.5" }
            - { proto: TestNewWall, pos: "0.5,3.5" }
            - { proto: TestNewWall, pos: "3.5,2.5" }
            - { proto: TestNewWall, pos: "4.5,2.5" }
            - { proto: TestNewWall, pos: "5.5,2.5" }
            - { proto: TestNewWall, pos: "4.5,3.5" }
            """), Prototypes);

        // Four corner layers, south-east, south-west, north-east, north-west, each from its own
        // direction's frame and of the sprite's own RSI; a wall alone shows state 0 in each.
        Assert.Equal("wall0/None wall0/Clockwise wall0/CounterClockwise wall0/Flip", Layers(plan, 3));
        Assert.All(plan.Entities.Single(e => e.Index == 3).Layers, l => Assert.Equal("Structures/newwall.rsi", l.Rsi));
        // In a run each corner reads 1 for the cardinal clockwise of it and 2 for the one
        // counter-clockwise: the south-east corner's east is 2, the north-east corner's east is 1.
        Assert.Equal("wall2 wall0 wall1 wall0", States(plan, 0));
        Assert.Equal("wall2 wall1 wall1 wall2", States(plan, 1));
        Assert.Equal("wall0 wall1 wall0 wall2", States(plan, 2));
        // The corner of an L: east and north meet in the north-east corner, 5.
        Assert.Equal("wall2 wall0 wall5 wall1", States(plan, 4));
        // The middle of a T: both north corners meet the stem.
        Assert.Equal("wall2 wall1 wall5 wall5", States(plan, 8));
    }

    [Fact]
    public void A_newer_form_wall_joins_its_mask_counts_its_alt_mask_otherwise_and_ignores_other_keys()
    {
        var plan = RoomPlan.Make(Floor(6, 3, """
            - { proto: TestNewWall, pos: "0.5,0.5" }
            - { proto: TestNewWindow, pos: "1.5,0.5" }
            - { proto: TestNewWall, pos: "4.5,0.5" }
            - { proto: TestNewWall, pos: "3.5,0.5" }
            - { proto: TestNewWindow, pos: "5.5,0.5" }
            - { proto: TestNewWall, pos: "4.5,1.5" }
            - { proto: TestPipe, pos: "0.5,2.5" }
            - { proto: TestNewWall, pos: "1.5,2.5" }
            - { proto: TestNewWall, pos: "5.5,2.5" }
            """), Prototypes);

        // A window east of a wall is in the wall's altMask only: 4 and 3 where a wall would be 2 and 1.
        Assert.Equal("wall4 wall0 wall3 wall0", States(plan, 0));
        // The window counts the wall by its own altMask.
        Assert.Equal("window0 window3 window0 window4", States(plan, 1));
        // Wall west, wall north, window east: the north-east corner mixes the two masks (8 and up),
        // the south-east one has the window alone.
        Assert.Equal("wall4 wall1 wall10 wall5", States(plan, 2));
        // A pipe's key is in neither mask, and the pipe's own corners take no wall.
        Assert.Equal("wall0 wall0 wall0 wall0", States(plan, 7));
        Assert.Equal("pipe0 pipe0 pipe0 pipe0", string.Join(" ", plan.Entities.Single(e => e.Index == 6).Layers.Skip(1).Select(l => l.State)));
        // A diagonal neighbour alone fills no corner.
        Assert.Equal("wall0 wall0 wall0 wall0", States(plan, 8));
    }

    [Fact]
    public void Older_and_newer_form_walls_join_each_other_by_key()
    {
        var plan = RoomPlan.Make(Floor(2, 1, """
            - { proto: TestWall, pos: "0.5,0.5" }
            - { proto: TestNewWall, pos: "1.5,0.5" }
            """), Prototypes);

        // The older wall's corners as before, with the newer wall to its east.
        Assert.Equal("wall1/None wall4/CounterClockwise wall0/Flip wall0/Clockwise", Layers(plan, 0));
        Assert.Equal("wall0 wall1 wall0 wall2", States(plan, 1));
    }

    [Fact]
    public void A_turned_newer_form_wall_reads_its_neighbours_in_its_own_frame()
    {
        var plan = RoomPlan.Make(Floor(2, 1, """
            - { proto: TestNewWall, pos: "0.5,0.5", rot: 90 }
            - { proto: TestNewWall, pos: "1.5,0.5" }
            """), Prototypes);

        // Turned a quarter counter-clockwise, its east is its own south.
        Assert.Equal("wall1 wall2 wall0 wall0", States(plan, 0));
    }

    [Fact]
    public void Each_newer_smoothing_kind_sets_its_own_layers()
    {
        var plan = RoomPlan.Make(Floor(9, 3, """
            - { proto: TestPost, pos: "1.5,1.5" }
            - { proto: TestNewWall, pos: "1.5,2.5" }
            - { proto: TestNewWall, pos: "2.5,1.5" }
            - { proto: TestPost, pos: "4.5,1.5" }
            - { proto: TestNewWall, pos: "4.5,0.5" }
            - { proto: TestNewWall, pos: "5.5,0.5" }
            - { proto: TestNewWall, pos: "5.5,1.5" }
            - { proto: TestPost, pos: "7.5,1.5" }
            - { proto: TestNewWindow, pos: "7.5,0.5" }
            - { proto: TestNewWindow, pos: "8.5,0.5" }
            - { proto: TestNewWindow, pos: "8.5,1.5" }
            - { proto: TestRug, pos: "0.5,0.5" }
            - { proto: TestRug, pos: "1.5,0.5" }
            """), Prototypes);

        // Direction: south 1, east 2, north 4, west 8. Diagonal: 1 when south, south-east and east
        // all match. AltDiagonal, put at index 0 and unshaded: 1 for the mask, 2 for the altMask.
        Assert.Equal("alt0 dir6 diag0", States(plan, 0));
        Assert.Equal("alt1 dir3 diag1", States(plan, 3));
        Assert.Equal("alt2 dir0 diag0", States(plan, 7));
        Assert.Equal([true, false, false], plan.Entities.Single(e => e.Index == 0).Layers.Select(l => l.Shader == "unshaded"));
        // Edge: the rug, then a layer a tile out each way, shown where no rug is.
        var rug = plan.Entities.Single(e => e.Index == 11).Layers;
        Assert.Equal(["rug", "edge_south", "edge_east", "edge_north", "edge_west"], rug.Select(l => l.State));
        Assert.Equal([true, true, false, true, true], rug.Select(l => l.Visible));
        Assert.Equal([(0f, 0f), (0f, -1f), (1f, 0f), (0f, 1f), (-1f, 0f)], rug.Select(l => (l.OffsetX, l.OffsetY)));
    }

    [Fact]
    public void A_smoothing_kind_not_known_leaves_the_entity_its_own_layers_and_its_key()
    {
        var plan = RoomPlan.Make(Floor(2, 1, """
            - { proto: TestOddSmooth, pos: "0.5,0.5" }
            - { proto: TestNewWall, pos: "1.5,0.5" }
            """), Prototypes);

        Assert.Equal("crate", States(plan, 0));
        Assert.Equal("wall0 wall1 wall0 wall2", States(plan, 1));
    }

    [Fact]
    public void A_computer_shows_its_computer_visuals_states_with_screen_and_keys_unshaded()
    {
        var plan = RoomPlan.Make(Floor(1, 1, "- { proto: TestComputer, pos: \"0.5,0.5\" }"), Prototypes);
        var layers = plan.Entities.Single().Layers;

        // The keyboard has no state given; the panel takes its state but stays shut.
        Assert.Equal(["computer", null, "screen", "keys", "panel"], layers.Select(l => l.State));
        Assert.Equal([true, true, true, true, false], layers.Select(l => l.Visible));
        Assert.Equal([null, null, "unshaded", "unshaded", null], layers.Select(l => l.Shader));
        Assert.Equal(new[] { "meta.json", "computer.png", "screen.png", "keys.png" }.Select(f => "Structures/computer.rsi/" + f).ToHashSet(), plan.RsiFiles.ToHashSet());
    }

    [Fact]
    public void A_computer_layer_its_computer_visuals_leave_unset_draws_nothing_unless_it_has_a_texture()
    {
        var plan = RoomPlan.Make(Floor(1, 1, "- { proto: TestBareComputer, pos: \"0.5,0.5\" }"), Prototypes);
        var layers = plan.Entities.Single().Layers;

        // The game blanks the keyboard and the screen, though the prototype gives them states, and
        // the screen is still shown; the keys, set to null, keep their texture, drawn unshaded.
        Assert.Equal(["computer", null, null, null], layers.Select(l => l.State));
        Assert.Equal([null, null, null, "Structures/sticker.png"], layers.Select(l => l.Texture));
        Assert.Equal([true, true, true, true], layers.Select(l => l.Visible));
        Assert.Equal([null, null, "unshaded", "unshaded"], layers.Select(l => l.Shader));
        Assert.Equal(new[] { "Structures/computer.rsi/meta.json", "Structures/computer.rsi/computer.png" }.ToHashSet(), plan.RsiFiles.ToHashSet());
        Assert.Equal(new[] { "Structures/sticker.png" }.ToHashSet(), plan.Pictures.ToHashSet());
    }

    [Fact]
    public void A_door_state_written_as_null_blanks_its_layer_and_one_not_written_takes_the_default()
    {
        var plan = RoomPlan.Make(Floor(6, 1, """
            - { proto: TestDoor, pos: "0.5,0.5" }
            - { proto: TestNullDoor, pos: "1.5,0.5" }
            - { proto: TestNullUnlitDoor, pos: "2.5,0.5" }
            - { proto: TestShutDoor, pos: "3.5,0.5" }
            - { proto: TestShutDoorNulled, pos: "4.5,0.5" }
            - { proto: TestShutDoorNulledChild, pos: "5.5,0.5" }
            """), Prototypes);
        LayerSpec Layer(int index, string key) => plan.Entities.Single(e => e.Index == index).Layers.Single(l => l.MapKeys.Contains(key));
        const string Base = "enum.DoorVisualLayers.Base", Unlit = "enum.DoorVisualLayers.BaseUnlit";

        // Not written: the defaults "closed" and, with openUnlitVisible, "closed_unlit".
        Assert.Equal(("closed", "closed_unlit"), (Layer(0, Base).State, Layer(0, Unlit).State));
        // Written as null: the game sets no state, so the layer draws nothing.
        Assert.Null(Layer(1, Base).State);
        Assert.Equal(("closed", null, true), (Layer(2, Base).State, Layer(2, Unlit).State, Layer(2, Unlit).Visible));
        // A child's null over its parent's state, and a grandchild that inherits that null.
        Assert.Equal("shut", Layer(3, Base).State);
        Assert.Null(Layer(4, Base).State);
        Assert.Null(Layer(5, Base).State);
        // Nothing is fetched for a blanked layer.
        var rsi = RoomPlan.Make(Floor(1, 1, "- { proto: TestNullDoor, pos: \"0.5,0.5\" }"), Prototypes).RsiFiles;
        Assert.DoesNotContain("Structures/door.rsi/closed.png", rsi);
        Assert.Contains("Structures/door.rsi/closed_unlit.png", rsi);
    }

    [Fact]
    public async Task A_room_of_newer_form_walls_and_windows_downloads_every_state_it_draws_and_nothing_else()
    {
        var (session, store) = await ForkR();
        await using var _s = session;
        var room = Floor(4, 2, """
            - { proto: TestNewWall, pos: "0.5,0.5" }
            - { proto: TestNewWall, pos: "1.5,0.5" }
            - { proto: TestNewWindow, pos: "2.5,0.5" }
            - { proto: TestNewWall, pos: "1.5,1.5" }
            """);
        var plan = RoomPlan.Make(room, Prototypes);

        var scene = await session.RoomAsync(room, TestContext.Current.CancellationToken);

        var states = plan.Entities.SelectMany(e => e.Layers).Select(l => EntityComposer.StatePath(l.Rsi!, l.State!)).ToHashSet();
        Assert.Equal(states.Concat(["Structures/newwall.rsi/meta.json", "Structures/newwindow.rsi/meta.json"]).ToHashSet(), plan.RsiFiles);
        var fetched = Assert.Single(store.Fetches).Where(p => p.Contains(".rsi/", StringComparison.Ordinal)).ToHashSet();
        Assert.Equal(plan.RsiFiles.Select(f => TestRooms.Textures + f).ToHashSet(), fetched);
        // Every corner of every wall and window is drawn, from a state that was downloaded.
        Assert.Equal(16, scene.Entities.Sum(e => e.Items.Count));
        Assert.Subset(fetched, scene.Entities.SelectMany(e => e.Items).Select(i => TestRooms.Textures + i.Image).ToHashSet());
        Assert.DoesNotContain(TestRooms.Textures + "Structures/newwall.rsi/full.png", fetched);
    }

    [Fact]
    public async Task Entities_draw_by_depth_then_lower_edge_then_place_in_the_file()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var scene = await session.RoomAsync(Floor(2, 2, """
            - { proto: TestRailing, pos: "0.5,0.5" }
            - { proto: TestCrate, pos: "0.5,1.5" }
            - { proto: TestCrate, pos: "0.5,0.5" }
            - { proto: TestWall, pos: "1.5,1.5" }
            - { proto: TestCrate, pos: "1.5,0.5" }
            """), TestContext.Current.CancellationToken);

        // The wall is lowest; crates further south draw over those north of them, and those level
        // in the file's order; the railing is a mob's depth.
        Assert.Equal([3, 1, 2, 4, 0], scene.Entities.Select(e => e.Index));
    }

    [Fact]
    public async Task Decals_sit_on_their_corner_tinted_and_turned_about_their_centre_in_z_order()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var scene = await session.RoomAsync(Floor(3, 2, "", """
            - { id: TestPaint, pos: "2,1", color: "#FF8000", z: 1 }
            - { id: TestMark, pos: "0,0" }
            - { id: NotADecal, pos: "1,1" }
            - { id: TestMark, pos: "1,0", angle: 90 }
            """), TestContext.Current.CancellationToken);

        Assert.Equal(["Decals/marks.rsi/mark.png", "Decals/marks.rsi/mark.png", "Decals/paint.png"], scene.Decals.Select(d => d.Image));
        Assert.Equal([new Box(0, 0, 1, 1), new Box(1, 0, 2, 1), new Box(2, 1, 3, 2)], scene.Decals.Select(d => Round(d.Bounds)));
        Assert.Equal(new Rgba(1, 128 / 255f, 0), scene.Decals[2].Tint);
        Assert.Null(scene.Decals[0].Tint);
        // Turned a quarter counter-clockwise about its centre, its top-left pixel is at the bottom left.
        var corner = Vector2.Transform(Vector2.Zero, scene.Decals[1].Transform);
        Assert.Equal((1f, 0f), (MathF.Round(corner.X, 3), MathF.Round(corner.Y, 3)));
    }

    [Fact]
    public async Task A_tile_of_higher_edge_priority_throws_its_edge_sprites_on_other_tiles()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var room = Parse("""
            id: test
            stand: "0,0"
            legend: { f: FloorTest, g: FloorGrass }
            tiles:
            - "fff"
            - "fgf"
            - "fff"
            """);

        // Grass has North, East and SouthWest edges; the grass square itself gets none.
        Assert.Equal(
            [new PlannedEdge(0, 0, "Tiles/grass_corner.png", Math.PI * 1.5), new PlannedEdge(1, 2, "Tiles/grass_edge.png", Math.PI), new PlannedEdge(2, 1, "Tiles/grass_edge.png", Math.PI / 2)],
            RoomPlan.Make(room, Prototypes).Edges);

        var picture = (await session.RoomAsync(room, TestContext.Current.CancellationToken)).Paint(SKColors.Black);
        using var below = SKBitmap.FromImage(picture.Below);
        // The edge's yellow top rows, turned a quarter to the east square's west side, and half a turn to the north square's bottom.
        Assert.Equal((TestRooms.Yellow, TestRooms.Blue), (below.GetPixel(66, 48), below.GetPixel(90, 48)));
        Assert.Equal((TestRooms.Yellow, TestRooms.Blue), (below.GetPixel(48, 28), below.GetPixel(48, 4)));
        Assert.Equal(TestRooms.Purple, below.GetPixel(16, 80));
        Assert.Equal(TestRooms.Green, below.GetPixel(48, 48));
    }

    [Fact]
    public void A_tile_draws_the_frame_of_its_variant_turned_and_mirrored_as_its_byte_says()
    {
        using var strip = SKImage.FromEncodedData(TestRooms.Strip(TestRooms.Red, TestRooms.Green));
        using var halves = SKImage.FromEncodedData(TestRooms.Png(32, 32, (x, _) => x < 16 ? TestRooms.Blue : TestRooms.Yellow));
        var checker = new TilePicture("FloorChecker", strip, 2, false, null);
        var half = new TilePicture("FloorHalf", halves, 1, true, null);
        using var bitmap = new SKBitmap(128, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);

        // The second frame, and a variant past the strip, which takes its last frame.
        Assert.True(checker.Draw(canvas, new FloorTile("FloorChecker", 1, 0), 0, 0, 32, sampling, paint));
        Assert.True(checker.Draw(canvas, new FloorTile("FloorChecker", 7, 0), 32, 0, 32, sampling, paint));
        // Turned a quarter counter-clockwise, the left half is at the bottom; mirrored, on the right.
        Assert.True(half.Draw(canvas, new FloorTile("FloorHalf", 0, 1), 64, 0, 32, sampling, paint));
        Assert.True(half.Draw(canvas, new FloorTile("FloorHalf", 0, 4), 96, 0, 32, sampling, paint));
        // Without a picture nothing is drawn, and the caller draws a plate.
        Assert.False(new TilePicture("FloorLost", null, 1, false, null).Draw(canvas, new FloorTile("FloorLost", 0, 0), 0, 0, 32, sampling, paint));
        canvas.Flush();

        Assert.Equal((TestRooms.Green, TestRooms.Green), (bitmap.GetPixel(4, 4), bitmap.GetPixel(36, 4)));
        Assert.Equal((TestRooms.Yellow, TestRooms.Blue), (bitmap.GetPixel(80, 4), bitmap.GetPixel(80, 28)));
        Assert.Equal((TestRooms.Yellow, TestRooms.Blue), (bitmap.GetPixel(100, 16), bitmap.GetPixel(124, 16)));
    }

    [Fact]
    public void A_credit_list_names_files_in_a_list_one_name_comma_separated_or_by_wildcard()
    {
        static string? License(string yaml, string file) => TileCredits.Find(Encoding.UTF8.GetBytes(yaml), file)?.License;
        const string Lists = """
            - files: ["a.png", "b.png"]
              license: List
            - files: c.png
              license: One
            - files: ["d.png, e.png"]
              license: Commas
            - files: ["./f.png"]
              license: Here
            - files: ["*.png"]
              license: Rest
            """;

        Assert.Equal(["List", "One", "Commas", "Commas", "Here", "Rest"], new[] { "b.png", "c.png", "d.png", "e.png", "f.png", "z.png" }.Select(f => License(Lists, f)));
        Assert.Null(License(Lists, "z.jpg"));
        Assert.Null(License("- files: [unclosed", "a.png"));
    }

    [Fact]
    public async Task One_download_brings_every_file_the_room_draws_and_nothing_else()
    {
        var (session, store) = await ForkR();
        await using var _s = session;
        var ct = TestContext.Current.CancellationToken;
        var room = Parse(TestRooms.Room);

        var scene = await session.RoomAsync(room, ct);

        var fetched = Assert.Single(store.Fetches).ToHashSet();
        Assert.Equal(new[]
        {
            "Structures/crate.rsi/meta.json", "Structures/crate.rsi/crate.png", "Structures/crate.rsi/glow.png", "Structures/sticker.png",
            "Structures/attributions.yml", "Structures/railing.rsi/meta.json", "Structures/railing.rsi/railing.png", "Decals/paint.png", "Tiles/test.png",
            "Tiles/attribution.yml",
        }.Select(p => TestRooms.Textures + p).ToHashSet(), fetched);
        // Every picture drawn was downloaded, and each that was downloaded is drawn.
        var drawn = scene.Decals.Concat(scene.Entities.SelectMany(e => e.Items)).Select(i => TestRooms.Textures + i.Image)
            .Concat(scene.Tiles.Where(t => t.Picture.Strip != null).Select(_ => TestRooms.Textures + "Tiles/test.png"))
            .ToHashSet();
        Assert.Subset(fetched, drawn);
        Assert.Equal(fetched.Where(p => p.EndsWith(".png", StringComparison.Ordinal)).ToHashSet(), drawn);

        // Drawn once for this version of the fork.
        Assert.Same(scene, await session.RoomAsync(room, ct));
        Assert.Single(store.Fetches);
    }

    [Fact]
    public void Entities_and_decals_the_fork_lacks_are_left_out()
    {
        var plan = RoomPlan.Make(Parse(TestRooms.Room), Prototypes);

        Assert.Equal(["TestCrate", "TestRailing"], plan.Entities.Select(e => e.Proto));
        Assert.Equal("TestPaint", Assert.Single(plan.Decals).Decal.Id);
        // So is an entity whose sprite is hidden.
        Assert.Empty(RoomPlan.Make(Floor(1, 1, "- { proto: TestHidden, pos: \"0.5,0.5\" }"), Prototypes).Entities);
    }

    [Fact]
    public async Task The_room_paints_under_the_character_what_stands_behind_it_and_over_it_what_stands_in_front()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var scene = await session.RoomAsync(Parse(TestRooms.Room), TestContext.Current.CancellationToken);
        var plate = new SKColor(1, 2, 3);

        var picture = scene.Paint(plate);

        Assert.Equal((96, 96, 32, 32), (picture.Below.Width, picture.Below.Height, picture.StandLeft, picture.StandTop));
        using var below = SKBitmap.FromImage(picture.Below);
        using var above = SKBitmap.FromImage(picture.Above);
        // North of the stand, the floor; west, the crate with its glow (unshaded) and its green
        // sticker turned and shrunk; east, the white decal tinted orange; south-east, a tile
        // without a picture, as a plate.
        Assert.Equal(TestRooms.Blue, below.GetPixel(48, 8));
        Assert.Equal(TestRooms.Green, below.GetPixel(4, 60));
        Assert.Equal(TestRooms.White, below.GetPixel(1, 33));
        Assert.Equal(new SKColor(0, 255, 0), below.GetPixel(24, 40));
        Assert.Equal(new SKColor(255, 128, 0), below.GetPixel(80, 48));
        Assert.Equal(plate, below.GetPixel(80, 80));
        // The railing south of the stand reaches half a tile into it, over the character.
        Assert.Equal(TestRooms.Blue, below.GetPixel(48, 52));
        Assert.Equal(TestRooms.Yellow, above.GetPixel(48, 52));
        Assert.Equal(0, above.GetPixel(48, 44).Alpha);
        Assert.True(scene.OverCharacter(scene.Entities.Single(e => e.Proto == "TestRailing")));
        Assert.False(scene.OverCharacter(scene.Entities.Single(e => e.Proto == "TestCrate")));
        // Unshaded layers keep the flag, and each entity its prototype, place and turn.
        Assert.Equal([false, true, false], scene.Entities.Single(e => e.Proto == "TestCrate").Items.Select(i => i.Unshaded));
        Assert.Equal((new Vector2(0.5f, 1.5f), 0.0), scene.Entities.Select(e => (e.Position, e.Rotation)).First());
    }

    [Fact]
    public void Things_look_as_the_game_s_visualizers_leave_them_at_round_start()
    {
        var plan = RoomPlan.Make(Floor(8, 1, """
            - { proto: TestDoor, pos: "0.5,0.5" }
            - { proto: TestVend, pos: "1.5,0.5" }
            - { proto: TestJukebox, pos: "2.5,0.5" }
            - { proto: TestLamp2, pos: "3.5,0.5" }
            - { proto: TestLampEmpty, pos: "4.5,0.5" }
            - { proto: TestPanelOnly, pos: "5.5,0.5" }
            - { proto: TestLocker, pos: "6.5,0.5" }
            - { proto: TestLockerNoDoor, pos: "7.5,0.5" }
            """), Prototypes);
        string Look(string proto) => string.Join(" ", plan.Entities.Single(e => e.Proto == proto).Layers.Select(l => l.Visible ? l.State : "-"));

        // A closed, powered door: closed, its unlit layer in the airlock's closed state; not welded,
        // bolted, on emergency access, open for maintenance or sparking. Closed doors draw at the doors' depth.
        Assert.Equal("closed closed_unlit - - - - -", Look("TestDoor"));
        Assert.Equal(DrawDepths.ByName["Doors"], plan.Entities.Single(e => e.Proto == "TestDoor").Sprite.DrawDepth);
        // A powered vending machine: off under the normal state; a screen state it lacks leaves that layer alone; panel shut.
        Assert.Equal("off normal-unshaded - -", Look("TestVend"));
        Assert.Equal("on", Look("TestJukebox"));
        // A lamp with a bulb is on and glows; without one it is empty and dark.
        Assert.Equal("lit glow", Look("TestLamp2"));
        Assert.Equal("empty -", Look("TestLampEmpty"));
        // A closed locker: its own base and door states; without a door state, no door.
        Assert.Equal("sec sec_door", Look("TestLocker"));
        Assert.Equal("generic -", Look("TestLockerNoDoor"));
        // Without the component that drives it, a layer stays as written.
        Assert.Equal("panel", Look("TestPanelOnly"));
    }

    [Fact]
    public async Task A_sprite_however_big_draws_no_more_than_a_few_tiles_past_the_room()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var scene = await session.RoomAsync(Floor(2, 1, "- { proto: TestGiant, pos: \"0.5,0.5\" }"), TestContext.Current.CancellationToken);

        Assert.Equal(new Box(-4, -4, 6, 5), scene.Extent());
        var picture = scene.Paint(SKColors.Black);
        Assert.Equal((10 * 32, 9 * 32), (picture.Below.Width, picture.Below.Height));
    }

    [Fact]
    public async Task The_preview_moves_only_for_what_moves_facing_one_of_its_four_ways()
    {
        var (session, _) = await ForkR();
        await using var _s = session;

        session.PreviewJob = "Sculptor";
        Assert.False(session.IsAnimated());
        session.PreviewJob = "Clerk";
        Assert.True(session.IsAnimated());
    }

    [Fact]
    public async Task The_credits_name_each_picture_drawn_within_a_region()
    {
        var (session, _) = await ForkR();
        await using var _s = session;
        var scene = await session.RoomAsync(Parse(TestRooms.Room), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new CreditLine("Decals/paint.png", null, "no credit listed"),
                new CreditLine("Structures/crate.rsi", "CC-BY-SA-3.0", "Crate made up"),
                new CreditLine("Structures/railing.rsi", "CC-BY-SA-3.0", "Railing made up"),
                new CreditLine("Structures/sticker.png", "CC0-1.0", "Sticker made up"),
                new CreditLine("Tiles/test.png", "CC0-1.0", "Tile made up"),
            ],
            scene.CreditsWithin(scene.Extent()));
        // The north-west square shows only the floor.
        Assert.Equal(["Tiles/test.png"], scene.CreditsWithin(new Box(0, 2, 1, 3)).Select(c => c.Rsi));
    }

    [Fact]
    public async Task A_download_that_fails_or_times_out_throws_and_draws_nothing()
    {
        var (session, store) = await ForkR();
        await using var _s = session;
        var ct = TestContext.Current.CancellationToken;
        var room = Parse(TestRooms.Room);

        store.FetchFails = new HttpRequestException("offline");
        Assert.Equal("offline", (await Assert.ThrowsAsync<HttpRequestException>(() => session.RoomAsync(room, ct))).Message);
        store.FetchFails = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        Assert.Contains("HttpClient.Timeout", (await Assert.ThrowsAsync<IOException>(() => session.RoomAsync(room, ct))).Message);

        store.FetchFails = null;
        Assert.Equal(2, (await session.RoomAsync(room, ct)).Entities.Count);
    }
}
