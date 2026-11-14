using System.Globalization;
using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rooms;

/// <summary>A thing placed in a room: its entity prototype, where it stands in tiles (a tile's centre is at .5), and its rotation in radians, counter-clockwise.</summary>
public sealed record RoomEntity(string Proto, float X, float Y, double Rotation);

/// <summary>A floor marking: its decal prototype, its bottom-left corner in tiles, colour, rotation in radians and z-index.</summary>
public sealed record RoomDecal(string Id, float X, float Y, Rgba Color, double Angle, int Z);

/// <summary>
/// A small hand-made room the preview can stand the character in. A room is a YAML file:
/// <code>
/// id: bar
/// name: Bar
/// departments: [ Civilian ]     # whose jobs it suits, by department prototype id
/// jobs: [ Bartender ]           # and by job id, which wins over a department
/// jobsIfNoDepartment: [ Mime ]  # job ids it takes only when no room names the job's departments
/// stand: "4,2"                  # the tile the character stands on, x east and y north
/// legend:                       # a character of the rows below for each tile prototype
///   w: FloorWood
/// tiles:                        # rows from north to south; a space or "." is no tile
/// - "wwww"
/// entities:                     # pos in tiles, a tile's centre at .5; rot in degrees, counter-clockwise
/// - { proto: WallSolid, pos: "0.5,0.5", rot: 0 }
/// decals:                       # pos is the bottom-left corner; angle in degrees; z orders them
/// - { id: Bot, pos: "1,1", color: "#FFFFFFFF", angle: 0, z: 0 }
/// </code>
/// The south-west tile is 0,0. Prototype ids name what the game has; one the fork lacks is skipped
/// when drawn. A room is at most <see cref="MaxSize"/> tiles a side, with at most
/// <see cref="MaxThings"/> entities and as many decals, none more than <see cref="Margin"/> tiles
/// outside its tiles.
/// </summary>
public sealed class Room
{
    /// <summary>The most tiles a room has across or down, so its picture stays small.</summary>
    public const int MaxSize = 32;

    /// <summary>How far past the tiles, in tiles, an entity or decal may stand: enough for a light on an outer wall.</summary>
    public const int Margin = 2;

    /// <summary>The most entities, and the most decals, a room has.</summary>
    public const int MaxThings = 1024;

    public required string Id { get; init; }

    /// <summary>The file's text, which tells a changed room from the same one read again.</summary>
    public required string Text { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Departments { get; init; } = [];
    public IReadOnlyList<string> Jobs { get; init; } = [];

    /// <summary>Jobs the room suits only when none of the job's departments has a room.</summary>
    public IReadOnlyList<string> JobsIfNoDepartment { get; init; } = [];
    public required (int X, int Y) Stand { get; init; }

    /// <summary>The tile prototype on each square that has one, by square.</summary>
    public required IReadOnlyDictionary<(int X, int Y), string> Tiles { get; init; }

    /// <summary>Tiles across and down.</summary>
    public required int Width { get; init; }
    public required int Height { get; init; }

    public IReadOnlyList<RoomEntity> Entities { get; init; } = [];
    public IReadOnlyList<RoomDecal> Decals { get; init; } = [];

    /// <summary>The tile on a square, or null for none.</summary>
    public string? TileAt(int x, int y) => Tiles.GetValueOrDefault((x, y));

    /// <summary>
    /// Reads a room file. Returns null with the reason when it is not a room Paperdoll can read; it
    /// never throws for a broken file.
    /// </summary>
    public static Room? TryParse(string text, out string? problem)
    {
        try
        {
            problem = null;
            return Parse(text);
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException or InvalidOperationException or ArgumentException)
        {
            problem = e.Message;
            return null;
        }
    }

    private static Room Parse(string text)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException("the file is not one mapping of a room's fields");

        var id = Required(root, "id");
        var legend = Map(root, "legend") ?? throw new FormatException("it has no legend");
        var tileIds = new Dictionary<char, string>();
        foreach (var (key, value) in legend.Children)
        {
            if (key is not YamlScalarNode { Value: { Length: 1 } symbol } || value is not YamlScalarNode { Value: { Length: > 0 } tile })
                throw new FormatException($"legend entry '{key}' is not one character naming a tile");
            if (symbol is " " or ".")
                throw new FormatException("legend entry ' ' or '.' would hide 'no tile'");
            tileIds[symbol[0]] = tile;
        }

        var rows = Strings(root, "tiles");
        if (rows.Count == 0)
            throw new FormatException("it has no tiles");
        var height = rows.Count;
        var width = rows.Max(r => r.Length);
        if (width > MaxSize || height > MaxSize)
            throw new FormatException($"the room is {width} by {height} tiles; it may be at most {MaxSize} by {MaxSize}");
        var tiles = new Dictionary<(int, int), string>();
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                var symbol = rows[row][column];
                if (symbol is ' ' or '.')
                    continue;
                // Rows go north to south; y counts north from the bottom row.
                tiles[(column, height - 1 - row)] = tileIds.GetValueOrDefault(symbol)
                    ?? throw new FormatException($"tile row {row + 1} has '{symbol}', which the legend does not name");
            }
        }

        var standText = Required(root, "stand");
        var stand = PrototypeYaml.Vector(standText) is { } s && s.X == MathF.Floor(s.X) && s.Y == MathF.Floor(s.Y)
            ? ((int)s.X, (int)s.Y)
            : throw new FormatException($"stand '{standText}' is not a tile's \"x,y\"");
        if (!tiles.ContainsKey(stand))
            throw new FormatException($"stand {standText} is not on a tile of the room");

        var entities = new List<RoomEntity>();
        foreach (var (entry, number) in Entries(root, "entities"))
        {
            var proto = Required(entry, "proto", $"entity {number}");
            var (x, y) = Position(entry, "pos", $"entity {number} ({proto})", width, height);
            entities.Add(new RoomEntity(proto, x, y, Degrees(entry, "rot", $"entity {number} ({proto})")));
        }

        var decals = new List<RoomDecal>();
        foreach (var (entry, number) in Entries(root, "decals"))
        {
            var decal = Required(entry, "id", $"decal {number}");
            var what = $"decal {number} ({decal})";
            var (x, y) = Position(entry, "pos", what, width, height);
            var colorText = PrototypeYaml.Scalar(entry, "color");
            var color = colorText == null ? Rgba.White
                : Rgba.TryParse(colorText, out var parsed) ? parsed : throw new FormatException($"{what} has colour '{colorText}'");
            var zText = PrototypeYaml.Scalar(entry, "z");
            var z = zText == null ? 0
                : int.TryParse(zText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zValue) ? zValue : throw new FormatException($"{what} has z '{zText}'");
            decals.Add(new RoomDecal(decal, x, y, color, Degrees(entry, "angle", what), z));
        }

        return new Room
        {
            Id = id,
            Text = text,
            Name = PrototypeYaml.Scalar(root, "name") ?? id,
            Departments = Strings(root, "departments"),
            Jobs = Strings(root, "jobs"),
            JobsIfNoDepartment = Strings(root, "jobsIfNoDepartment"),
            Stand = stand,
            Tiles = tiles,
            Width = width,
            Height = height,
            Entities = entities,
            Decals = decals,
        };
    }

    private static string Required(YamlMappingNode node, string key, string? what = null) =>
        PrototypeYaml.Scalar(node, key) is { Length: > 0 } value ? value : throw new FormatException($"{what ?? "it"} has no {key}");

    private static YamlMappingNode? Map(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlMappingNode ?? throw new FormatException($"{key} is not a mapping") : null;

    // A list of text; a missing or empty list is empty.
    private static IReadOnlyList<string> Strings(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is YamlScalarNode { Value: "" })
            return [];
        if (value is not YamlSequenceNode list || list.Children.Any(c => c is not YamlScalarNode))
            throw new FormatException($"{key} is not a list of text");
        return list.Children.Cast<YamlScalarNode>().Select(s => s.Value ?? "").ToList();
    }

    // The entries of a list, at most MaxThings of them.
    private static IEnumerable<(YamlMappingNode Entry, int Number)> Entries(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is YamlScalarNode { Value: "" })
            yield break;
        if (value is not YamlSequenceNode list)
            throw new FormatException($"{key} is not a list");
        if (list.Children.Count > MaxThings)
            throw new FormatException($"it has {list.Children.Count} {key}; it may have at most {MaxThings}");
        var number = 0;
        foreach (var child in list.Children)
        {
            number++;
            yield return (child as YamlMappingNode ?? throw new FormatException($"{key} entry {number} is not a mapping"), number);
        }
    }

    // A position within the room's tiles and the margin around them.
    private static (float X, float Y) Position(YamlMappingNode entry, string key, string what, int width, int height)
    {
        if (PrototypeYaml.Vector(Required(entry, key, what)) is not { } pos || !float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
            throw new FormatException($"{what} has {key} '{PrototypeYaml.Scalar(entry, key)}', not \"x,y\"");
        if (pos.X < -Margin || pos.Y < -Margin || pos.X > width + Margin || pos.Y > height + Margin)
            throw new FormatException($"{what} is at {PrototypeYaml.Scalar(entry, key)}, more than {Margin} tiles outside the room");
        return pos;
    }

    // Degrees, counter-clockwise; none is 0.
    private static double Degrees(YamlMappingNode entry, string key, string what)
    {
        var text = PrototypeYaml.Scalar(entry, key);
        if (text == null)
            return 0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees) && double.IsFinite(degrees)
            ? degrees * Math.PI / 180
            : throw new FormatException($"{what} has {key} '{text}', not a number of degrees");
    }
}
