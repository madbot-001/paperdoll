// The IconSmooth rules are ported from Space Station 14 (Content.Client/IconSmoothing/IconSmoothSystem.cs),
// Copyright (c) 2017-2026 Space Wizards Federation, and the drawing from RobustToolbox
// (Robust.Client/GameObjects/EntitySystems/SpriteSystem.Render.cs, SpriteComponent.cs), Copyright (c)
// 2019 Space Station 14 Contributors; both MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Numerics;
using Paperdoll.Core.Editing;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// Works out what the game draws for one placed entity: its prototype's sprite layers, changed the
/// way <c>IconSmooth</c> changes them from the anchored neighbours (Corners adds four corner layers
/// whose states come from the eight neighbours; CardinalFlags and Diagonal set the first layer's
/// state), then each layer as a picture with the engine's direction and matrices. Everything else
/// shows its prototype's layers as written: states the game sets at run time from appearance data
/// (a door's lights, a machine's screen) are not shown, and animations show their first frame.
/// </summary>
public static class EntityComposer
{
    /// <summary>The sprite's layers after <c>IconSmooth</c> has had its say, bottom to top. Needs no picture, so it can list the files to fetch.</summary>
    /// <param name="anchoredAt">The anchored entities on a tile.</param>
    public static List<LayerSpec> Layers(EntityVisual v, Func<int, int, IEnumerable<EntityVisual>> anchoredAt)
    {
        var layers = v.Sprite.Layers.Select(l => new LayerSpec(l)).ToList();
        if (v.Smooth is { Mode: not "NoSprite" } smooth)
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
