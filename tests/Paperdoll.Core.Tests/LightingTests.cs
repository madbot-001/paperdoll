using System.Diagnostics;
using System.Numerics;
using System.Text;
using Paperdoll.Core.Lighting;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Rooms;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>The game's lighting on a demo room, with the made-up lights and occluders of fork "r".</summary>
public class LightingTests
{
    private static readonly PrototypeIndex Prototypes = PrototypeIndex.Load([new PrototypeSource("room.yml", Encoding.UTF8.GetBytes(TestRooms.Prototypes))]);

    // A floor of FloorTest, the character on its south-west square, with these entities.
    private static Room Floor(int width, int height, string entities) =>
        Room.TryParse($"""
            id: test
            stand: "0,0"
            legend:
              f: FloorTest
            tiles:
            {string.Concat(Enumerable.Repeat($"- \"{new string('f', width)}\"\n", height))}
            entities:
            {entities}
            """, out var problem) ?? throw new InvalidDataException(problem);

    // The room's light over its squares and two more around.
    private static LightField Light(Room room)
    {
        var plan = RoomPlan.Make(room, Prototypes);
        return LightField.Compute(new Box(-2, -2, room.Width + 2, room.Height + 2), plan.Lights, plan.Occluders, (x, y) => !plan.Roofed.Contains((x, y)), TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<LightSource> Lights(Room room) => RoomPlan.Make(room, Prototypes).Lights;

    [Theory]
    [InlineData(0f, 10f, 0f, 0.583393f)]
    [InlineData(3f, 10f, 0f, 0.257114f)]
    [InlineData(3f, 10f, 1f, 0.482143f)]
    [InlineData(9.95f, 10f, 0f, 0f)]
    [InlineData(0f, 1f, 0f, 0f)]
    public void A_light_falls_off_as_the_engine_s_shader_has_it(float distance, float radius, float curve, float expected) =>
        Assert.Equal(expected, LightField.Attenuation(distance, radius, 6.8f, curve), 5);

    [Fact]
    public void A_lamp_lights_with_its_bulb_s_colour_made_linear_times_its_energy_at_a_known_distance()
    {
        var field = Light(Floor(8, 3, "- { proto: TestLamp2, pos: \"0.5,1.5\" }"));

        // The bulb: #FF8000, energy 2, radius 6. Three tiles away, 2 × att(3, 6) = 0.2276;
        // green is FromSrgb(128 / 255) = 0.2158 of that, blue none.
        var (r, g, b) = field.LinearAt(3.5, 1.5);
        Assert.Equal(0.2276, r, 2);
        Assert.Equal(0.0491, g, 2);
        Assert.Equal(0, b, 3);
        // Brighter nearer the lamp; nothing past its reach, √(6² − 1) = 5.9 tiles, short of where
        // the starlight comes in at the room's edge.
        Assert.True(field.LinearAt(1.5, 1.5).R > r);
        Assert.True(field.LinearAt(6.6, 1.5).R < 0.001);
        Assert.True(field.LinearAt(7.9, 1.5).B > 0.02);
    }

    [Fact]
    public void A_fixture_shines_with_its_bulb_s_colour_radius_and_energy_rather_than_its_own()
    {
        var light = Assert.Single(Lights(Floor(1, 1, "- { proto: TestLamp2, pos: \"0.5,0.5\" }")));
        Assert.Equal((Rgba.Parse("#FF8000"), 6f, 2f), (light.Color, light.Radius, light.Energy));
        Assert.Equal((6.8f, 0f, true), (light.Falloff, light.CurveFactor, light.CastShadows));
    }

    [Theory]
    [InlineData("TestTorchOff")]
    [InlineData("TestLampEmpty")]
    [InlineData("TestLampOff")]
    [InlineData("TestLampBroken")]
    [InlineData("TestEmergencyLight")]
    public void A_light_that_is_off_at_round_start_gives_no_light(string proto) =>
        Assert.Empty(Lights(Floor(1, 1, $"- {{ proto: {proto}, pos: \"0.5,0.5\" }}")));

    [Theory]
    [InlineData("TestBlock", true)]
    [InlineData("TestClosedDoor", true)]
    [InlineData("TestOpenDoor", false)]
    [InlineData("TestRailing", false)]
    public void A_wall_or_closed_door_between_lamp_and_floor_casts_a_shadow_and_an_open_door_or_a_window_does_not(string between, bool shadow)
    {
        var field = Light(Floor(6, 3, $$"""
            - { proto: TestLamp2, pos: "0.5,1.5" }
            - { proto: {{between}}, pos: "2.5,1.5" }
            """));

        // 2 × att(4, 6) = 0.098 in the open.
        Assert.Equal(shadow ? 0 : 0.098, field.LinearAt(4.5, 1.5).R, 2);
    }

    [Fact]
    public void A_light_that_casts_no_shadows_shines_through_walls_and_a_shadow_caster_inside_one_is_not_drawn()
    {
        var through = Light(Floor(6, 3, """
            - { proto: TestFlatLight, pos: "0.5,1.5" }
            - { proto: TestBlock, pos: "2.5,1.5" }
            """));
        Assert.Equal(0.0679, through.LinearAt(3.5, 1.5).R, 3);

        // In the engine, "Shadow-casting lights embedded inside an occluder cannot work consistently".
        Assert.Empty(Lights(Floor(2, 1, """
            - { proto: TestTorch, pos: "0.5,0.5" }
            - { proto: TestBlock, pos: "0.5,0.5" }
            """)));
        Assert.Single(Lights(Floor(2, 1, """
            - { proto: TestFlatLight, pos: "0.5,0.5" }
            - { proto: TestBlock, pos: "0.5,0.5" }
            """)));
    }

    [Fact]
    public void A_wall_is_lit_by_the_light_bled_onto_it_rather_than_left_black()
    {
        var field = Light(Floor(5, 3, """
            - { proto: TestLamp2, pos: "1.5,1.5" }
            - { proto: TestBlock, pos: "2.5,1.5" }
            """));

        // The wall's own square is past the face that meets the light, so in its shadow; the
        // wall bleed blurs the light in front into it, a little brighter for its 1.1⁶.
        var wall = field.LinearAt(2.5, 1.5).R;
        var front = field.LinearAt(1.9, 1.5).R;
        Assert.InRange(wall, 0.2 * front, front);
        // Behind it, nothing reaches.
        Assert.Equal(0, field.LinearAt(3.6, 1.5).R, 2);
    }

    [Fact]
    public void A_masked_light_shines_only_within_its_cones_turned_with_the_entity()
    {
        var cones = Assert.Single(Lights(Floor(1, 1, "- { proto: TestConeLight, pos: \"0.5,0.5\" }"))).Cones;
        // Direction 0 is south: full within 30°, half at 45°, none past 60°.
        Assert.Equal(1, LightSources.ConeFactor(cones, 0, 0, -1));
        Assert.Equal(0.5f, LightSources.ConeFactor(cones, 0, 1, -1), 3);
        Assert.Equal(0, LightSources.ConeFactor(cones, 0, 0, 1));

        // autoRot: an entity turned a quarter counter-clockwise points its cone east.
        var turned = Assert.Single(Lights(Floor(1, 1, "- { proto: TestConeLight, pos: \"0.5,0.5\", rot: 90 }")));
        Assert.Equal(Math.PI / 2, turned.MaskRotation, 6);
        Assert.Equal(1, LightSources.ConeFactor(turned.Cones, turned.MaskRotation, 1, 0));
        Assert.Equal(0, LightSources.ConeFactor(turned.Cones, turned.MaskRotation, 0, -1));
    }

    [Fact]
    public void Past_128_shadow_casting_lights_those_farthest_from_the_character_are_dropped()
    {
        var entities = string.Concat(Enumerable.Range(0, 130).Select(i => $"- {{ proto: TestTorch, pos: \"{i % 32 + 0.5},{i / 32 + 0.5}\" }}\n"));
        var lights = Lights(Floor(32, 5, entities));

        Assert.Equal(LightSources.MaxShadowCasters, lights.Count);
        // The character stands on 0,0; the two farthest lights are the east ends of the two rows above.
        Assert.DoesNotContain(lights, l => l.Position == new Vector2(31.5f, 3.5f) || l.Position == new Vector2(31.5f, 2.5f));
        Assert.Contains(lights, l => l.Position == new Vector2(30.5f, 3.5f));
    }

    [Fact]
    public void Colours_go_from_sRGB_to_linear_light_and_back_as_the_engine_converts_them()
    {
        Assert.Equal(0.21404f, LightField.FromSrgb(0.5f), 5);
        Assert.Equal(0.5f, LightField.ToSrgb(0.21404f), 4);
        Assert.Equal(0.02f / 12.92f, LightField.FromSrgb(0.02f), 6);
        for (var v = 0; v <= 255; v++)
            Assert.Equal(v, (int)MathF.Round(LightField.ToSrgb(LightField.FromSrgb(v / 255f)) * 255));
    }

    [Fact]
    public void Squares_open_to_space_get_the_starlight_and_those_under_the_roof_none()
    {
        var space = LightField.Compute(new Box(0, 0, 8, 8), [], new Occluders(), (_, _) => true, TestContext.Current.CancellationToken);
        var roofed = LightField.Compute(new Box(0, 0, 8, 8), [], new Occluders(), (_, _) => false, TestContext.Current.CancellationToken);

        // light.space_light_color #8487db, made linear by SpaceLightSystem and again by the clear.
        var (r, g, b) = space.LinearAt(4, 4);
        Assert.Equal((0.04350, 0.04784, 0.46001), (Math.Round(r, 5), Math.Round(g, 5), Math.Round(b, 5)));
        Assert.Equal((0f, 0f, 0f), roofed.LinearAt(4, 4));

        // A white picture under the starlight: each channel times the light in linear light, back to sRGB.
        using var picture = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        picture.SetPixel(0, 0, SKColors.White);
        picture.SetPixel(1, 0, new SKColor(255, 255, 255, 128));
        space.LightPicture(picture, 4, 4, 0.01, 0.01);
        Assert.Equal(new SKColor(59, 62, 181), picture.GetPixel(0, 0));
        // Half covered: the same colour, at the same alpha.
        var half = picture.GetPixel(1, 0);
        Assert.Equal(128, half.Alpha);
        Assert.InRange(half.Blue, 179, 183);
    }

    [Fact]
    public void Light_over_the_floor_is_brighter_near_the_lamp_and_unshaded_layers_keep_their_colour()
    {
        var room = Floor(8, 3, """
            - { proto: TestTorch, pos: "0.5,1.5" }
            - { proto: TestCrate, pos: "6.5,1.5" }
            """);
        var scene = Scene(room);
        Assert.NotNull(scene.Light);
        using var lit = Pixels(scene.Paint(SKColors.White, lit: true));
        using var flat = Pixels(scene.Paint(SKColors.White));

        // The picture is the room's squares, 32 pixels each, row 0 at the north edge.
        SKColor At(SKBitmap bitmap, float x, float y) => bitmap.GetPixel((int)(x * 32), (int)((3 - y) * 32));
        Assert.Equal(TestRooms.Blue, At(flat, 1.5f, 0.5f));
        var near = At(lit, 1.5f, 0.5f).Blue;
        var far = At(lit, 4.5f, 0.5f).Blue;
        Assert.True(near > far && far > 0 && near < 255, $"near {near}, far {far}");
        // The crate's glow, its top-left corner, is unshaded: as bright as ever this far from the lamp;
        // the crate itself is dimmed.
        Assert.Equal(TestRooms.White, At(lit, 6.05f, 1.95f));
        Assert.True(At(lit, 6.5f, 1.5f).Green < 64);
    }

    [Fact]
    public void A_room_with_no_light_that_shines_has_no_light_and_is_drawn_flat_lit_either_way()
    {
        var scene = Scene(Floor(2, 1, "- { proto: TestTorchOff, pos: \"0.5,0.5\" }"));
        Assert.Null(scene.Light);
        Assert.Null(scene.LightProblem);
        var picture = scene.Paint(SKColors.White, lit: true);
        Assert.Null(picture.Light);
        using var pixels = Pixels(picture);
        Assert.Equal(TestRooms.Blue, pixels.GetPixel(8, 8));
    }

    [Fact]
    public void The_character_is_lit_where_it_stands()
    {
        var scene = Scene(Floor(3, 1, "- { proto: TestTorch, pos: \"2.5,0.5\" }"));
        var picture = scene.Paint(SKColors.White, lit: true);
        using var sprite = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        sprite.Erase(SKColors.White);

        using var lit = picture.LightCharacter(sprite, 1, 1);
        // Two tiles from the light, its west edge further than its east edge.
        var west = lit.GetPixel(1, 16).Red;
        var east = lit.GetPixel(30, 16).Red;
        Assert.True(west < east && east < 255, $"west {west}, east {east}");
        var expected = (int)MathF.Round(LightField.ToSrgb(LightField.Attenuation(2f, 5f)) * 255);
        Assert.InRange(lit.GetPixel(16, 16).Red, expected - 2, expected + 2);

        // Without a light the copy is the sprite.
        using var flat = scene.Paint(SKColors.White).LightCharacter(sprite, 1, 1);
        Assert.Equal(SKColors.White, flat.GetPixel(1, 16));
    }

    [Fact]
    public void A_door_that_starts_open_shows_its_open_state_at_its_open_depth_and_casts_no_shadow()
    {
        var plan = RoomPlan.Make(Floor(2, 1, """
            - { proto: TestOpenDoor, pos: "0.5,0.5" }
            - { proto: TestClosedDoor, pos: "1.5,0.5" }
            """), Prototypes);
        string Look(string proto) => string.Join(" ", plan.Entities.Single(e => e.Proto == proto).Layers.Select(l => l.Visible ? l.State : "-"));

        // Open: Door.openSpriteState, and the airlock's unlit layer in its open state (openUnlitVisible).
        Assert.Equal("ajar open_unlit - - - - -", Look("TestOpenDoor"));
        Assert.Equal(DrawDepths.ByName["Overdoors"], plan.Entities.Single(e => e.Proto == "TestOpenDoor").Sprite.DrawDepth);
        Assert.Equal("closed closed_unlit - - - - -", Look("TestClosedDoor"));
        Assert.Equal(DrawDepths.ByName["Doors"], plan.Entities.Single(e => e.Proto == "TestClosedDoor").Sprite.DrawDepth);
        Assert.Equal(1, plan.Occluders.Count);
        Assert.False(plan.Occluders.Contains(new Vector2(0.5f, 0.5f)));
        Assert.True(plan.Occluders.Contains(new Vector2(1.5f, 0.5f)));
    }

    [Fact]
    public void A_lit_fixture_s_glow_takes_its_bulb_s_colour()
    {
        var plan = RoomPlan.Make(Floor(2, 1, """
            - { proto: TestLamp2, pos: "0.5,0.5" }
            - { proto: TestLampOff, pos: "1.5,0.5" }
            """), Prototypes);
        LayerSpec Glow(string proto) => plan.Entities.Single(e => e.Proto == proto).Layers.Single(l => l.MapKeys.Contains("enum.PoweredLightLayers.Glow"));

        Assert.Equal((true, Rgba.Parse("#FF8000")), (Glow("TestLamp2").Visible, Glow("TestLamp2").Color));
        // Off, the glow is hidden, in the fixture's own light colour.
        Assert.Equal((false, Rgba.Parse("#0000FF")), (Glow("TestLampOff").Visible, Glow("TestLampOff").Color));
    }

    [Fact]
    public void The_largest_room_with_the_most_lights_is_lit_in_well_under_a_second()
    {
        // 32 by 32 tiles and as many entities as a room may hold: 512 lights reaching across the
        // room, half of them casting shadows, and 512 walls.
        var entities = new StringBuilder();
        string[] kinds = ["TestFloodLight", "TestFloodCaster", "TestBlock", "TestBlock"];
        for (var i = 0; i < Room.MaxThings; i++)
            entities.Append($"- {{ proto: {kinds[i % 4]}, pos: \"{i % 32 + 0.5},{i / 32 + 0.5}\" }}\n");
        var room = Floor(Room.MaxSize, Room.MaxSize, entities.ToString());

        var clock = Stopwatch.StartNew();
        var plan = RoomPlan.Make(room, Prototypes);
        var bounds = new Box(-RoomScene.PictureMargin, -RoomScene.PictureMargin, Room.MaxSize + RoomScene.PictureMargin, Room.MaxSize + RoomScene.PictureMargin);
        var field = LightField.Compute(bounds, plan.Lights, plan.Occluders, (x, y) => !plan.Roofed.Contains((x, y)), TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(LightSources.MaxShadowCasters, plan.Lights.Count(l => l.CastShadows));
        // Fewer samples a tile keep the work within bounds: every light reaches all 40 × 40 tiles.
        Assert.True(field.SamplesPerTile < LightField.MaxSamplesPerTile);
        Assert.True((double)field.SamplesPerTile * field.SamplesPerTile * plan.Lights.Count * 40 * 40 <= LightField.MaxWork);
        Assert.True(clock.ElapsedMilliseconds < 1500, $"{clock.ElapsedMilliseconds} ms at {field.SamplesPerTile} a tile");
    }

    // The doll lit three tiles from a lamp: dim, but not black.
    private static SKBitmap LitInTheDark(SKBitmap doll)
    {
        var picture = Scene(Floor(4, 1, "- { proto: TestTorch, pos: \"3.5,0.5\" }")).Paint(SKColors.White, lit: true);
        Assert.NotNull(picture.Light);
        return picture.LightCharacter(doll, 1, 1);
    }

    [Fact]
    public void An_unshaded_layer_of_a_worn_item_keeps_its_colour_in_the_dark_while_the_rest_of_the_doll_is_dimmed()
    {
        using var doll = TestRooms.Wearing("GlowVest");
        using var lit = LitInTheDark(doll);

        Assert.Equal(TestRooms.VestStrip, lit.GetPixel(16, 12));
        Assert.Equal(TestRooms.VestStrip, lit.GetPixel(9, 13));
        Assert.InRange(lit.GetPixel(16, 20).Green, 1, 120);
        Assert.InRange(lit.GetPixel(2, 2).Red, 1, 120);
    }

    [Fact]
    public void A_shaded_layer_drawn_over_an_unshaded_one_covers_it_and_is_dimmed()
    {
        using var lit = LitInTheDark(TestRooms.Wearing("PatchedVest"));

        var patch = lit.GetPixel(9, 12);
        Assert.Equal((0, 0), (patch.Red - patch.Green, patch.Alpha - 255));
        Assert.InRange(patch.Blue, 1, 120);
        Assert.Equal(TestRooms.VestStrip, lit.GetPixel(16, 12));
    }

    [Fact]
    public void Unshaded_layers_change_no_pixel_of_the_doll_as_drawn_without_lighting()
    {
        using var glowing = TestRooms.Wearing("GlowVest");
        using var plain = TestRooms.Wearing("PlainVest");

        Assert.NotNull(Assert.IsType<CharacterBitmap>(glowing).Unshaded);
        Assert.Null(Assert.IsType<CharacterBitmap>(plain).Unshaded);
        Assert.Equal(plain.Pixels, glowing.Pixels);
        Assert.Equal(TestRooms.VestStrip, glowing.GetPixel(16, 12));

        // A room drawn without lighting leaves the doll as it is.
        using var flat = Scene(Floor(4, 1, "- { proto: TestTorch, pos: \"3.5,0.5\" }")).Paint(SKColors.White).LightCharacter(glowing, 1, 1);
        Assert.Equal(plain.Pixels, flat.Pixels);
    }

    private static RoomScene Scene(Room room)
    {
        var plan = RoomPlan.Make(room, Prototypes);
        var blue = TestRooms.Solid(32, 32, TestRooms.Blue);
        var crate = TestRooms.Png(32, 32, (_, _) => TestRooms.Green);
        var glow = TestRooms.Png(32, 32, (x, y) => x < 4 && y < 4 ? TestRooms.White : SKColors.Transparent);
        var files = new Dictionary<string, byte[]>
        {
            ["Structures/crate.rsi/meta.json"] = Encoding.UTF8.GetBytes(TestRooms.Meta("Crate made up", ("crate", 1), ("glow", 1))),
            ["Structures/crate.rsi/crate.png"] = crate,
            ["Structures/crate.rsi/glow.png"] = glow,
        };
        using var tile = SKBitmap.Decode(blue);
        var tileImage = SKImage.FromBitmap(tile);
        return RoomScene.Build(plan, "test", path => files.GetValueOrDefault(path), _ => null,
            id => new TilePicture(id, tileImage, 1, false, null));
    }

    // Both pictures of a room, the one over the character on the one under it.
    private static SKBitmap Pixels(RoomPicture picture)
    {
        var bitmap = new SKBitmap(picture.Below.Width, picture.Below.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawImage(picture.Below, 0, 0);
        canvas.DrawImage(picture.Above, 0, 0);
        return bitmap;
    }
}
