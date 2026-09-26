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
public sealed record RsiState(string Name, int Directions, IReadOnlyList<int> FramesPerDirection);

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
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
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
            if (state.TryGetProperty("delays", out var delays))
            {
                var i = 0;
                foreach (var list in delays.EnumerateArray())
                {
                    if (i < directions)
                        frames[i] = Math.Max(1, list.GetArrayLength());
                    i++;
                }
            }
            states[name] = new RsiState(name, directions, frames);
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
