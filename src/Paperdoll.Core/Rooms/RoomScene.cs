// The tile, edge and decal drawing follows RobustToolbox (Robust.Client/Graphics/Clyde/Clyde.GridRendering.cs,
// Robust.Client/Graphics/Drawing/DrawingHandleWorld.cs), Copyright (c) 2019 Space Station 14 Contributors,
// and Space Station 14 (Content.Client/Decals/Overlays/DecalOverlay.cs), Copyright (c) 2017-2026 Space
// Wizards Federation; both MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Numerics;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Lighting;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Rooms;

/// <summary>A tile of a room as drawn: its square, the tile, and its picture (no strip: a plain plate).</summary>
public sealed record RoomTile(int X, int Y, FloorTile Tile, TilePicture Picture);

/// <summary>
/// A room's pictures at 32 pixels a tile: what is drawn under the character and what is drawn over
/// it, and where the top-left corner of the character's square falls in them, in pixels.
/// </summary>
public sealed record RoomPicture(SKImage Below, SKImage Above, int StandLeft, int StandTop)
{
    /// <summary>The light the pictures are lit by; null when they are drawn flat lit.</summary>
    public LightField? Light { get; init; }

    /// <summary>The centre of the character's square, in tiles.</summary>
    public Vector2 StandCentre { get; init; }

    /// <summary>
    /// A copy of the character's picture, premultiplied, lit by <see cref="Light"/> where it stands:
    /// centred on its square, each of its pixels <paramref name="scaleX"/> by <paramref name="scaleY"/>
    /// of a room's pixel; what a <see cref="CharacterBitmap"/>'s unshaded layers drew keeps its colour.
    /// Without a light it is only copied. The caller disposes it.
    /// </summary>
    public SKBitmap LightCharacter(SKBitmap sprite, float scaleX, float scaleY)
    {
        var lit = new SKBitmap(sprite.Width, sprite.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(lit))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(sprite, 0, 0);
        }
        if (Light != null)
        {
            var width = scaleX / SpriteMath.PixelsPerTile;
            var height = scaleY / SpriteMath.PixelsPerTile;
            Light.LightPicture(lit, StandCentre.X - sprite.Width * width / 2, StandCentre.Y + sprite.Height * height / 2, width, height,
                (sprite as CharacterBitmap)?.Unshaded);
        }
        return lit;
    }
}

/// <summary>
/// A room ready to draw, with every picture read: its tiles, the edges they throw, its decals and
/// entities in the game's order (tiles, edges, decals, then entities by draw depth, y-sort and
/// place in the file), and each picture's credit. The character stands among the entities as a
/// mob on <see cref="Room.Stand"/>, so <see cref="Paint"/> draws two pictures, under and over it.
/// </summary>
public sealed class RoomScene
{
    private readonly IReadOnlyDictionary<string, SKImage> _images;

    private RoomScene(IReadOnlyDictionary<string, SKImage> images) => _images = images;

    /// <summary>The fork commit the pictures were read from.</summary>
    public required string Commit { get; init; }

    public required Room Room { get; init; }

    public required IReadOnlyList<RoomTile> Tiles { get; init; }

    public required IReadOnlyList<DrawItem> Edges { get; init; }

    /// <summary>The decals' pictures, in the order they are drawn: z-index, then the room file's order.</summary>
    public required IReadOnlyList<DrawItem> Decals { get; init; }

    /// <summary>The entities that draw something, in the order they are drawn, each with its prototype, position and rotation.</summary>
    public required IReadOnlyList<EntityVisual> Entities { get; init; }

    /// <summary>The light over <see cref="Extent"/> at round start; null when no light shines or it could not be worked out.</summary>
    public LightField? Light { get; private set; }

    /// <summary>Why the room's light could not be worked out, or null.</summary>
    public string? LightProblem { get; private set; }

    /// <summary>
    /// Puts the plan's pictures together. <paramref name="rsiFile"/> gives an RSI file's bytes (null
    /// when the fork lacks it), <paramref name="picture"/> a loose picture with its credit, and
    /// <paramref name="tile"/> a tile prototype's picture. A file that is missing or cannot be read
    /// leaves out what draws from it.
    /// </summary>
    public static RoomScene Build(RoomPlan plan, string commit, Func<string, byte[]?> rsiFile,
        Func<string, (SKImage? Image, CreditLine Credit)?> picture, Func<string, TilePicture> tile)
    {
        var metas = new Dictionary<string, RsiMeta?>(StringComparer.Ordinal);
        var images = new Dictionary<string, SKImage>(StringComparer.Ordinal);
        var credits = new Dictionary<string, CreditLine>(StringComparer.Ordinal);

        RsiMeta? Meta(string rsi)
        {
            if (metas.TryGetValue(rsi, out var known))
                return known;
            RsiMeta? meta = null;
            if (rsiFile(rsi + "/meta.json") is { } json)
            {
                try
                {
                    meta = RsiMeta.Parse(json);
                }
                catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException)
                {
                    // A broken meta.json draws nothing from its folder.
                }
            }
            credits[rsi] = new CreditLine(rsi, meta?.License, meta?.Copyright);
            return metas[rsi] = meta;
        }

        // Each PNG is decoded once, however many things draw from it.
        (int Width, int Height)? Size(string path)
        {
            if (images.TryGetValue(path, out var known))
                return (known.Width, known.Height);
            SKImage? image;
            if (plan.Pictures.Contains(path))
            {
                var loose = picture(path);
                image = loose?.Image;
                if (loose is { } found)
                    credits[path] = found.Credit;
            }
            else
                image = Decode(rsiFile(path));
            if (image == null)
                return null;
            images[path] = image;
            return (image.Width, image.Height);
        }

        CreditLine Credit(string path) => credits.GetValueOrDefault(path) ?? new CreditLine(path, null, null);

        foreach (var entity in plan.Entities)
            EntityComposer.Compose(entity, Meta, Size, Credit);
        var entities = plan.Entities.Where(e => e.Items.Count > 0).ToList();
        entities.Sort(EntityVisual.DrawOrder);

        var decals = new List<(int Z, int Number, DrawItem Item)>();
        for (var i = 0; i < plan.Decals.Count; i++)
        {
            if (DecalItem(plan.Decals[i], Meta, Size, Credit) is { } item)
                decals.Add((plan.Decals[i].Decal.Z, i, item));
        }

        var edges = new List<DrawItem>();
        foreach (var edge in plan.Edges)
        {
            if (Size(edge.Texture) is not { } size)
                continue;
            // Each edge sprite is turned counter-clockwise by its direction's angle and lies on the
            // tile that receives it.
            edges.Add(new DrawItem
            {
                Image = edge.Texture,
                Width = size.Width,
                Height = size.Height,
                Transform = SpriteMath.PixelsToCentred(size.Width, size.Height)
                    * Matrix3x2.CreateRotation((float)edge.Angle)
                    * Matrix3x2.CreateTranslation(edge.X + 0.5f, edge.Y + 0.5f),
                Credit = Credit(edge.Texture),
            });
        }

        var scene = new RoomScene(images)
        {
            Commit = commit,
            Room = plan.Room,
            Tiles = plan.Room.Tiles.OrderBy(t => t.Key.Y).ThenBy(t => t.Key.X)
                .Select(t => new RoomTile(t.Key.X, t.Key.Y, new FloorTile(t.Value, 0, 0), tile(t.Value)))
                .ToList(),
            Edges = edges,
            Decals = decals.OrderBy(d => d.Z).ThenBy(d => d.Number).Select(d => d.Item).ToList(),
            Entities = entities,
            LightProblem = plan.LightProblem,
        };
        // Worked out once, with the pictures, so drawing the room again never does it again.
        if (plan.LightProblem == null && plan.Lights.Count > 0)
        {
            try
            {
                scene.Light = LightField.Compute(scene.Extent(), plan.Lights, plan.Occluders, (x, y) => !plan.Roofed.Contains((x, y)));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or OutOfMemoryException or AggregateException)
            {
                scene.LightProblem = e.Message;
            }
        }
        return scene;
    }

    /// <summary>
    /// A decal's picture: frame 0 of its RSI state facing south, or its texture, tinted by its
    /// colour, with its bottom-left corner at its position and turned about its centre
    /// (DecalOverlay.cs; DrawingHandleWorld.DrawTexture turns the quad about its centre).
    /// </summary>
    private static DrawItem? DecalItem(PlannedDecal decal, Func<string, RsiMeta?> metas, Func<string, (int Width, int Height)?> size, Func<string, CreditLine> credit)
    {
        RsiMeta? meta = null;
        RsiState? state = null;
        string image;
        int width, height;
        if (decal is { Rsi: { } rsi, State: { } stateName })
        {
            image = EntityComposer.StatePath(rsi, stateName);
            if (metas(rsi) is not { } m || !m.States.TryGetValue(stateName, out state) || size(image) is null)
                return null;
            (meta, width, height) = (m, m.FrameWidth, m.FrameHeight);
        }
        else if (decal.Texture is { } texture && size(texture) is { } textureSize)
            (image, width, height) = (texture, textureSize.Width, textureSize.Height);
        else
            return null;

        var w = width / SpriteMath.PixelsPerTile;
        var h = height / SpriteMath.PixelsPerTile;
        var color = decal.Decal.Color;
        return new DrawItem
        {
            Image = image,
            Width = width,
            Height = height,
            Transform = SpriteMath.PixelsToCentred(width, height)
                * Matrix3x2.CreateRotation((float)decal.Decal.Angle)
                * Matrix3x2.CreateTranslation(decal.Decal.X + w / 2, decal.Decal.Y + h / 2),
            Tint = color == Rgba.White ? null : color,
            Meta = meta,
            State = state,
            Credit = credit(decal.Rsi ?? image),
        };
    }

    // A picture that cannot be decoded is left out.
    private static SKImage? Decode(byte[]? png)
    {
        if (png == null)
            return null;
        try
        {
            using var bitmap = SKBitmap.Decode(png);
            return bitmap == null ? null : SKImage.FromBitmap(bitmap);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether an entity draws over the character, which stands on <see cref="Room.Stand"/> as one
    /// more entity at the Mobs depth, its 32-pixel frame on its square (so its y-sort key is the
    /// square's south edge), after every entity of the same depth and key.
    /// </summary>
    public bool OverCharacter(EntityVisual entity) =>
        EntityVisual.Order(entity.DrawDepth, entity.SortY, entity.Index, DrawDepths.Mobs, -Room.Stand.Y, int.MaxValue) > 0;

    /// <summary>How far past the room's tiles, in tiles, the pictures reach at most; anything further is cut off.</summary>
    public const int PictureMargin = 4;

    /// <summary>
    /// What the scene covers, in tiles: the room's squares and every picture drawn, rounded out to
    /// whole tiles, and no more than <see cref="PictureMargin"/> past the squares however big a
    /// sprite is, so the picture stays small.
    /// </summary>
    public Box Extent()
    {
        var box = new Box(0, 0, Room.Width, Room.Height);
        foreach (var item in Edges.Concat(Decals).Concat(Entities.SelectMany(e => e.Items)))
            box = box.Union(item.Bounds);
        return new Box(
            Math.Max(-PictureMargin, MathF.Floor(box.MinX)), Math.Max(-PictureMargin, MathF.Floor(box.MinY)),
            Math.Min(Room.Width + PictureMargin, MathF.Ceiling(box.MaxX)), Math.Min(Room.Height + PictureMargin, MathF.Ceiling(box.MaxY)));
    }

    /// <summary>
    /// Draws the room at 32 pixels a tile: tiles (a tile without a picture as a <paramref name="plate"/>),
    /// their edges and the decals, then the entities, those under the character in one picture and
    /// those over it in the other. Nothing else is drawn; the pictures are transparent there.
    /// When <paramref name="lit"/> and the room has a <see cref="Light"/>, each picture is lit by it
    /// as it is drawn, in the game's order, except <c>unshaded</c> layers, which keep their colour.
    /// </summary>
    public RoomPicture Paint(SKColor plate, bool lit = false)
    {
        var light = lit ? Light : null;
        var extent = Extent();
        var width = (int)(extent.MaxX - extent.MinX) * (int)SpriteMath.PixelsPerTile;
        var height = (int)(extent.MaxY - extent.MinY) * (int)SpriteMath.PixelsPerTile;
        // World tiles (+y north) to picture pixels (+y down).
        var view = new Matrix3x2(SpriteMath.PixelsPerTile, 0, 0, -SpriteMath.PixelsPerTile, -extent.MinX * SpriteMath.PixelsPerTile, extent.MaxY * SpriteMath.PixelsPerTile);
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
        using var paint = new SKPaint { IsAntialias = false };

        using var below = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(below))
        {
            canvas.Clear(SKColors.Transparent);
            var size = SpriteMath.PixelsPerTile;
            foreach (var tile in Tiles)
            {
                var x = (tile.X - extent.MinX) * size;
                var y = (extent.MaxY - tile.Y - 1) * size;
                if (!tile.Picture.Draw(canvas, tile.Tile, x, y, size, sampling, paint))
                {
                    paint.Color = plate;
                    canvas.DrawRect(x, y, size, size, paint);
                }
            }
            foreach (var item in Edges.Concat(Decals))
                PaintItem(canvas, view, item, sampling);
            // Tiles, edges and decals are all shaded, so they are lit together.
            canvas.Flush();
            light?.LightPicture(below, extent.MinX, extent.MaxY, 1 / SpriteMath.PixelsPerTile, 1 / SpriteMath.PixelsPerTile);
            foreach (var item in Entities.Where(e => !OverCharacter(e)).SelectMany(e => e.Items))
                PaintItem(canvas, view, item, sampling, light, extent);
        }

        using var above = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(above))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var item in Entities.Where(OverCharacter).SelectMany(e => e.Items))
                PaintItem(canvas, view, item, sampling, light, extent);
        }

        var standLeft = (int)((Room.Stand.X - extent.MinX) * SpriteMath.PixelsPerTile);
        var standTop = (int)((extent.MaxY - Room.Stand.Y - 1) * SpriteMath.PixelsPerTile);
        return new RoomPicture(SKImage.FromBitmap(below), SKImage.FromBitmap(above), standLeft, standTop)
        {
            Light = light,
            StandCentre = new Vector2(Room.Stand.X + 0.5f, Room.Stand.Y + 0.5f),
        };
    }

    // An entity's picture, lit when there is a light and it is shaded: drawn on its own over the
    // pixels it covers, lit there, then laid on the canvas, so that what is under it keeps its own light.
    private void PaintItem(SKCanvas canvas, Matrix3x2 view, DrawItem item, SKSamplingOptions sampling, LightField? light, Box extent)
    {
        if (light == null || item.Unshaded)
        {
            PaintItem(canvas, view, item, sampling);
            return;
        }
        var size = SpriteMath.PixelsPerTile;
        var bounds = item.Bounds;
        var info = canvas.DeviceClipBounds;
        var left = (int)Math.Clamp(MathF.Floor((bounds.MinX - extent.MinX) * size), info.Left, info.Right);
        var right = (int)Math.Clamp(MathF.Ceiling((bounds.MaxX - extent.MinX) * size), info.Left, info.Right);
        var top = (int)Math.Clamp(MathF.Floor((extent.MaxY - bounds.MaxY) * size), info.Top, info.Bottom);
        var bottom = (int)Math.Clamp(MathF.Ceiling((extent.MaxY - bounds.MinY) * size), info.Top, info.Bottom);
        if (right <= left || bottom <= top)
            return;
        using var layer = new SKBitmap(right - left, bottom - top, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var layerCanvas = new SKCanvas(layer))
        {
            layerCanvas.Clear(SKColors.Transparent);
            PaintItem(layerCanvas, view * Matrix3x2.CreateTranslation(-left, -top), item, sampling);
        }
        light.LightPicture(layer, extent.MinX + left / size, extent.MaxY - top / size, 1 / size, 1 / size);
        canvas.DrawBitmap(layer, left, top);
    }

    // One picture, its frame carried by its matrix and the view, multiplied by its tint.
    private void PaintItem(SKCanvas canvas, Matrix3x2 view, DrawItem item, SKSamplingOptions sampling)
    {
        if (!_images.TryGetValue(item.Image, out var image))
            return;
        var m = item.Transform * view;
        canvas.SetMatrix(new SKMatrix(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1));
        using var tint = item.Tint is { } t
            ? SKColorFilter.CreateBlendMode(new SKColor(Byte(t.R), Byte(t.G), Byte(t.B), Byte(t.A)), SKBlendMode.Modulate)
            : null;
        using var paint = new SKPaint { ColorFilter = tint };
        canvas.DrawImage(image, item.SourceIn(image), SKRect.Create(item.Width, item.Height), sampling, paint);
        canvas.ResetMatrix();
    }

    private static byte Byte(float channel) => (byte)Math.Round(Math.Clamp(channel, 0f, 1f) * 255);

    /// <summary>The credits of the pictures drawn within a region, in tiles, by path.</summary>
    public IReadOnlyList<CreditLine> CreditsWithin(Box region)
    {
        var credits = new Dictionary<string, CreditLine>(StringComparer.Ordinal);
        foreach (var tile in Tiles)
        {
            if (tile.Picture is { Strip: not null, Credit: { } credit } && new Box(tile.X, tile.Y, tile.X + 1, tile.Y + 1).Intersects(region))
                credits[credit.Rsi] = credit;
        }
        foreach (var item in Edges.Concat(Decals).Concat(Entities.SelectMany(e => e.Items)))
        {
            if (item.Bounds.Intersects(region))
                credits[item.Credit.Rsi] = item.Credit;
        }
        return credits.Values.OrderBy(c => c.Rsi, StringComparer.Ordinal).ToList();
    }
}
