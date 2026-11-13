// The IconSmooth rules are ported from Space Station 14 (Content.Client/IconSmoothing/IconSmoothSystem.cs,
// and for the newer form ISpriteSmoothState.cs and Smoothings/*SpriteSmoothing.cs),
// Copyright (c) 2017-2026 Space Wizards Federation, and the drawing from RobustToolbox
// (Robust.Client/GameObjects/EntitySystems/SpriteSystem.Render.cs, SpriteComponent.cs), Copyright (c)
// 2019 Space Station 14 Contributors; both MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Numerics;
using Paperdoll.Core.Editing;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// Works out what the game draws for one placed entity: its prototype's sprite layers, changed the
/// way <c>IconSmooth</c> changes them from the anchored neighbours (Corners adds four corner layers
/// whose states come from the eight neighbours; CardinalFlags and Diagonal set the first layer's
/// state; in the newer form each of its states adds and sets its own layers), then each layer as
/// a picture with the engine's direction and matrices. Everything else shows its prototype's
/// layers as written: states the game sets at run time from appearance data
/// (a door's lights, a machine's screen) are not shown, and animations show their first frame.
/// </summary>
public static class EntityComposer
{
    /// <summary>The sprite's layers after <c>IconSmooth</c> has had its say, bottom to top. Needs no picture, so it can list the files to fetch.</summary>
    /// <param name="anchoredAt">The anchored entities on a tile.</param>
    public static List<LayerSpec> Layers(EntityVisual v, Func<int, int, IEnumerable<EntityVisual>> anchoredAt)
    {
        var layers = v.Sprite.Layers.Select(l => new LayerSpec(l)).ToList();
        if (v.Smooth is { States: { } states })
            SmoothStates(v, states, anchoredAt, layers, v.Sprite.Rsi);
        else if (v.Smooth is { Mode: not "NoSprite" } smooth)
            Smooth(v, smooth, anchoredAt, layers, v.Sprite.Rsi);
        return layers;
    }

    /// <summary>
    /// Fills in the entity's pictures, draw depth, bounds and y-sort key from its <see cref="EntityVisual.Layers"/>.
    /// A layer whose state, meta.json or picture is missing is left out.
    /// </summary>
    /// <param name="meta">An RSI's meta.json, by folder; null when it is missing or broken.</param>
    /// <param name="credit">The credit of an RSI or loose picture, by path.</param>
    /// <param name="textureSize">A loose picture's size, by path; null when it is missing or broken.</param>
    public static void Compose(EntityVisual v, Func<string, RsiMeta?> meta, Func<string, (int Width, int Height)?> textureSize, Func<string, CreditLine> credit)
    {
        var sprite = v.Sprite;
        var angle = SpriteMath.Positive(v.Rotation);
        // SpriteSystem.Render.cs: snapCardinals keeps a sprite upright at the nearest quarter
        // turn; noRot keeps it upright always.
        var cardinal = !sprite.NoRot && sprite.SnapCardinals ? SpriteMath.RoundToCardinal(angle) : 0;
        var entity = SpriteMath.Transform(v.Position.X, v.Position.Y, sprite.NoRot ? 0 : v.Rotation - cardinal);
        var spriteMatrix = SpriteMath.Transform(sprite.OffsetX, sprite.OffsetY, sprite.Rotation, sprite.ScaleX, sprite.ScaleY) * entity;
        var spriteColor = sprite.Color ?? Rgba.White;

        var items = new List<DrawItem>(v.Layers.Count);
        foreach (var layer in v.Layers)
        {
            if (layer.Visible && Item(layer, spriteColor, angle, sprite.NoRot, spriteMatrix, meta, textureSize, credit) is { } item)
                items.Add(item);
        }

        v.Items = items;
        v.DrawDepth = sprite.DrawDepth;
        var bounds = Box.Empty;
        foreach (var item in items)
            bounds = bounds.Union(item.Bounds);
        v.Bounds = bounds;
        v.SortY = bounds.IsEmpty ? -v.Position.Y : -bounds.MinY;
    }

    // One layer as a picture: the frame its state shows at this angle, carried by layer, sprite
    // and entity matrices (SpriteSystem.Render.cs, RenderLayer).
    private static DrawItem? Item(LayerSpec layer, Rgba spriteColor, double angle, bool noRot, Matrix3x2 spriteMatrix,
        Func<string, RsiMeta?> metas, Func<string, (int Width, int Height)?> textureSize, Func<string, CreditLine> credit)
    {
        var color = EntitySprite.Multiply(spriteColor, layer.Color);
        var tint = color == Rgba.White ? (Rgba?)null : color;
        var local = SpriteMath.Transform(layer.OffsetX, layer.OffsetY, layer.Rotation, layer.ScaleX, layer.ScaleY);
        var unshaded = layer.Shader == "unshaded";

        if (layer.State != null)
        {
            if (layer.Rsi is not { } rsi || metas(rsi) is not { } meta || !meta.States.TryGetValue(layer.State, out var state)
                || textureSize(StatePath(rsi, layer.State)) is null)
                return null;
            var dir = SpriteMath.ForState(state.Directions, angle);
            // SpriteComponent.Layer.GetLayerDrawMatrix: a directional frame is turned back by its
            // direction's angle, so it shows upright.
            var draw = dir == Direction.South || noRot ? local : Matrix3x2.CreateRotation((float)-SpriteMath.AngleOf(dir)) * local;
            return new DrawItem
            {
                Image = StatePath(rsi, layer.State),
                Tint = tint,
                Width = meta.FrameWidth,
                Height = meta.FrameHeight,
                Transform = SpriteMath.PixelsToCentred(meta.FrameWidth, meta.FrameHeight) * draw * spriteMatrix,
                Meta = meta,
                State = state,
                Direction = SpriteMath.Offset(dir, layer.DirOffset),
                Unshaded = unshaded,
                Credit = credit(rsi),
            };
        }

        if (layer.Texture is { } texture && textureSize(texture) is { } size)
        {
            return new DrawItem
            {
                Image = texture,
                Tint = tint,
                Width = size.Width,
                Height = size.Height,
                Transform = SpriteMath.PixelsToCentred(size.Width, size.Height) * local * spriteMatrix,
                Unshaded = unshaded,
                Credit = credit(texture),
            };
        }
        return null;
    }

    /// <summary>A state's PNG in its RSI: "Structures/Walls/solid.rsi" and "full" give "Structures/Walls/solid.rsi/full.png".</summary>
    public static string StatePath(string rsi, string state) => $"{rsi}/{state}.png";

    private static void Smooth(EntityVisual v, SmoothFacts smooth, Func<int, int, IEnumerable<EntityVisual>> anchoredAt, List<LayerSpec> layers, string? baseRsi)
    {
        var (x, y) = v.Tile;
        // An entity that is not anchored has no grid to smooth on (IconSmoothSystem.cs).
        var onGrid = v.Anchored;
        bool Joins(int dx, int dy) => anchoredAt(x + dx, y + dy).Any(o => o != v && o.Smooth is { } other && smooth.JoinsWith(other));

        switch (smooth.Mode)
        {
            case "CardinalFlags":
            {
                // North 1, South 2, East 4, West 8.
                if (!smooth.Enabled || layers.Count == 0)
                    return;
                var dirs = !onGrid ? 0
                    : (Joins(0, 1) ? 1 : 0) | (Joins(0, -1) ? 2 : 0) | (Joins(1, 0) ? 4 : 0) | (Joins(-1, 0) ? 8 : 0);
                layers[0].State = smooth.Base + dirs;
                return;
            }
            case "Diagonal":
            {
                // East, south-east and south of it, turned with it, all join.
                if (!smooth.Enabled || layers.Count == 0)
                    return;
                var joined = onGrid;
                var (sin, cos) = Math.SinCos(v.Rotation);
                foreach (var (nx, ny) in new[] { (1, 0), (1, -1), (0, -1) })
                {
                    var (rx, ry) = ((int)Math.Round(nx * cos - ny * sin), (int)Math.Round(nx * sin + ny * cos));
                    joined = joined && Joins(rx, ry);
                }
                layers[0].State = smooth.Base + (joined ? "1" : "0");
                return;
            }
            case "Corners":
            {
                var (ne, nw, sw, se) = smooth.Enabled && onGrid ? CornerFill(v, Joins) : (0, 0, 0, 0);
                // IconSmoothSystem.SetCornerLayers: SE, NE, NW, SW, each showing the frame of its
                // corner by a direction offset, on top unless an index is given.
                var corners = new[]
                {
                    new LayerSpec { Rsi = baseRsi, State = smooth.Base + se, DirOffset = DirOffset.None },
                    new LayerSpec { Rsi = baseRsi, State = smooth.Base + ne, DirOffset = DirOffset.CounterClockwise },
                    new LayerSpec { Rsi = baseRsi, State = smooth.Base + nw, DirOffset = DirOffset.Flip },
                    new LayerSpec { Rsi = baseRsi, State = smooth.Base + sw, DirOffset = DirOffset.Clockwise },
                };
                foreach (var corner in corners)
                {
                    if (smooth.Index is { } index)
                        layers.Insert(Math.Clamp(index, 0, layers.Count), corner);
                    else
                        layers.Add(corner);
                }
                return;
            }
        }
    }

    // The newer IconSmooth. IconSmoothSystem.StartupLayers has each state add its layers to the
    // sprite in order (ISpriteSmoothState.Initialize); CalculateNewSprite then lets each state set
    // its layers from the keys of the eight neighbouring tiles. An entity that is not anchored or
    // is switched off is never smoothed at round start, so keeps the layers as first added.
    private static void SmoothStates(EntityVisual v, IReadOnlyList<SmoothState> states, Func<int, int, IEnumerable<EntityVisual>> anchoredAt,
        List<LayerSpec> layers, string? baseRsi)
    {
        var keyed = new Dictionary<string, LayerSpec>(StringComparer.Ordinal);
        foreach (var state in states)
            InitializeState(state, layers, keyed, baseRsi);
        if (!v.Anchored || v.Smooth is not { Enabled: true })
            return;
        var adjacent = AdjacentKeys(v, anchoredAt);
        foreach (var state in states)
            ApplyState(state, adjacent, keyed);
    }

    /// <summary>
    /// <c>IconSmoothSystem.PopulateAdjacentKeys</c>: for each of the eight neighbouring tiles, the
    /// keys of the switched-on <c>IconSmooth</c> entities anchored there, or null for none; indexed
    /// by direction (South 0, then counter-clockwise to South-West 7) turned into the entity's own
    /// frame by its rotation.
    /// </summary>
    private static HashSet<string>?[] AdjacentKeys(EntityVisual v, Func<int, int, IEnumerable<EntityVisual>> anchoredAt)
    {
        var adjacent = new HashSet<string>?[8];
        var (x, y) = v.Tile;
        // AngleToOffset: the rotation turned back, as a cardinal Direction (0, 2, 4 or 6).
        var offset = 2 * SpriteMath.CardinalQuarter(-v.Rotation);
        for (var direction = 0; direction < 8; direction++)
        {
            var (dx, dy) = Step(direction);
            var keys = anchoredAt(x + dx, y + dy)
                .Select(o => o.Smooth is { Enabled: true, Key: { } key } ? key : null)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            if (keys.Count > 0)
                adjacent[(direction + offset) % 8] = keys;
        }
        return adjacent;
    }

    // Robust.Shared.Maths/Direction.cs, ToIntVec: the step of each Direction.
    private static (int X, int Y) Step(int direction) => direction switch
    {
        0 => (0, -1),
        1 => (1, -1),
        2 => (1, 0),
        3 => (1, 1),
        4 => (0, 1),
        5 => (-1, 1),
        6 => (-1, 0),
        _ => (-1, -1),
    };

    // ISpriteSmoothState.InitializeStates for each kind: AddRsiLayer in the sprite's own RSI, at the
    // state's index or on top, mapped by its layer key; then Initialize sets the shader of the layer
    // mapped by the plain layer key, when there is one.
    private static void InitializeState(SmoothState state, List<LayerSpec> layers, Dictionary<string, LayerSpec> keyed, string? baseRsi)
    {
        void Add(string key, string rsiState, DirOffset dirOffset = DirOffset.None, float offsetX = 0, float offsetY = 0, bool visible = true)
        {
            var layer = new LayerSpec { Rsi = baseRsi, State = rsiState, DirOffset = dirOffset, OffsetX = offsetX, OffsetY = offsetY, Visible = visible, MapKeys = [key] };
            if (state.Index is { } index)
                layers.Insert(Math.Clamp(index, 0, layers.Count), layer);
            else
                layers.Add(layer);
            keyed[key] = layer;
        }

        switch (state.Kind)
        {
            case SmoothKind.Corner or SmoothKind.CardinalCorner:
                // CornerSpriteSmoothing.InitializeStates: one layer per DirectionOffset in the enum's
                // order, each showing its corner's frame and first the state for no neighbours.
                Add(state.LayerKey + "SouthEast", state.Base + 0, DirOffset.None);
                Add(state.LayerKey + "SouthWest", state.Base + 0, DirOffset.Clockwise);
                Add(state.LayerKey + "NorthEast", state.Base + 0, DirOffset.CounterClockwise);
                Add(state.LayerKey + "NorthWest", state.Base + 0, DirOffset.Flip);
                break;
            case SmoothKind.Diagonal or SmoothKind.AltDiagonal or SmoothKind.Direction:
                Add(state.LayerKey, state.Base + 0);
                break;
            case SmoothKind.Edge:
                // EdgeSpriteSmoothing: south, east, north and west, each a tile out that way and hidden.
                foreach (var direction in new[] { 0, 2, 4, 6 })
                {
                    var (dx, dy) = Step(direction);
                    Add(state.LayerKey + EdgeSuffix(direction), state.Base + EdgeSuffix(direction), offsetX: dx, offsetY: dy, visible: false);
                }
                break;
        }

        if (state.Shader is { } shader && (keyed.GetValueOrDefault(state.LayerKey) ?? layers.FirstOrDefault(l => l.MapKeys.Contains(state.LayerKey))) is { } shaded)
            shaded.Shader = shader;
    }

    // EdgeSpriteSmoothing.GetSuffix.
    private static string EdgeSuffix(int direction) => direction switch
    {
        0 => "south",
        2 => "east",
        4 => "north",
        _ => "west",
    };

    // Each kind's EnumerateStates, giving the states IconSmoothSystem.ApplyState sets by layer key.
    private static void ApplyState(SmoothState state, HashSet<string>?[] adjacent, Dictionary<string, LayerSpec> keyed)
    {
        bool Matches(int i, IReadOnlySet<string> mask) => adjacent[i] is { } keys && keys.Overlaps(mask);

        switch (state.Kind)
        {
            case SmoothKind.Corner or SmoothKind.CardinalCorner:
            {
                // Each corner from its three directions, once all three have been seen: at 2
                // south-east, at 4 north-east, at 6 north-west and at 7 south-west.
                int match = 0, altMatch = 0, seen = 0;
                for (var i = 0; i < 8; i++)
                {
                    if (Matches(i, state.Mask))
                        match |= 1 << i;
                    if (state.Kind == SmoothKind.CardinalCorner && Matches(i, state.AltMask))
                        altMatch |= 1 << i;
                    if (Corner(i) is not { } corner)
                        continue;
                    var directions = Offset(match & corner.Mask, seen);
                    var appendix = state.Kind == SmoothKind.Corner ? directions.ToString(CultureInfo.InvariantCulture)
                        : CardinalCornerAppendix(directions, Offset(altMatch & corner.Mask, seen));
                    keyed[state.LayerKey + corner.Name].State = state.Base + appendix;
                    seen += 2;
                }
                return;
            }
            case SmoothKind.Diagonal or SmoothKind.AltDiagonal:
            {
                // South, south-east and east must all match.
                const int mask = 0b111;
                int match = 0, altMatch = 0;
                for (var i = 0; i <= 2; i++)
                {
                    if (Matches(i, state.Mask))
                        match |= 1 << i;
                    if (state.Kind == SmoothKind.AltDiagonal && Matches(i, state.AltMask))
                        altMatch |= 1 << i;
                }
                keyed[state.LayerKey].State = state.Base + ((match & mask) == mask ? 1 : (altMatch & mask) == mask ? 2 : 0);
                return;
            }
            case SmoothKind.Direction:
            {
                // DirectionFlag: South 1, East 2, North 4, West 8.
                var match = 0;
                for (var i = 0; i < 4; i++)
                {
                    if (Matches(2 * i, state.Mask))
                        match |= 1 << i;
                }
                keyed[state.LayerKey].State = state.Base + match;
                return;
            }
            case SmoothKind.Edge:
            {
                // An edge shows where the neighbour does not match.
                foreach (var direction in new[] { 0, 2, 4, 6 })
                    keyed[state.LayerKey + EdgeSuffix(direction)].Visible = !Matches(direction, state.Mask);
                return;
            }
        }
    }

    // CornerSpriteSmoothing.GetCorners and GetCornerLayerKey: the directions a corner is made from
    // (as Direction8Flag bits) and its name.
    private static (int Mask, string Name)? Corner(int i) => i switch
    {
        2 => (0b0000_0111, "SouthEast"),
        4 => (0b0001_1100, "NorthEast"),
        6 => (0b0111_0000, "NorthWest"),
        7 => (0b1100_0001, "SouthWest"),
        _ => null,
    };

    // CornerSpriteSmoothing.Offset: the byte turned right by the offset, so a corner's three
    // directions read 1 (clockwise cardinal), 2 (diagonal) and 4 (counter-clockwise cardinal).
    private static int Offset(int directions, int offset) =>
        offset > 0 ? (byte)((directions >> offset) | (directions << (8 - offset))) : directions;

    // CardinalCornerSpriteSmoothing.GetCornerState: 0 to 13 from which of the corner's directions
    // match the mask and which only the alternative mask.
    private static string CardinalCornerAppendix(int directions, int altDirections)
    {
        var cardinals = ConvertToCardinal(directions);
        var cardinalMask = cardinals | ConvertToCardinal(altDirections);
        int appendix;
        if (cardinalMask < 3)
            appendix = cardinalMask != cardinals ? cardinalMask + 2 : cardinals;
        else if (cardinalMask == cardinals)
            appendix = (directions & 2) == 2 ? 6 : (altDirections & 2) == 2 ? 7 : 5;
        else
            appendix = (directions & 2) == 2 || (altDirections & 2) == 2 ? cardinals + 11 : cardinals + 8;
        return appendix.ToString(CultureInfo.InvariantCulture);
    }

    // CardinalCornerSpriteSmoothing.ConvertToCardinal: the even bits packed together.
    private static int ConvertToCardinal(int flag)
    {
        flag &= 0x55;
        flag |= flag >> 1;
        flag &= 0x33;
        flag |= flag >> 2;
        return flag & 0xF;
    }

    /// <summary>
    /// <c>IconSmoothSystem.CalculateCornerFill</c>: each corner is 1 when the cardinal neighbour
    /// counter-clockwise of it joins, 2 for the diagonal, 4 for the cardinal clockwise; then the
    /// corners are turned with the entity's rotation.
    /// </summary>
    private static (int NE, int NW, int SW, int SE) CornerFill(EntityVisual v, Func<int, int, bool> joins)
    {
        const int ccw = 1, diagonal = 2, cw = 4;
        int cNE = 0, cNW = 0, cSW = 0, cSE = 0;
        if (joins(0, 1))
        {
            cNE |= ccw;
            cNW |= cw;
        }
        if (joins(1, 1))
            cNE |= diagonal;
        if (joins(1, 0))
        {
            cNE |= cw;
            cSE |= ccw;
        }
        if (joins(1, -1))
            cSE |= diagonal;
        if (joins(0, -1))
        {
            cSE |= cw;
            cSW |= ccw;
        }
        if (joins(-1, -1))
            cSW |= diagonal;
        if (joins(-1, 0))
        {
            cSW |= cw;
            cNW |= ccw;
        }
        if (joins(-1, 1))
            cNW |= diagonal;

        return SpriteMath.CardinalQuarter(v.Rotation) switch
        {
            2 => (cSW, cSE, cNE, cNW),   // North
            3 => (cSE, cNE, cNW, cSW),   // West
            0 => (cNE, cNW, cSW, cSE),   // South
            _ => (cNW, cSW, cSE, cNE),   // East
        };
    }
}
