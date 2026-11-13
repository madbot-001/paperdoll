// Ported from RobustToolbox (Robust.Shared.Maths/Angle.cs, Robust.Client/Utility/DirExt.cs,
// Robust.Client/GameObjects/Components/Renderable/SpriteComponent.cs), Copyright (c) 2019 Space
// Station 14 Contributors, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Numerics;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// The engine's arithmetic for drawing an entity's sprite: which RSI direction a layer shows at an
/// angle, how a layer's frame is turned back upright, and the matrices that carry a frame's pixels
/// into the world. Angles are radians, counter-clockwise, with 0 facing south, as in the engine;
/// world positions are in tiles, +y north.
/// </summary>
public static class SpriteMath
{
    private const double Tau = 2 * Math.PI;
    private const double Quarter = Math.PI / 2;

    /// <summary><c>CVars.RenderSpriteDirectionBias</c>, as <c>SpriteComponent.cs</c> reads it.</summary>
    private const double DirectionBias = -0.05;

    /// <summary>Pixels per tile: sprites and tiles are drawn at 32 pixels a tile (<c>EyeManager.PixelsPerMeter</c>).</summary>
    public const float PixelsPerTile = 32f;

    /// <summary>The same angle in [0, 2π), as <c>Angle.Reduced().FlipPositive()</c> gives it.</summary>
    public static double Positive(double theta)
    {
        var t = theta % Tau;
        return t < 0 ? t + Tau : t;
    }

    /// <summary>
    /// The direction a state with this many directions shows at an angle in [0, 2π)
    /// (<c>SpriteComponent.Layer.GetDirection</c>). Four-direction states lean a little towards
    /// the direction they had, so diagonal movers do not flicker.
    /// </summary>
    public static Direction ForState(int directions, double angle)
    {
        if (directions == 8)
            return EightWay(angle);
        if (directions != 4)
            return Direction.South;
        var mod = Math.Floor(angle / Quarter) % 2 - 0.5;
        var modTheta = angle + mod * DirectionBias;
        return ((int)Math.Round(modTheta / Quarter) % 4) switch
        {
            0 => Direction.South,
            1 => Direction.East,
            2 => Direction.North,
            _ => Direction.West,
        };
    }

    // Angle.GetDir gives the engine's Direction (South, SouthEast, East... counting
    // counter-clockwise); DirExt.Convert names the same direction in RSI order.
    private static Direction EightWay(double angle)
    {
        var engineDir = (int)(Math.Floor((Positive(angle) + Math.PI / 8) / (Math.PI / 4)) % 8);
        return engineDir switch
        {
            0 => Direction.South,
            1 => Direction.SouthEast,
            2 => Direction.East,
            3 => Direction.NorthEast,
            4 => Direction.North,
            5 => Direction.NorthWest,
            6 => Direction.West,
            _ => Direction.SouthWest,
        };
    }

    /// <summary>The angle a direction faces (<c>Direction.ToAngle</c>): South 0, East π/2, North π, West 3π/2.</summary>
    public static double AngleOf(Direction direction) => direction switch
    {
        Direction.South => 0,
        Direction.SouthEast => Math.PI / 4,
        Direction.East => Quarter,
        Direction.NorthEast => 3 * Math.PI / 4,
        Direction.North => Math.PI,
        Direction.NorthWest => 5 * Math.PI / 4,
        Direction.West => 3 * Quarter,
        _ => 7 * Math.PI / 4,
    };

    /// <summary><c>Angle.RoundToCardinalAngle</c>: the nearest multiple of π/2.</summary>
    public static double RoundToCardinal(double angle) => Quarter * Math.Floor((angle + Quarter / 2) / Quarter);

    /// <summary><c>Angle.GetCardinalDir</c> as a quarter-turn count: 0 South, 1 East, 2 North, 3 West.</summary>
    public static int CardinalQuarter(double angle) => (int)Math.Floor((Positive(angle) + Quarter / 2) / Quarter) % 4;

    /// <summary>
    /// A layer's direction turned by its <c>DirOffset</c> (<c>DirExt.OffsetRsiDir</c>). The engine
    /// refuses diagonals here; they are left as they are.
    /// </summary>
    public static Direction Offset(Direction dir, DirOffset offset) => offset switch
    {
        DirOffset.Clockwise => dir switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => dir,
        },
        DirOffset.CounterClockwise => dir switch
        {
            Direction.North => Direction.West,
            Direction.East => Direction.North,
            Direction.South => Direction.East,
            Direction.West => Direction.South,
            _ => dir,
        },
        DirOffset.Flip => dir switch
        {
            Direction.North => Direction.South,
            Direction.East => Direction.West,
            Direction.South => Direction.North,
            Direction.West => Direction.East,
            _ => dir,
        },
        _ => dir,
    };

    /// <summary>
    /// <c>Matrix3Helpers.CreateTransform(position, angle, scale)</c>: scale, then rotate
    /// counter-clockwise, then move. Row vectors, as System.Numerics multiplies them.
    /// </summary>
    public static Matrix3x2 Transform(float x, float y, double angle, float scaleX = 1, float scaleY = 1)
    {
        var sin = (float)Math.Sin(angle);
        var cos = (float)Math.Cos(angle);
        return new Matrix3x2(cos * scaleX, sin * scaleX, -sin * scaleY, cos * scaleY, x, y);
    }

    /// <summary>
    /// Carries a frame's pixels (y down, origin at its top-left) to tiles centred on the frame
    /// (y up), as the engine's quad <c>Box2.FromDimensions(-size / 2, size)</c> places it
    /// (<c>RobustToolbox/Robust.Client/GameObjects/EntitySystems/SpriteSystem.Render.cs</c>).
    /// </summary>
    public static Matrix3x2 PixelsToCentred(float width, float height) =>
        new(1 / PixelsPerTile, 0, 0, -1 / PixelsPerTile, -width / 2 / PixelsPerTile, height / 2 / PixelsPerTile);
}

/// <summary>A layer's <c>DirectionOffset</c> (<c>SpriteComponent.DirectionOffset</c>).</summary>
public enum DirOffset : byte
{
    None,
    Clockwise,
    CounterClockwise,
    Flip,
}
