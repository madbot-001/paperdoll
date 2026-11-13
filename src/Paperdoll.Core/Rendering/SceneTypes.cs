using System.Numerics;
using Paperdoll.Core.Editing;
using SkiaSharp;

namespace Paperdoll.Core.Rendering;

/// <summary>An axis-aligned box in tiles, +y north.</summary>
public readonly record struct Box(float MinX, float MinY, float MaxX, float MaxY)
{
    public static readonly Box Empty = new(float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public Box Union(Box other) => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public bool Intersects(Box other) => MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;

    /// <summary>The box around a w × h rectangle carried by a matrix.</summary>
    public static Box Around(Matrix3x2 transform, float width, float height)
    {
        var a = Vector2.Transform(new Vector2(0, 0), transform);
        var b = Vector2.Transform(new Vector2(width, 0), transform);
        var c = Vector2.Transform(new Vector2(0, height), transform);
        var d = Vector2.Transform(new Vector2(width, height), transform);
        return new Box(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
    }
}

/// <summary>
/// One picture to draw: a frame of a PNG, carried from its pixels (y down, origin at its top-left)
/// into the world (tiles, +y north) by <see cref="Transform"/>.
/// </summary>
public sealed class DrawItem
{
    /// <summary>The PNG drawn from, relative to <c>Textures/</c>: an RSI state's or a loose texture.</summary>
    public required string Image { get; init; }

    /// <summary>The frame's size in pixels.</summary>
    public required int Width { get; init; }
    public required int Height { get; init; }

    public required Matrix3x2 Transform { get; init; }

    /// <summary>What the picture is multiplied by, alpha included; null for none.</summary>
    public Rgba? Tint { get; init; }

    /// <summary>For an RSI frame: the meta, state and direction to find the frame's rectangle by.</summary>
    public RsiMeta? Meta { get; init; }
    public RsiState? State { get; init; }
    public Direction Direction { get; init; }

    /// <summary>Made from a layer with <c>shader: unshaded</c>: lit whatever the light.</summary>
    public bool Unshaded { get; init; }

    /// <summary>Who made the picture, and its licence.</summary>
    public required CreditLine Credit { get; init; }

    /// <summary>The frame's rectangle in its PNG.</summary>
    public SKRectI SourceIn(SKImage sheet) =>
        Meta != null && State != null ? Meta.FrameRect(State, sheet.Width, Direction) : SKRectI.Create(sheet.Width, sheet.Height);

    public Box Bounds => Box.Around(Transform, Width, Height);
}

/// <summary>
/// An <c>IconSmooth</c> component (<c>Content.Client/IconSmoothing/IconSmoothComponent.cs</c>): the
/// key it joins by, the extra keys it also joins (one way), the state prefix, the mode, where its
/// corner layers go (null: on top) and whether it is on.
/// </summary>
public sealed record SmoothFacts(string? Key, string Base, string Mode, IReadOnlyList<string> AdditionalKeys, int? Index, bool Enabled)
{
    /// <summary>
    /// The component's <c>states</c> in the form the game uses since October 2026, each smoothing
    /// its own layers; null for the older form of <c>base</c> and <c>mode</c>, which forks still use.
    /// Empty when there is nothing to smooth or a state is of a kind not known here.
    /// </summary>
    public IReadOnlyList<SmoothState>? States { get; init; }

    /// <summary><c>IconSmoothSystem.MatchingEntity</c>: the other's key is ours or one of our extra keys, and it is on.</summary>
    public bool JoinsWith(SmoothFacts other) => other.Enabled && (other.Key == Key || AdditionalKeys.Contains(other.Key ?? ""));
}

/// <summary>The <c>ISpriteSmoothState</c> kinds, one per file in <c>Content.Client/IconSmoothing/Smoothings/</c>.</summary>
public enum SmoothKind
{
    Corner,
    CardinalCorner,
    Diagonal,
    AltDiagonal,
    Direction,
    Edge,
}

/// <summary>
/// One entry of <c>IconSmooth.states</c> (<c>Content.Client/IconSmoothing/ISpriteSmoothState.cs</c>):
/// the state prefix, the keys it joins (<c>mask</c>) and, for the kinds that have one, the keys
/// that count otherwise (<c>altMask</c>), the map key its layers are named by, where its layers go
/// (null: on top) and their shader.
/// </summary>
public sealed record SmoothState(SmoothKind Kind, string Base, IReadOnlySet<string> Mask, IReadOnlySet<string> AltMask, string LayerKey, int? Index, string? Shader);

/// <summary>A sprite layer while the visualizers change it.</summary>
public sealed class LayerSpec
{
    public LayerSpec()
    {
    }

    public LayerSpec(SpriteLayerInfo info)
    {
        Rsi = info.Rsi;
        State = info.State;
        Texture = info.Texture;
        Color = info.Color ?? Rgba.White;
        Visible = info.Visible;
        OffsetX = info.OffsetX;
        OffsetY = info.OffsetY;
        ScaleX = info.ScaleX;
        ScaleY = info.ScaleY;
        Rotation = info.Rotation;
        Shader = info.Shader;
        MapKeys = info.MapKeys;
    }

    public string? Rsi;
    public string? State;
    public string? Texture;
    public Rgba Color = Rgba.White;
    public bool Visible = true;
    public float OffsetX, OffsetY, Rotation;
    public float ScaleX = 1, ScaleY = 1;
    public string? Shader;
    public IReadOnlyList<string> MapKeys = [];
    public DirOffset DirOffset;
}

/// <summary>
/// One placed entity: its prototype, where it stands and how it is turned, and the pictures it
/// draws in the game's order.
/// </summary>
public sealed class EntityVisual
{
    /// <summary>Its place in the room file: the last tie-break of the draw order, as the uid is in the game.</summary>
    public required int Index { get; init; }

    public required string Proto { get; init; }

    /// <summary>Where it stands, in tiles; a tile's centre is at .5.</summary>
    public required Vector2 Position { get; init; }

    /// <summary>Rotation, radians counter-clockwise.</summary>
    public required double Rotation { get; init; }

    /// <summary>The prototype's <c>Transform.anchored</c>: only anchored entities smooth with neighbours.</summary>
    public required bool Anchored { get; init; }

    public required SpriteInfo Sprite { get; init; }

    public SmoothFacts? Smooth { get; init; }

    public (int X, int Y) Tile => ((int)Math.Floor(Position.X), (int)Math.Floor(Position.Y));

    /// <summary>The sprite's layers after the visualizers, bottom to top.</summary>
    public IReadOnlyList<LayerSpec> Layers { get; set; } = [];

    // Filled by the composer.
    public IReadOnlyList<DrawItem> Items { get; set; } = [];
    public int DrawDepth { get; set; }
    public Box Bounds { get; set; } = Box.Empty;

    /// <summary>
    /// The engine's y-sort key: the screen y of the sprite's lower edge, so things further south
    /// draw over things north of them at the same depth.
    /// </summary>
    public float SortY { get; set; }

    /// <summary>
    /// The game's draw order (<c>RobustToolbox/Robust.Client/Graphics/Clyde/Clyde.Sprite.cs</c>,
    /// <c>SpriteSortItem.CompareTo</c>): draw depth, then render order (0 for every prototype
    /// here), then y-sort, then uid, here the place in the room file.
    /// </summary>
    public static int DrawOrder(EntityVisual a, EntityVisual b) => Order(a.DrawDepth, a.SortY, a.Index, b.DrawDepth, b.SortY, b.Index);

    public static int Order(int depthA, float sortA, int indexA, int depthB, float sortB, int indexB)
    {
        var cmp = depthA.CompareTo(depthB);
        if (cmp != 0)
            return cmp;
        cmp = sortA.CompareTo(sortB);
        return cmp != 0 ? cmp : indexA.CompareTo(indexB);
    }
}
