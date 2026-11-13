// Ported from Space Station 14 (Content.Shared/DrawDepth/DrawDepth.cs), Copyright (c) 2017-2026
// Space Wizards Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Globalization;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// The game's draw depths by name. A sprite's <c>drawdepth: Walls</c> is read into these numbers;
/// higher draws on top, and the engine's default is 0 (<c>Objects</c>;
/// <c>RobustToolbox/Robust.Shared/GameObjects/DrawDepth.cs</c>).
/// <para>
/// This is upstream's table. A fork may renumber it or add names in its own
/// <c>Content.Shared/DrawDepth/DrawDepth.cs</c>; that file is code, not data Paperdoll downloads, so
/// it is not read: a fork's renumbering is not followed, and a name only a fork has draws at the
/// default depth.
/// </para>
/// </summary>
public static class DrawDepths
{
    public const int Default = 0;

    /// <summary>Where a mob stands, as the character does.</summary>
    public const int Mobs = 6;

    /// <summary>Upstream's depths, relative to <c>DrawDepthTag.Default</c> = 0.</summary>
    public static readonly IReadOnlyDictionary<string, int> ByName = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["LowFloors"] = -22,
        ["ThickPipe"] = -21,
        ["ThickWire"] = -20,
        ["ThinPipeAlt2"] = -19,
        ["ThinPipeAlt1"] = -18,
        ["ThinPipe"] = -17,
        ["ThinWire"] = -16,
        ["BelowFloor"] = -15,
        ["FloorTiles"] = -14,
        ["FloorObjects"] = -13,
        ["Puddles"] = -12,
        ["HighFloorObjects"] = -5,
        ["DeadMobs"] = -4,
        ["SmallMobs"] = -3,
        ["Walls"] = -2,
        ["WallTops"] = -1,
        ["Objects"] = 0,
        ["SmallObjects"] = 1,
        ["WallMountedItems"] = 2,
        ["LargeObjects"] = 3,
        ["Items"] = 4,
        ["BelowMobs"] = 5,
        ["Mobs"] = Mobs,
        ["OverMobs"] = 7,
        ["Doors"] = 8,
        ["BlastDoors"] = 9,
        ["Overdoors"] = 10,
        ["Gasses"] = 17,
        ["Effects"] = 18,
        ["Ghosts"] = 19,
        ["Overlays"] = 20,
    };

    /// <summary>A depth written by name or as a number; null for anything else.</summary>
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (ByName.TryGetValue(text.Trim(), out var depth))
            return depth;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
