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
    /// <param name="firstInCategory">
    /// Old appearance model only: the first marking the character has in a category. The old
    /// model's colouring differs a little: a definition without a type follows the skin, one whose
    /// type gives nothing falls back to its fallback colour, and category colouring copies that
    /// category's first marking.
    /// </param>
    public static List<Rgba> LayerColors(MarkingInfo marking, Rgba? skin, Rgba? eyes, IReadOnlyList<MarkingEntry> otherMarkings,
        Func<string, MarkingEntry?>? firstInCategory = null)
    {
        var context = new Context(skin, eyes, otherMarkings, firstInCategory);
        var coloring = marking.Node.Children.TryGetValue(new YamlScalarNode("coloring"), out var node) ? node as YamlMappingNode : null;
        var defaultDef = Definition(Child(coloring, "default"));
        var layers = Child(coloring, "layers") as YamlMappingNode;
        var defaultColor = defaultDef.Color(context);

        var colors = new List<Rgba>();
        foreach (var sprite in marking.Sprites)
        {
            if (layers != null && sprite.State != null
                && layers.Children.TryGetValue(new YamlScalarNode(sprite.State), out var layerNode))
                colors.Add(Definition(layerNode).Color(context));
            else
                colors.Add(defaultColor);
        }
        return colors;
    }

    private sealed record Context(Rgba? Skin, Rgba? Eyes, IReadOnlyList<MarkingEntry> Others, Func<string, MarkingEntry?>? FirstInCategory)
    {
        public bool Old => FirstInCategory != null;
    }

    /// <summary>
    /// Colours for a randomly picked marking. Sprites whose colouring names a type use it; the
    /// rest get <paramref name="untyped"/>.
    /// </summary>
    public static List<Rgba> RandomColors(MarkingInfo marking, Rgba skin, Rgba eyes, IReadOnlyList<MarkingEntry> otherMarkings, Func<Rgba> untyped)
    {
        var coloring = marking.Node.Children.TryGetValue(new YamlScalarNode("coloring"), out var node) ? node as YamlMappingNode : null;
        var defaultDef = Definition(Child(coloring, "default"));
        var layers = Child(coloring, "layers") as YamlMappingNode;
        var context = new Context(skin, eyes, otherMarkings, null);
        var colors = new List<Rgba>();
        foreach (var sprite in marking.Sprites)
        {
            var definition = layers != null && sprite.State != null && layers.Children.TryGetValue(new YamlScalarNode(sprite.State), out var layerNode)
                ? Definition(layerNode)
                : defaultDef;
            colors.Add(definition.Type != null ? definition.Color(context) : untyped());
        }
        return colors;
    }

    private sealed record ColoringDefinition(YamlNode? Type, IReadOnlyList<YamlNode>? FallbackTypes, Rgba FallbackColor)
    {
        public Rgba Color(Context context)
        {
            // The old model's type defaults to skin colouring, the new model's to none.
            var color = Type != null ? TypeColor(Type, context) : context.Old ? context.Skin : null;
            if (color == null)
            {
                // The new model's default fallback is the skin colour; the old model has none.
                if (FallbackTypes == null)
                    color = context.Old ? null : context.Skin;
                else
                {
                    foreach (var fallback in FallbackTypes)
                    {
                        color = TypeColor(fallback, context);
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
    private static Rgba? TypeColor(YamlNode node, Context context)
    {
        var tag = node.Tag.IsEmpty ? "" : node.Tag.Value;
        var map = node as YamlMappingNode;
        Rgba? color = tag switch
        {
            "!type:SkinColoring" => context.Skin,
            "!type:EyeColoring" => context.Eyes,
            "!type:TattooColoring" => context.Skin is { } s ? Tattoo(s) : null,
            "!type:SimpleColoring" => Child(map, "color") is YamlScalarNode { Value: { } hex } && Rgba.TryParse(hex, out var simple) ? simple : Rgba.White,
            "!type:CategoryColoring" => CategoryColor(map, context),
            _ => null,
        };

        if (color is { } value && Child(map, "negative") is YamlScalarNode { Value: "true" or "True" })
            return value with { R = 1 - value.R, G = 1 - value.G, B = 1 - value.B };
        return color;
    }

    // The new model takes the first colour of the first other marking, whatever its category; the
    // old model the first colour of the named category's first marking.
    private static Rgba? CategoryColor(YamlMappingNode? map, Context context)
    {
        if (context.FirstInCategory is { } first)
            return Child(map, "category") is YamlScalarNode { Value: { } category } && first(category) is { Colors.Count: > 0 } marking ? marking.Colors[0] : null;
        return context.Others.Count > 0 && context.Others[0].Colors.Count > 0 ? context.Others[0].Colors[0] : null;
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
