// Ported from RobustToolbox (Robust.Client/GameObjects/EntitySystems/ClientOccluderSystem.cs, the
// shared-edge rule; Robust.Shared/Physics/Systems/FixtureSystem.Shapes.cs, TestPoint), Copyright (c)
// 2019 Space Station 14 Contributors, and Space Station 14 (Content.Shared/Doors/Systems/SharedDoorSystem.cs,
// the door occluder rule), Copyright (c) 2017-2026 Space Wizards Federation; both MIT licence.
// See THIRD-PARTY-NOTICES.md.

using System.Numerics;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Rooms;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Lighting;

/// <summary>
/// An edge of the occluders' outline, from <see cref="A"/> to <see cref="B"/> with the occluder on
/// its left (counter-clockwise), so its outward normal is <c>(dy, -dx)</c>.
/// </summary>
internal readonly record struct Edge(Vector2 A, Vector2 B);

/// <summary>
/// What blocks light in a room at round start: every enabled <c>Occluder</c> polygon, placed and
/// turned with its entity. Walls, closed opaque doors and bookshelves occlude; windows, glass
/// airlocks and open doors do not.
/// </summary>
/// <remarks>
/// A door with <c>occludes: true</c> (the default, <c>Content.Shared/Doors/Components/DoorComponent.cs</c>)
/// has its occluder on exactly when it is collidable, whatever its <c>Occluder.enabled</c> says:
/// when it starts closed (<c>SharedDoorSystem.OnComponentInit</c> and <c>SetCollidable</c>). Other
/// entities use <c>Occluder.enabled</c> (<c>RobustToolbox/Robust.Shared/GameObjects/Components/Light/OccluderComponent.cs</c>).
/// The polygon is the component's <c>polygon</c>, the convex hull of its points, or the unit square;
/// an older engine's <c>boundingBox</c> ("left,bottom,right,top") is read too.
/// </remarks>
public sealed class Occluders
{
    // Keys of edge end points, in thousandths of a tile: ClientOccluderSystem matches shared edges to 0.001.
    private const float KeyScale = 1000f;

    // A prototype's polygon wider than this many tiles is taken for a mistake and left out.
    private const float MaxSize = 16;

    private readonly List<Vector2[]> _polygons = [];
    private readonly Dictionary<(int X, int Y), List<int>> _byCell = [];
    private Edge[]? _edges;

    /// <summary>How many occluders there are.</summary>
    public int Count => _polygons.Count;

    /// <summary>
    /// The outline of the union of the polygons: every polygon edge that does not run, reversed,
    /// along an edge of another (a wall's side against its neighbour's). For a light outside the
    /// occluders this casts the shadow the engine's shared-edge suppression aims at
    /// (<c>Robust.Client/Graphics/Clyde/Clyde.LightRendering.cs</c>, <c>ShouldSuppressSharedOccluderEdge</c>);
    /// the seams at wall corners that depend on where the eye is are not modelled.
    /// </summary>
    internal Edge[] Edges => _edges ??= BoundaryEdges();

    /// <summary>The room's occluders, each entity's prototype read from the fork's prototypes.</summary>
    public static Occluders Build(Room room, PrototypeIndex prototypes)
    {
        var occluders = new Occluders();
        foreach (var placed in room.Entities)
        {
            if (prototypes.Resolve("entity", placed.Proto) is not { } entity
                || PrototypeYaml.Component(entity, "Occluder") is not { } occluder || !IsEnabled(entity, occluder))
                continue;
            occluders.Add(Polygon(occluder, new Vector2(placed.X, placed.Y), placed.Rotation));
        }
        return occluders;
    }

    // A door that occludes has its occluder on while it is closed; anything else as its Occluder says.
    private static bool IsEnabled(YamlMappingNode entity, YamlMappingNode occluder)
    {
        var door = PrototypeYaml.Component(entity, "Door");
        if (door != null && PrototypeYaml.Bool(door, "occludes", true))
            return !RoundStartLook.DoorOpen(entity);
        return PrototypeYaml.Bool(occluder, "enabled", true);
    }

    /// <summary>
    /// True when the point is inside an occluder or on its edge, as the physics <c>TestPoint</c> the
    /// engine uses to drop shadow-casting lights embedded in one (<c>Clyde.LightRendering.cs</c>, <c>IsLightEmbeddedInOccluder</c>).
    /// </summary>
    public bool Contains(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
            || !_byCell.TryGetValue(((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)), out var list))
            return false;
        foreach (var index in list)
        {
            if (Inside(_polygons[index], point))
                return true;
        }
        return false;
    }

    // The polygon in room tiles, counter-clockwise, turned by the entity's rotation and moved to it.
    private static Vector2[] Polygon(YamlMappingNode occluder, Vector2 position, double rotation)
    {
        var local = new List<Vector2>();
        foreach (var point in PrototypeYaml.Strings(occluder, "polygon"))
        {
            if (PrototypeYaml.Vector(point) is { } v && float.IsFinite(v.X) && float.IsFinite(v.Y))
                local.Add(new Vector2(v.X, v.Y));
        }
        if (local.Count == 0 && PrototypeYaml.Scalar(occluder, "boundingBox")?.Split(',') is { Length: 4 } box
            && box.Select(p => PrototypeYaml.Float(p.Trim())).ToList() is [{ } l, { } b, { } r, { } t])
            local.AddRange([new(l, b), new(r, b), new(r, t), new(l, t)]);
        var hull = Hull(local);
        // PhysicsHullSerializer refuses fewer than three points off one line; the default square stands.
        if (hull.Count < 3)
            hull = [new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f)];

        var (sin, cos) = Math.SinCos(rotation);
        return hull.Select(p => new Vector2(
            (float)(position.X + p.X * cos - p.Y * sin),
            (float)(position.Y + p.X * sin + p.Y * cos))).ToArray();
    }

    // The convex hull, counter-clockwise, without points on its edges (Andrew's monotone chain).
    private static List<Vector2> Hull(List<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
            return sorted;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Vector2>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    private void Add(Vector2[] polygon)
    {
        var (minX, maxX) = (polygon.Min(p => p.X), polygon.Max(p => p.X));
        var (minY, maxY) = (polygon.Min(p => p.Y), polygon.Max(p => p.Y));
        if (!float.IsFinite(minX + maxX + minY + maxY) || maxX - minX > MaxSize || maxY - minY > MaxSize || Math.Abs(SignedArea(polygon)) < 1e-6f)
            return;
        var index = _polygons.Count;
        _polygons.Add(polygon);
        for (var x = (int)MathF.Floor(minX); x <= (int)MathF.Floor(maxX); x++)
        {
            for (var y = (int)MathF.Floor(minY); y <= (int)MathF.Floor(maxY); y++)
            {
                if (!_byCell.TryGetValue((x, y), out var list))
                    _byCell[(x, y)] = list = [];
                list.Add(index);
            }
        }
    }

    private static float SignedArea(Vector2[] points)
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2;
    }

    // Counter-clockwise: inside is to the left of every edge; on an edge counts as inside.
    private static bool Inside(Vector2[] polygon, Vector2 point)
    {
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            if ((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X) < -1e-6f)
                return false;
        }
        return true;
    }

    private Edge[] BoundaryEdges()
    {
        static (long, long) Key(Vector2 p) => ((long)Math.Round(p.X * (double)KeyScale), (long)Math.Round(p.Y * (double)KeyScale));

        var directed = new HashSet<((long, long) From, (long, long) To)>();
        foreach (var polygon in _polygons)
        {
            for (var i = 0; i < polygon.Length; i++)
                directed.Add((Key(polygon[i]), Key(polygon[(i + 1) % polygon.Length])));
        }

        var edges = new List<Edge>();
        foreach (var polygon in _polygons)
        {
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                if (!directed.Contains((Key(b), Key(a))))
                    edges.Add(new Edge(a, b));
            }
        }
        return edges.ToArray();
    }
}
