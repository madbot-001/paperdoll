// Ported from Space Station 14 (Content.Shared/Humanoid/Markings/MarkingColoring.cs and
// Content.Shared/Humanoid/Markings/ColoringTypes/*.cs), Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Characters;

/// <summary>A marking on a character, with one colour per sprite.</summary>
public sealed record MarkingEntry(string Id, IReadOnlyList<Rgba> Colors);

/// <summary>
/// A marking's default colours, from its <c>coloring</c> block: per sprite, a coloring type
/// (skin, eyes, tattoo, a fixed colour, or another marking's colour), fallback types when that
/// gives nothing, and a fallback colour. Without a block, markings follow the skin.
/// </summary>
public static class MarkingColoring
{
    /// <summary>One colour per sprite of the marking.</summary>
    public static List<Rgba> LayerColors(MarkingInfo marking, Rgba? skin, Rgba? eyes, IReadOnlyList<MarkingEntry> otherMarkings)
    {
        var coloring = marking.Node.Children.TryGetValue(new YamlScalarNode("coloring"), out var node) ? node as YamlMappingNode : null;
        var defaultDef = Definition(Child(coloring, "default"));
        var layers = Child(coloring, "layers") as YamlMappingNode;
        var defaultColor = defaultDef.Color(skin, eyes, otherMarkings);

        var colors = new List<Rgba>();
        foreach (var sprite in marking.Sprites)
        {
            if (layers != null && sprite.State != null
                && layers.Children.TryGetValue(new YamlScalarNode(sprite.State), out var layerNode))
                colors.Add(Definition(layerNode).Color(skin, eyes, otherMarkings));
            else
                colors.Add(defaultColor);
        }
        return colors;
    }

    private sealed record ColoringDefinition(YamlNode? Type, IReadOnlyList<YamlNode>? FallbackTypes, Rgba FallbackColor)
    {
        public Rgba Color(Rgba? skin, Rgba? eyes, IReadOnlyList<MarkingEntry> others)
        {
            var color = Type != null ? TypeColor(Type, skin, eyes, others) : null;
            if (color == null)
            {
                // The game's default fallback is the skin colour.
                if (FallbackTypes == null)
                    color = skin;
                else
                {
                    foreach (var fallback in FallbackTypes)
                    {
                        color = TypeColor(fallback, skin, eyes, others);
                        if (color != null)
                            break;
                    }
                }
            }
            return color ?? FallbackColor;
        }
    }

    private static ColoringDefinition Definition(YamlNode? node)
    {
        if (node is not YamlMappingNode map)
            return new ColoringDefinition(null, null, Rgba.White);
        var fallbacks = Child(map, "fallbackTypes") is YamlSequenceNode seq ? seq.Children.ToList() : null;
        var fallbackColor = Child(map, "fallbackColor") is YamlScalarNode { Value: { } hex } && Rgba.TryParse(hex, out var c) ? c : Rgba.White;
        return new ColoringDefinition(Child(map, "type"), fallbacks, fallbackColor);
    }

    // A "!type:SkinColoring" node, as a tagged scalar or mapping.
    private static Rgba? TypeColor(YamlNode node, Rgba? skin, Rgba? eyes, IReadOnlyList<MarkingEntry> others)
    {
        var tag = node.Tag.IsEmpty ? "" : node.Tag.Value;
        var map = node as YamlMappingNode;
        Rgba? color = tag switch
        {
            "!type:SkinColoring" => skin,
            "!type:EyeColoring" => eyes,
            "!type:TattooColoring" => skin is { } s ? Tattoo(s) : null,
            "!type:SimpleColoring" => Child(map, "color") is YamlScalarNode { Value: { } hex } && Rgba.TryParse(hex, out var simple) ? simple : Rgba.White,
            // As in the game: the first colour of the first other marking, whatever its category.
            "!type:CategoryColoring" => others.Count > 0 && others[0].Colors.Count > 0 ? others[0].Colors[0] : null,
            _ => null,
        };

        if (color is { } value && Child(map, "negative") is YamlScalarNode { Value: "true" or "True" })
            return value with { R = 1 - value.R, G = 1 - value.G, B = 1 - value.B };
        return color;
    }

    // Skin colour much darker: value set to 0.4.
    private static Rgba Tattoo(Rgba skin)
    {
        var (h, s, _, a) = skin.ToHsv();
        return Rgba.FromHsv(h, s, 0.40f, a);
    }

    private static YamlNode? Child(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
}
