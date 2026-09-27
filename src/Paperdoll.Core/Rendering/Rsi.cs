using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace Paperdoll.Core.Rendering;

/// <summary>Facing directions, numbered as RSI files order them.</summary>
public enum Direction
{
    South = 0,
    North = 1,
    East = 2,
    West = 3,
}

/// <summary>One state of an RSI: its name, how many directions it has and frames per direction.</summary>
public sealed record RsiState(string Name, int Directions, IReadOnlyList<int> FramesPerDirection)
{
    /// <summary>How long each frame shows, in seconds, per direction (the meta's <c>delays</c>).</summary>
    public IReadOnlyList<IReadOnlyList<float>> Delays { get; init; } = [];

    /// <summary>Whether the state plays more than one frame facing this way.</summary>
    public bool IsAnimated(Direction direction) => DelaysFor(direction) is { Count: > 1 };

    /// <summary>Frame index at a given time; animations loop.</summary>
    public int FrameAt(Direction direction, double seconds)
    {
        if (DelaysFor(direction) is not { Count: > 1 } delays)
            return 0;
        var total = delays.Sum(d => (double)d);
        if (total <= 0)
            return 0;
        var time = seconds % total;
        for (var i = 0; i < delays.Count; i++)
        {
            time -= delays[i];
            if (time < 0)
                return i;
        }
        return delays.Count - 1;
    }

    private IReadOnlyList<float>? DelaysFor(Direction direction)
    {
        var dir = (int)direction < Directions ? (int)direction : 0;
        return dir < Delays.Count ? Delays[dir] : null;
    }
}

/// <summary>
/// An RSI folder's <c>meta.json</c>: frame size, licence, credit and states. Each state is a PNG
/// holding its frames left to right, top to bottom, all of one direction before the next
/// (South, North, East, West, then the diagonals).
/// </summary>
public sealed class RsiMeta
{
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required string? License { get; init; }
    public required string? Copyright { get; init; }
    public required IReadOnlyDictionary<string, RsiState> States { get; init; }

    /// <summary>
    /// Reads a meta.json as leniently as the engine does (web JSON rules: property names in any
    /// case, numbers written as text), and refuses sizes and direction counts it refuses.
    /// </summary>
    public static RsiMeta Parse(byte[] json)
    {
        // Some forks save meta.json with a UTF-8 byte order mark, which the JSON reader refuses.
        var start = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;
        using var doc = JsonDocument.Parse(json.AsMemory(start), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var size = Get(root, "size") ?? throw new FormatException("meta.json has no size.");
        var width = Int(Get(size, "x")) ?? 0;
        var height = Int(Get(size, "y")) ?? 0;
        if (width <= 0 || height <= 0)
            throw new FormatException($"meta.json gives a frame size of {width} by {height}.");

        var states = new Dictionary<string, RsiState>(StringComparer.Ordinal);
        foreach (var state in Get(root, "states") is { ValueKind: JsonValueKind.Array } list ? list.EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            if (Get(state, "name")?.GetString() is not { } name)
                continue;
            var directions = Int(Get(state, "directions")) ?? 1;
            if (directions is not (1 or 4 or 8))
                throw new FormatException($"State {name} has {directions} directions.");
            var frames = new int[directions];
            for (var i = 0; i < directions; i++)
                frames[i] = 1;
            var delayLists = new List<IReadOnlyList<float>>();
            if (Get(state, "delays") is { ValueKind: JsonValueKind.Array } delays)
            {
                var i = 0;
                foreach (var direction in delays.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Array))
                {
                    if (i < directions)
                        frames[i] = Math.Max(1, direction.GetArrayLength());
                    delayLists.Add(direction.EnumerateArray().Select(d => Float(d) ?? 0f).ToList());
                    i++;
                }
            }
            states[name] = new RsiState(name, directions, frames) { Delays = delayLists };
        }

        return new RsiMeta
        {
            FrameWidth = width,
            FrameHeight = height,
            License = Get(root, "license")?.GetString(),
            Copyright = Get(root, "copyright")?.GetString(),
            States = states,
        };
    }

    // A property by name in any case, as the engine's web-style JSON reading finds it.
    private static JsonElement? Get(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
        }
        return null;
    }

    // A number, or a number written as text.
    private static int? Int(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i,
        { ValueKind: JsonValueKind.String } t when int.TryParse(t.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };

    private static float? Float(JsonElement value) => value switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetSingle(),
        { ValueKind: JsonValueKind.String } t when float.TryParse(t.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) => f,
        _ => null,
    };

    /// <summary>
    /// Cuts one frame out of a state's PNG. A state with fewer directions than asked for (a
    /// one-direction state) gives its first. Null if the image cannot be read.
    /// </summary>
    public SKBitmap? Frame(RsiState state, byte[] png, Direction direction = Direction.South, int frame = 0)
    {
        using var image = SKBitmap.Decode(png);
        if (image == null)
            return null;

        var dir = (int)direction < state.Directions ? (int)direction : 0;
        var index = 0;
        for (var i = 0; i < dir; i++)
            index += state.FramesPerDirection[i];
        index += Math.Min(frame, state.FramesPerDirection[dir] - 1);

        var columns = Math.Max(1, image.Width / FrameWidth);
        var x = index % columns * FrameWidth;
        var y = index / columns * FrameHeight;

        var result = new SKBitmap(new SKImageInfo(FrameWidth, FrameHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var converted = image.Copy(SKColorType.Rgba8888);
        for (var py = 0; py < FrameHeight; py++)
        {
            for (var px = 0; px < FrameWidth; px++)
            {
                var sx = x + px;
                var sy = y + py;
                result.SetPixel(px, py, sx < converted.Width && sy < converted.Height ? converted.GetPixel(sx, sy) : SKColors.Transparent);
            }
        }
        return result;
    }
}
