// TilePicture.Draw's turning is ported from RobustToolbox (Robust.Client/Graphics/Clyde/Clyde.GridRendering.cs,
// WriteTileToBuffers, written 2025), Copyright (c) 2019 Space Station 14 Contributors, MIT licence.
// See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Editing;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rooms;

/// <summary>A tile as drawn: its tile prototype, and the variant and rotation-and-mirror bytes the game stores with it.</summary>
public readonly record struct FloorTile(string Id, byte Variant, byte Rotation);

/// <summary>
/// A tile prototype as the floor draws it: its picture, a strip of <see cref="Variants"/> 32-pixel
/// frames (null when the fork has none to give), and its credit.
/// </summary>
/// <param name="AllowRotationMirror">Whether the tile's rotation-and-mirror byte applies (<c>Content.Shared/Maps/ContentTileDefinition.cs</c>).</param>
public sealed record TilePicture(string Id, SKImage? Strip, int Variants, bool AllowRotationMirror, CreditLine? Credit)
{
    private const int Frame = 32;

    /// <summary>
    /// Draws the tile into the square of <paramref name="size"/> pixels whose top-left corner is at
    /// (<paramref name="x"/>, <paramref name="y"/>). False when there is no picture to draw, where
    /// the caller draws a plain plate.
    /// </summary>
    public bool Draw(SKCanvas canvas, FloorTile tile, float x, float y, float size, SKSamplingOptions sampling, SKPaint paint)
    {
        if (Strip is not { } strip)
            return false;
        // The variant picks a frame of the strip; one past the strip takes its last.
        var frames = Math.Max(1, Math.Min(Variants, strip.Width / Frame));
        var source = SKRect.Create(Math.Clamp((int)tile.Variant, 0, frames - 1) * Frame, 0, Frame, Frame);
        canvas.Save();
        canvas.Translate(x + size / 2f, y + size / 2f);
        if (AllowRotationMirror && tile.Rotation != 0)
            TurnAndMirror(canvas, tile.Rotation);
        canvas.DrawImage(strip, source, SKRect.Create(-size / 2f, -size / 2f, size, size), sampling, paint);
        canvas.Restore();
        return true;
    }

    // The tile's rotation-and-mirror byte, as Clyde.GridRendering.cs applies it: the picture is
    // mirrored first (left to right for 4 and 6, top to bottom for 5 and 7), then turned a quarter
    // counter-clockwise per step of the byte modulo 4. Skia applies the last transform first.
    private static void TurnAndMirror(SKCanvas canvas, byte rotation)
    {
        canvas.RotateDegrees(-90 * (rotation % 4));
        if (rotation >= 4)
            canvas.Scale(rotation % 2 == 0 ? -1 : 1, rotation % 2 == 0 ? 1 : -1);
    }
}

/// <summary>The credits of the tile pictures, from the <c>attributions.yml</c> (or <c>attribution.yml</c>) beside them.</summary>
public static class TileCredits
{
    /// <summary>
    /// A file's licence and authors from its folder's credit list (a list of
    /// <c>{files, license, copyright, source}</c>); null when the file is not listed or the list
    /// cannot be read. Files may be a list or one name, a name may hold several split by commas,
    /// and a name may have wildcards ("*.png").
    /// </summary>
    public static (string? License, string? Copyright)? Find(byte[]? attributions, string fileName)
    {
        if (attributions == null)
            return null;
        try
        {
            using var reader = new StreamReader(new MemoryStream(attributions), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var yaml = new YamlStream();
            yaml.Load(reader);
            foreach (var entry in yaml.Documents.Select(d => d.RootNode).OfType<YamlSequenceNode>().SelectMany(s => s.Children).OfType<YamlMappingNode>())
            {
                if (entry.Children.TryGetValue(new YamlScalarNode("files"), out var files) && Names(files).Any(name => Matches(name, fileName)))
                    return (Prototypes.PrototypeIndex.Scalar(entry, "license"), Prototypes.PrototypeIndex.Scalar(entry, "copyright"));
            }
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // An attributions file that cannot be read credits nothing.
        }
        return null;
    }

    // ["a.png", "b.png"], "a.png", or ["a.png, b.png"]
    private static IEnumerable<string> Names(YamlNode files) =>
        (files is YamlSequenceNode list ? list.Children.OfType<YamlScalarNode>() : files is YamlScalarNode one ? [one] : [])
            .SelectMany(s => (s.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private static bool Matches(string name, string fileName)
    {
        name = name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;
        return name.Contains('*') || name.Contains('?')
            ? System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(name, fileName, ignoreCase: false)
            : name == fileName;
    }
}
