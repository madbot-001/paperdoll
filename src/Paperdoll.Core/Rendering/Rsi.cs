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

    /// <summary>The frame showing after this many seconds, the animation looping as the game plays it.</summary>
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

    public static RsiMeta Parse(byte[] json)
    {
        // Some forks save meta.json with a UTF-8 byte order mark, which the JSON reader refuses.
        var start = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;
        using var doc = JsonDocument.Parse(json.AsMemory(start), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var size = root.GetProperty("size");

        var states = new Dictionary<string, RsiState>(StringComparer.Ordinal);
        foreach (var state in root.GetProperty("states").EnumerateArray())
        {
            var name = state.GetProperty("name").GetString()!;
            var directions = state.TryGetProperty("directions", out var d) ? d.GetInt32() : 1;
            var frames = new int[directions];
            for (var i = 0; i < directions; i++)
                frames[i] = 1;
            var delayLists = new List<IReadOnlyList<float>>();
            if (state.TryGetProperty("delays", out var delays))
            {
                var i = 0;
                foreach (var list in delays.EnumerateArray())
                {
                    if (i < directions)
                        frames[i] = Math.Max(1, list.GetArrayLength());
                    delayLists.Add(list.EnumerateArray().Select(d => d.ValueKind == JsonValueKind.Number ? d.GetSingle() : 0f).ToList());
                    i++;
                }
            }
            states[name] = new RsiState(name, directions, frames) { Delays = delayLists };
        }

        return new RsiMeta
        {
            FrameWidth = size.GetProperty("x").GetInt32(),
            FrameHeight = size.GetProperty("y").GetInt32(),
            License = root.TryGetProperty("license", out var license) ? license.GetString() : null,
            Copyright = root.TryGetProperty("copyright", out var copyright) ? copyright.GetString() : null,
            States = states,
        };
    }

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
