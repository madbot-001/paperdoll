// The layer order and colouring follow Space Station 14's Content.Client/Body/VisualBodySystem.cs
// and Content.Shared/Body/SharedVisualBodySystem.cs, Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Characters;
using Paperdoll.Core.Outfits;
using Paperdoll.Core.Prototypes;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rendering;

/// <summary>How a character looks: what the preview draws (new appearance model).</summary>
public sealed class CharacterLook
{
    public required string Species { get; init; }
    public string Sex { get; init; } = "Male";
    public Rgba SkinColor { get; init; } = Rgba.White;
    public Rgba EyeColor { get; init; } = Rgba.White;

    /// <summary>Markings by organ category (such as <c>Head</c>), then by layer (such as <c>Hair</c>).</summary>
    public Dictionary<string, Dictionary<string, List<MarkingEntry>>> Markings { get; init; } = [];
}

/// <summary>Reads files under <c>Resources/Textures</c> by path.</summary>
public interface ITextureSource
{
    byte[]? Read(string path);
}

/// <summary>One drawn layer, bottom to top: what the preview shows and the credits list.</summary>
public sealed record DrawnLayer(string Key, SpriteRef Sprite, Rgba Color, DisplacementRef? Displacement = null);

/// <summary>
/// Draws a character from a fork's data as the game's lobby preview does, facing one way:
/// <list type="bullet">
/// <item>Layers start as the species doll's sprite layers, in order.</item>
/// <item>Each organ fills the layer named by its <c>VisualOrgan</c>, tinted with the skin colour
/// (the eye colour on the eyes layer), with its per-sex state if it has one.</item>
/// <item>Each marking is inserted just above its body part's layer, so a marking applied later
/// on the same layer ends up below earlier ones. Markings with forced colours, or on layers that
/// match the skin, are coloured by their rules and applied after the others.</item>
/// <item>Displacement maps reshape an organ's layer (Dwarf bodies) or markings on a layer (hair on
/// Vox), unless the marking opts out.</item>
/// <item>Colours multiply the sprite's pixels. Frames are centred on each other.</item>
/// </list>
/// Not drawn yet: marking shaders and the nudity-censoring defaults.
/// </summary>
public sealed class PaperdollRenderer
{
    private const string LayerPrefix = "enum.HumanoidVisualLayers.";
    private readonly Dictionary<string, RsiMeta?> _metas = new(StringComparer.Ordinal);

    // Decoded frames, and frames reshaped by a displacement map, kept so each is only made once.
    private readonly Dictionary<(string Rsi, string State, Direction Direction, int Frame), Pixels?> _frames = new();
    private readonly Dictionary<(SpriteRef Sprite, SpriteRef Map, Direction Direction, int Frame), Pixels?> _displaced = new();

    /// <summary>A frame's pixels, row by row, with straight (not premultiplied) alpha.</summary>
    private sealed record Pixels(int Width, int Height, SKColor[] Data)
    {
        public static Pixels From(SKBitmap bitmap)
        {
            var data = new SKColor[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                    data[y * bitmap.Width + x] = bitmap.GetPixel(x, y);
            }
            return new Pixels(bitmap.Width, bitmap.Height, data);
        }

        public SKBitmap ToBitmap()
        {
            var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                    bitmap.SetPixel(x, y, Data[y * Width + x]);
            }
            return bitmap;
        }
    }
    private readonly CharacterCatalog catalog;
    private readonly PrototypeIndex prototypes;
    private readonly ITextureSource textures;
    private readonly ClothingResolver _clothing;

    public PaperdollRenderer(CharacterCatalog catalog, PrototypeIndex prototypes, ITextureSource textures)
    {
        this.catalog = catalog;
        this.prototypes = prototypes;
        this.textures = textures;
        _clothing = new ClothingResolver(prototypes, Meta);
    }

    private sealed class Slot(IReadOnlyList<string> keys)
    {
        public IReadOnlyList<string> Keys { get; } = keys;
        public SpriteRef? Sprite { get; set; }
        public Rgba Color { get; set; } = Rgba.White;
        public DisplacementRef? Displacement { get; set; }

        /// <summary>The body layer this slot draws for, so clothing can hide it.</summary>
        public string? BodyLayer { get; set; }
    }

    /// <summary>The layers the character is drawn with, bottom to top.</summary>
    /// <param name="outfit">Worn items, slot name to entity id, or null for none.</param>
    public IReadOnlyList<DrawnLayer> Layers(CharacterLook look, IReadOnlyDictionary<string, string>? outfit = null)
    {
        if (!catalog.Species.TryGetValue(look.Species, out var species))
            throw new ArgumentException($"No species {look.Species}.", nameof(look));

        var slots = BaseSlots(species);

        foreach (var organ in species.Organs)
        {
            if (organ.Layer == null || organ.Sprite is not { } sprite)
                continue;
            var index = FindSlot(slots, organ.Layer);
            if (index < 0)
                continue;
            var state = organ.SexStates.TryGetValue(look.Sex, out var sexState) ? sexState : sprite.State;
            slots[index].Sprite = sprite with { State = state };
            slots[index].Color = organ.Layer == "Eyes" ? look.EyeColor : look.SkinColor;
            slots[index].Displacement = organ.Displacement;
            slots[index].BodyLayer = organ.Layer;
        }

        foreach (var organ in species.Organs)
            InsertMarkings(slots, organ, look);

        if (outfit != null)
            Dress(slots, species, look.Sex, outfit);

        return slots
            .Where(s => s.Sprite is { State: not null })
            .Select(s => new DrawnLayer(s.Keys.FirstOrDefault() ?? "", s.Sprite!.Value, s.Color, s.Displacement))
            .ToList();
    }

    /// <summary>What each worn item looks like on this species, by slot.</summary>
    public ClothingVisual? Clothing(string entityId, string slot, SpeciesInfo species) =>
        _clothing.Resolve(entityId, slot, species.ClothingSpeciesId);

    // Worn items' layers go right after their slot's layer, fitted by the species' clothing map for
    // the slot and sex (unless drawn in a species version); body layers they cover are hidden.
    private void Dress(List<Slot> slots, SpeciesInfo species, string sex, IReadOnlyDictionary<string, string> outfit)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (slot, entity) in outfit)
        {
            if (Clothing(entity, slot, species) is not { } visual)
                continue;
            hidden.UnionWith(visual.HiddenLayers);
            var index = slots.FindIndex(s => s.Keys.Contains(slot));
            if (index < 0)
                continue;
            var displacement = species.ClothingDisplacement(slot, sex);
            for (var i = 0; i < visual.Layers.Count; i++)
            {
                var layer = visual.Layers[i];
                slots.Insert(index + 1 + i, new Slot([$"{slot}-{i}"])
                {
                    Sprite = layer.Sprite,
                    Color = layer.Color,
                    Displacement = layer.SpeciesSpecific ? null : displacement,
                });
            }
        }

        // Only layers an organ lets clothing hide, and the layers hidden along with them.
        var hideable = species.Organs.SelectMany(o => o.HideableLayers).ToHashSet(StringComparer.Ordinal);
        var effective = hidden.Where(hideable.Contains).ToHashSet(StringComparer.Ordinal);
        foreach (var organ in species.Organs)
        {
            foreach (var layer in effective.ToList())
            {
                if (organ.DependentHidingLayers.TryGetValue(layer, out var dependents))
                    effective.UnionWith(dependents);
            }
        }
        slots.RemoveAll(s => s.BodyLayer != null && effective.Contains(s.BodyLayer));
    }

    /// <summary>The character as an image, one frame, facing the given way.</summary>
    /// <param name="outfit">Worn items, slot name to entity id, or null for none.</param>
    /// <param name="seconds">How far into their animations animated sprites are, as they loop in the game.</param>
    public SKBitmap Render(CharacterLook look, Direction direction = Direction.South, IReadOnlyDictionary<string, string>? outfit = null, double seconds = 0)
    {
        var frames = new List<(Pixels Frame, Rgba Color)>();
        foreach (var layer in Layers(look, outfit))
        {
            var index = FrameIndex(layer.Sprite, direction, seconds);
            if (LoadFrame(layer.Sprite, direction, index) is not { } frame)
                continue;
            if (layer.Displacement?.For(frame.Width) is { } map)
                frame = Displaced(layer.Sprite, frame, map, direction, index) ?? frame;
            frames.Add((frame, layer.Color));
        }

        var width = frames.Count == 0 ? 32 : frames.Max(f => f.Frame.Width);
        var height = frames.Count == 0 ? 32 : frames.Max(f => f.Frame.Height);
        var result = new Pixels(width, height, new SKColor[width * height]);
        Array.Fill(result.Data, SKColors.Transparent);

        foreach (var (frame, color) in frames)
        {
            var offsetX = (width - frame.Width) / 2;
            var offsetY = (height - frame.Height) / 2;
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var i = (y + offsetY) * width + x + offsetX;
                    result.Data[i] = Over(Tint(frame.Data[y * frame.Width + x], color), result.Data[i]);
                }
            }
        }
        return result.ToBitmap();
    }

    /// <summary>The RSI's meta.json, read once, or null if the fork does not have it.</summary>
    public RsiMeta? Meta(string rsi)
    {
        if (_metas.TryGetValue(rsi, out var cached))
            return cached;
        var data = textures.Read(rsi + "/meta.json");
        return _metas[rsi] = data == null ? null : RsiMeta.Parse(data);
    }

    private List<Slot> BaseSlots(SpeciesInfo species)
    {
        var slots = new List<Slot>();
        var doll = species.DollPrototype != null ? prototypes.Resolve("entity", species.DollPrototype) : null;
        var sprite = doll != null && doll.Children.TryGetValue(new YamlScalarNode("components"), out var components) && components is YamlSequenceNode list
            ? list.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Scalar(c, "type") == "Sprite")
            : null;
        if (sprite == null || !sprite.Children.TryGetValue(new YamlScalarNode("layers"), out var layers) || layers is not YamlSequenceNode layerList)
            return slots;

        foreach (var layer in layerList.Children.OfType<YamlMappingNode>())
        {
            var keys = layer.Children.TryGetValue(new YamlScalarNode("map"), out var map) && map is YamlSequenceNode mapList
                ? mapList.Children.OfType<YamlScalarNode>().Select(k => k.Value!).ToList()
                : [];
            slots.Add(new Slot(keys));
        }
        return slots;
    }

    private void InsertMarkings(List<Slot> slots, OrganInfo organ, CharacterLook look)
    {
        if (organ.MarkingLayers.Count == 0 || !look.Markings.TryGetValue(organ.Category, out var byLayer))
            return;

        catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup ?? "", out var group);
        foreach (var layer in organ.MarkingLayers)
        {
            if (!byLayer.TryGetValue(layer, out var entries))
                continue;

            foreach (var (marking, entry) in Resolve(entries, look, group))
            {
                var index = FindSlot(slots, marking.Layer);
                if (index < 0)
                    continue;
                var displacement = marking.CanBeDisplaced && organ.MarkingsDisplacement.TryGetValue(marking.Layer, out var map) ? map : null;
                for (var i = 0; i < marking.Sprites.Count; i++)
                {
                    var color = i < entry.Colors.Count ? entry.Colors[i] : Rgba.White;
                    slots.Insert(index + i + 1, new Slot([$"{marking.Id}-{marking.Sprites[i].State}"])
                    {
                        Sprite = marking.Sprites[i],
                        Color = color,
                        Displacement = displacement,
                        BodyLayer = marking.Layer,
                    });
                }
            }
        }
    }

    // Unforced markings keep their colours; forced ones, and ones on skin-matching layers, get
    // their rule's colours and go after the others.
    private List<(MarkingInfo, MarkingEntry)> Resolve(List<MarkingEntry> entries, CharacterLook look, MarkingsGroupInfo? group)
    {
        var result = new List<(MarkingInfo, MarkingEntry)>();
        var forced = new List<(MarkingInfo, MarkingEntry)>();
        foreach (var entry in entries)
        {
            if (!catalog.Markings.TryGetValue(entry.Id, out var marking))
                continue;
            if (!marking.ForcedColoring && !MatchesSkin(group, marking.Layer))
                result.Add((marking, entry));
            else
                forced.Add((marking, entry));
        }

        var unforced = result.Select(r => r.Item2).ToList();
        foreach (var (marking, entry) in forced)
        {
            var colors = MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, unforced);
            if (MatchesSkin(group, marking.Layer))
            {
                var alpha = LayerAlpha(group, marking.Layer);
                colors = colors.Select(_ => look.SkinColor.WithAlpha(alpha)).ToList();
            }
            result.Add((marking, entry with { Colors = colors }));
        }
        return result;
    }

    private bool MatchesSkin(MarkingsGroupInfo? group, string layer) =>
        Appearance(group, layer) is { } appearance && Scalar(appearance, "matchSkin") is "true" or "True";

    private float LayerAlpha(MarkingsGroupInfo? group, string layer) =>
        Appearance(group, layer) is { } appearance && float.TryParse(Scalar(appearance, "layerAlpha"), System.Globalization.CultureInfo.InvariantCulture, out var alpha) ? alpha : 1f;

    private YamlMappingNode? Appearance(MarkingsGroupInfo? group, string layer)
    {
        if (group == null || prototypes.Resolve("markingsGroup", group.Id) is not { } node)
            return null;
        if (!node.Children.TryGetValue(new YamlScalarNode("appearances"), out var appearances) || appearances is not YamlMappingNode map)
            return null;
        return map.Children.TryGetValue(new YamlScalarNode(LayerPrefix + layer), out var value) ? value as YamlMappingNode : null;
    }

    private static int FindSlot(List<Slot> slots, string layer) =>
        slots.FindIndex(s => s.Keys.Contains(LayerPrefix + layer));

    /// <summary>Whether anything drawn for the character moves when facing this way.</summary>
    public bool IsAnimated(CharacterLook look, Direction direction, IReadOnlyDictionary<string, string>? outfit = null) =>
        Layers(look, outfit).Any(layer => State(layer.Sprite)?.IsAnimated(direction) == true);

    private RsiState? State(SpriteRef sprite) =>
        sprite.State != null && Meta(sprite.Rsi) is { } meta && meta.States.TryGetValue(sprite.State, out var state) ? state : null;

    private int FrameIndex(SpriteRef sprite, Direction direction, double seconds) =>
        seconds > 0 && State(sprite) is { } state ? state.FrameAt(direction, seconds) : 0;

    private Pixels? LoadFrame(SpriteRef sprite, Direction direction, int index = 0)
    {
        if (sprite.State == null)
            return null;
        var key = (sprite.Rsi, sprite.State, direction, index);
        if (_frames.TryGetValue(key, out var cached))
            return cached;
        Pixels? pixels = null;
        if (Meta(sprite.Rsi) is { } meta && meta.States.TryGetValue(sprite.State, out var state)
            && textures.Read($"{sprite.Rsi}/{sprite.State}.png") is { } png)
        {
            using var frame = meta.Frame(state, png, direction, index);
            if (frame != null)
                pixels = Pixels.From(frame);
        }
        return _frames[key] = pixels;
    }

    // Displacement maps do not animate, so the map's first frame reshapes every frame.
    private Pixels? Displaced(SpriteRef sprite, Pixels frame, SpriteRef map, Direction direction, int index)
    {
        var key = (sprite, map, direction, index);
        if (_displaced.TryGetValue(key, out var cached))
            return cached;
        return _displaced[key] = LoadFrame(map, direction) is { } mapPixels ? Displace(frame, mapPixels) : null;
    }

    /// <summary>
    /// Reshapes a frame as the game's displacement shader does: each output pixel takes the source
    /// pixel moved by the map's (red - 128, green - 128), with the map's alpha as a mask.
    /// </summary>
    public static SKBitmap Displace(SKBitmap frame, SKBitmap map) => Displace(Pixels.From(frame), Pixels.From(map)).ToBitmap();

    private static Pixels Displace(Pixels frame, Pixels map)
    {
        var result = new Pixels(frame.Width, frame.Height, new SKColor[frame.Width * frame.Height]);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (x >= map.Width || y >= map.Height)
                {
                    result.Data[y * frame.Width + x] = SKColors.Transparent;
                    continue;
                }
                var d = map.Data[y * map.Width + x];
                var sx = x + d.Red - 128;
                var sy = y + d.Green - 128;
                var source = sx >= 0 && sy >= 0 && sx < frame.Width && sy < frame.Height ? frame.Data[sy * frame.Width + sx] : SKColors.Transparent;
                result.Data[y * frame.Width + x] = source.WithAlpha((byte)Math.Round(source.Alpha * (d.Alpha / 255f)));
            }
        }
        return result;
    }

    private static SKColor Tint(SKColor pixel, Rgba color) => new(
        (byte)Math.Round(pixel.Red * color.R),
        (byte)Math.Round(pixel.Green * color.G),
        (byte)Math.Round(pixel.Blue * color.B),
        (byte)Math.Round(pixel.Alpha * color.A));

    // Normal alpha blending of a pixel over another.
    private static SKColor Over(SKColor top, SKColor bottom)
    {
        if (top.Alpha == 255 || bottom.Alpha == 0)
            return top;
        if (top.Alpha == 0)
            return bottom;
        var ta = top.Alpha / 255f;
        var ba = bottom.Alpha / 255f * (1 - ta);
        var a = ta + ba;
        byte Mix(byte t, byte b) => (byte)Math.Round((t * ta + b * ba) / a);
        return new SKColor(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), (byte)Math.Round(a * 255));
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;
}
