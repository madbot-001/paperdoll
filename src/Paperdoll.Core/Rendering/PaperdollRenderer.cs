// The layer order and colouring follow Space Station 14's Content.Client/Body/VisualBodySystem.cs
// and Content.Shared/Body/SharedVisualBodySystem.cs, Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Characters;
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
public sealed record DrawnLayer(string Key, SpriteRef Sprite, Rgba Color);

/// <summary>
/// Draws a character from a fork's data as the game's lobby preview does, facing one way:
/// <list type="bullet">
/// <item>Layers start as the species doll's sprite layers, in order.</item>
/// <item>Each organ fills the layer named by its <c>VisualOrgan</c>, tinted with the skin colour
/// (the eye colour on the eyes layer), with its per-sex state if it has one.</item>
/// <item>Each marking is inserted just above its body part's layer, so a marking applied later
/// on the same layer ends up below earlier ones. Markings with forced colours, or on layers that
/// match the skin, are coloured by their rules and applied after the others.</item>
/// <item>Colours multiply the sprite's pixels. Frames are centred on each other.</item>
/// </list>
/// Not drawn yet: displacement maps (which reshape, for example, hair on Vox), shaders, and the
/// nudity-censoring defaults.
/// </summary>
public sealed class PaperdollRenderer(CharacterCatalog catalog, PrototypeIndex prototypes, ITextureSource textures)
{
    private const string LayerPrefix = "enum.HumanoidVisualLayers.";
    private readonly Dictionary<string, RsiMeta?> _metas = new(StringComparer.Ordinal);

    private sealed class Slot(IReadOnlyList<string> keys)
    {
        public IReadOnlyList<string> Keys { get; } = keys;
        public SpriteRef? Sprite { get; set; }
        public Rgba Color { get; set; } = Rgba.White;
    }

    /// <summary>The layers the character is drawn with, bottom to top.</summary>
    public IReadOnlyList<DrawnLayer> Layers(CharacterLook look)
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
        }

        foreach (var organ in species.Organs)
            InsertMarkings(slots, organ, look);

        return slots
            .Where(s => s.Sprite is { State: not null })
            .Select(s => new DrawnLayer(s.Keys.FirstOrDefault() ?? "", s.Sprite!.Value, s.Color))
            .ToList();
    }

    /// <summary>The character as an image, one frame, facing the given way.</summary>
    public SKBitmap Render(CharacterLook look, Direction direction = Direction.South)
    {
        var frames = new List<(SKBitmap Frame, Rgba Color)>();
        foreach (var layer in Layers(look))
        {
            if (LoadFrame(layer.Sprite, direction) is { } frame)
                frames.Add((frame, layer.Color));
        }

        var width = frames.Count == 0 ? 32 : frames.Max(f => f.Frame.Width);
        var height = frames.Count == 0 ? 32 : frames.Max(f => f.Frame.Height);
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        result.Erase(SKColors.Transparent);

        foreach (var (frame, color) in frames)
        {
            var offsetX = (width - frame.Width) / 2;
            var offsetY = (height - frame.Height) / 2;
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                    result.SetPixel(x + offsetX, y + offsetY, Over(Tint(frame.GetPixel(x, y), color), result.GetPixel(x + offsetX, y + offsetY)));
            }
            frame.Dispose();
        }
        return result;
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
                for (var i = 0; i < marking.Sprites.Count; i++)
                {
                    var color = i < entry.Colors.Count ? entry.Colors[i] : Rgba.White;
                    slots.Insert(index + i + 1, new Slot([$"{marking.Id}-{marking.Sprites[i].State}"]) { Sprite = marking.Sprites[i], Color = color });
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

    private SKBitmap? LoadFrame(SpriteRef sprite, Direction direction)
    {
        if (sprite.State == null || Meta(sprite.Rsi) is not { } meta || !meta.States.TryGetValue(sprite.State, out var state))
            return null;
        var png = textures.Read($"{sprite.Rsi}/{sprite.State}.png");
        return png == null ? null : meta.Frame(state, png, direction);
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
