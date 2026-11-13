// The edge sprites follow RobustToolbox (Robust.Client/Graphics/Clyde/Clyde.GridRendering.cs,
// _updateChunkEdges, and Robust.Client/Map/ClydeTileDefinitionManager.cs), Copyright (c) 2019 Space
// Station 14 Contributors, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Numerics;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Lighting;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rooms;

/// <summary>A decal as the room draws it: the room's decal and its prototype's picture, an RSI state's first frame or a loose texture.</summary>
public sealed record PlannedDecal(RoomDecal Decal, string? Rsi, string? State, string? Texture);

/// <summary>An edge sprite a tile throws on a neighbour: the square it falls on, its picture, and the turn the game gives it.</summary>
public sealed record PlannedEdge(int X, int Y, string Texture, double Angle);

/// <summary>
/// What a room draws, worked out from the fork's prototypes before any picture is read: the
/// entities with their sprite layers, the decals and tile edges, and so every file to download;
/// and what lights it. Entities and decals the fork lacks are left out.
/// </summary>
public sealed class RoomPlan
{
    public required Room Room { get; init; }

    /// <summary>The entities the fork has, in the room file's order, with their layers.</summary>
    public required IReadOnlyList<EntityVisual> Entities { get; init; }

    public required IReadOnlyList<PlannedDecal> Decals { get; init; }

    public required IReadOnlyList<PlannedEdge> Edges { get; init; }

    /// <summary>The tile prototypes the room's squares name.</summary>
    public IEnumerable<string> TileIds => Room.Tiles.Values.Distinct(StringComparer.Ordinal);

    /// <summary>The RSI files to read, relative to <c>Textures/</c>: each folder's meta.json and each state's PNG.</summary>
    public required IReadOnlySet<string> RsiFiles { get; init; }

    /// <summary>The loose pictures to read, relative to <c>Textures/</c>: texture layers, decal textures and edge sprites.</summary>
    public required IReadOnlySet<string> Pictures { get; init; }

    /// <summary>The lights that shine at round start, from every entity, whether or not it draws anything.</summary>
    public IReadOnlyList<LightSource> Lights { get; init; } = [];

    /// <summary>What casts shadows, from every entity, whether or not it draws anything.</summary>
    public Occluders Occluders { get; init; } = new();

    /// <summary>Why the room's lights could not be read, or null.</summary>
    public string? LightProblem { get; init; }

    /// <summary>
    /// The squares a station's roof covers: those with a tile not open to space (a tile prototype's
    /// <c>isSpace</c>, <c>Content.Shared/Maps/ContentTileDefinition.cs</c>); the rest take the map's ambient light.
    /// </summary>
    public IReadOnlySet<(int X, int Y)> Roofed { get; init; } = new HashSet<(int X, int Y)>();

    public static RoomPlan Make(Room room, PrototypeIndex prototypes)
    {
        var entities = new List<EntityVisual>();
        var prototypesOf = new Dictionary<EntityVisual, YamlMappingNode>();
        for (var i = 0; i < room.Entities.Count; i++)
        {
            var placed = room.Entities[i];
            if (prototypes.Resolve("entity", placed.Proto) is not { } proto
                || SpriteInfo.Read(PrototypeYaml.Component(proto, "Sprite")) is not { Visible: true } sprite)
                continue;
            var smooth = PrototypeYaml.Component(proto, "IconSmooth");
            var visual = new EntityVisual
            {
                Index = i,
                Proto = placed.Proto,
                Position = new Vector2(placed.X, placed.Y),
                Rotation = placed.Rotation,
                Anchored = PrototypeYaml.Bool(PrototypeYaml.Component(proto, "Transform"), "anchored", false),
                Sprite = sprite with { DrawDepth = RoundStartLook.DrawDepth(proto, sprite.DrawDepth) },
                // Content.Client/IconSmoothing/IconSmoothComponent.cs: base defaults to "", mode to Corners, enabled to true.
                Smooth = smooth == null ? null : new SmoothFacts(
                    PrototypeYaml.Scalar(smooth, "key"),
                    PrototypeYaml.Scalar(smooth, "base") ?? "",
                    PrototypeYaml.Scalar(smooth, "mode") ?? "Corners",
                    PrototypeYaml.Strings(smooth, "additionalKeys"),
                    int.TryParse(PrototypeYaml.Scalar(smooth, "index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null,
                    PrototypeYaml.Bool(smooth, "enabled", true)),
            };
            entities.Add(visual);
            prototypesOf[visual] = proto;
        }
        var anchored = entities.Where(e => e.Anchored).ToLookup(e => e.Tile);
        foreach (var entity in entities)
        {
            entity.Layers = EntityComposer.Layers(entity, (x, y) => anchored[(x, y)]);
            // As the game's visualizers leave them at round start; IconSmooth's corner layers have no map key.
            RoundStartLook.Apply(prototypesOf[entity], entity.Layers, prototypes);
        }

        var decals = new List<PlannedDecal>();
        foreach (var decal in room.Decals)
        {
            // DecalPrototype.Sprite is a SpriteSpecifier: {sprite, state} for an RSI state, or a
            // texture path (SpriteSpecifierSerializer).
            if (prototypes.Resolve("decal", decal.Id) is not { } proto || !proto.Children.TryGetValue(new YamlScalarNode("sprite"), out var sprite))
                continue;
            if (sprite is YamlMappingNode rsi && PrototypeYaml.Scalar(rsi, "sprite") is { } folder && PrototypeYaml.Scalar(rsi, "state") is { } state)
                decals.Add(new PlannedDecal(decal, CharacterCatalog.TexturePath(folder), state, null));
            else if (sprite is YamlScalarNode { Value: { Length: > 0 } texture })
                decals.Add(new PlannedDecal(decal, null, null, CharacterCatalog.TexturePath(texture)));
        }

        var edges = EdgesOf(room, prototypes);

        var rsiFiles = new HashSet<string>(StringComparer.Ordinal);
        var pictures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in entities.SelectMany(e => e.Layers).Where(l => l.Visible))
        {
            if (layer is { State: { } state, Rsi: { } folder })
            {
                rsiFiles.Add(folder + "/meta.json");
                rsiFiles.Add(EntityComposer.StatePath(folder, state));
            }
            else if (layer is { State: null, Texture: { } texture })
                pictures.Add(texture);
        }
        foreach (var decal in decals)
        {
            if (decal is { Rsi: { } folder, State: { } state })
            {
                rsiFiles.Add(folder + "/meta.json");
                rsiFiles.Add(EntityComposer.StatePath(folder, state));
            }
            else if (decal.Texture is { } texture)
                pictures.Add(texture);
        }
        pictures.UnionWith(edges.Select(e => e.Texture));

        var occluders = new Occluders();
        IReadOnlyList<LightSource> lights = [];
        string? lightProblem = null;
        try
        {
            occluders = Occluders.Build(room, prototypes);
            lights = LightSources.Collect(room, prototypes, occluders, new Vector2(room.Stand.X + 0.5f, room.Stand.Y + 0.5f));
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            lightProblem = e.Message;
        }
        var roofed = room.Tiles.Where(t => !PrototypeYaml.Bool(prototypes.Resolve("tile", t.Value), "isSpace", false)).Select(t => t.Key).ToHashSet();

        return new RoomPlan
        {
            Room = room, Entities = entities, Decals = decals, Edges = edges, RsiFiles = rsiFiles, Pictures = pictures,
            Lights = lights, Occluders = occluders, LightProblem = lightProblem, Roofed = roofed,
        };
    }

    // Clyde.GridRendering.cs, _updateChunkEdges: every different neighbour of higher edge priority
    // with edge sprites throws, on this tile, its edge sprite for the direction from it to here.
    private static List<PlannedEdge> EdgesOf(Room room, PrototypeIndex prototypes)
    {
        var tiles = room.Tiles.Values.Distinct(StringComparer.Ordinal).ToDictionary(id => id, id =>
        {
            var node = prototypes.Resolve("tile", id);
            var edges = PrototypeYaml.Mapping(node, "edgeSprites")?.Children
                .Where(kv => kv.Key is YamlScalarNode { Value: not null } && kv.Value is YamlScalarNode { Value: { Length: > 0 } })
                .ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => CharacterCatalog.TexturePath(((YamlScalarNode)kv.Value).Value!), StringComparer.Ordinal);
            var priority = int.TryParse(PrototypeYaml.Scalar(node, "edgeSpritePriority"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
            return (Edges: edges ?? [], Priority: priority);
        }, StringComparer.Ordinal);

        var result = new List<PlannedEdge>();
        foreach (var ((x, y), id) in room.Tiles.OrderBy(t => t.Key.X).ThenBy(t => t.Key.Y))
        {
            for (var nx = -1; nx <= 1; nx++)
            {
                for (var ny = -1; ny <= 1; ny++)
                {
                    if ((nx == 0 && ny == 0) || room.TileAt(x + nx, y + ny) is not { } neighbourId || neighbourId == id)
                        continue;
                    var neighbour = tiles[neighbourId];
                    if (neighbour.Edges.Count == 0 || neighbour.Priority <= tiles[id].Priority)
                        continue;
                    var (direction, angle) = EdgeFrom(-nx, -ny);
                    if (neighbour.Edges.TryGetValue(direction, out var texture))
                        result.Add(new PlannedEdge(x, y, texture, angle));
                }
            }
        }
        return result;
    }

    // The direction of a step and the angle ClydeTileDefinitionManager turns its edge sprite by,
    // counter-clockwise, when it builds the tile atlas.
    private static (string Direction, double Angle) EdgeFrom(int dx, int dy) => (dx, dy) switch
    {
        (0, 1) => ("North", Math.PI),
        (1, 1) => ("NorthEast", Math.PI / 2),
        (1, 0) => ("East", Math.PI / 2),
        (1, -1) => ("SouthEast", 0),
        (0, -1) => ("South", 0),
        (-1, -1) => ("SouthWest", Math.PI * 1.5),
        (-1, 0) => ("West", Math.PI * 1.5),
        _ => ("NorthWest", Math.PI),
    };
}
