// The clothing layer rules follow Space Station 14's Content.Client/Clothing/ClientClothingSystem.cs,
// Copyright (c) 2017-2026 Space Wizards Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Outfits;

/// <summary>One sprite layer an item adds to the character.</summary>
public sealed record ClothingLayer(SpriteRef Sprite, Rgba Color, bool SpeciesSpecific);

/// <summary>What an item looks like worn in a slot, and which body layers it hides.</summary>
public sealed record ClothingVisual(string Entity, IReadOnlyList<ClothingLayer> Layers, IReadOnlySet<string> HiddenLayers);

/// <summary>
/// How a worn item is drawn:
/// <list type="bullet">
/// <item>The item's <c>clothingVisuals</c> for the slot (a species version first, such as
/// <c>jumpsuit-vox</c>), each layer from its own sprite or the item's.</item>
/// <item>Otherwise one layer: the item's clothing sprite (or its own sprite) in state
/// <c>equipped-&lt;SLOT&gt;</c>, with the item's <c>equippedPrefix</c> or <c>equippedState</c>.</item>
/// <item>A species version of a state (<c>equipped-INNERCLOTHING-vox</c>) is used when the sprite
/// has one; such layers are not reshaped by the species' displacement maps.</item>
/// </list>
/// </summary>
public sealed class ClothingResolver(PrototypeIndex prototypes, Func<string, RsiMeta?> meta)
{
    /// <summary>Slot names to the upper-case names clothing sprite states use.</summary>
    public static readonly IReadOnlyDictionary<string, string> SlotStates = new Dictionary<string, string>
    {
        ["head"] = "HELMET",
        ["eyes"] = "EYES",
        ["ears"] = "EARS",
        ["mask"] = "MASK",
        ["outerClothing"] = "OUTERCLOTHING",
        ["jumpsuit"] = "INNERCLOTHING",
        ["neck"] = "NECK",
        ["back"] = "BACKPACK",
        ["belt"] = "BELT",
        ["gloves"] = "HAND",
        ["shoes"] = "FEET",
        ["id"] = "IDCARD",
        ["pocket1"] = "POCKET1",
        ["pocket2"] = "POCKET2",
        ["suitstorage"] = "SUITSTORAGE",
    };

    public ClothingVisual? Resolve(string entityId, string slot, string? speciesId)
    {
        if (prototypes.Resolve("entity", entityId) is not { } entity)
            return null;
        var clothing = Component(entity, "Clothing");
        if (clothing == null)
            return null;

        var clothingRsi = Tex(Str(clothing, "sprite"));
        var itemRsi = Tex(Str(Component(entity, "Sprite"), "sprite"));
        var baseRsi = clothingRsi ?? itemRsi;
        var layers = new List<ClothingLayer>();

        var visuals = Map(clothing, "clothingVisuals");
        var listed = (speciesId != null ? Seq(visuals, $"{slot}-{speciesId}") : null) ?? Seq(visuals, slot);
        if (listed != null)
        {
            foreach (var data in listed.Children.OfType<YamlMappingNode>())
            {
                var rsi = Tex(Str(data, "sprite")) ?? itemRsi ?? clothingRsi;
                var state = Str(data, "state");
                if (rsi == null || state == null || Str(data, "visible") is "false" or "False")
                    continue;
                var species = false;
                if (speciesId != null && !state.EndsWith(speciesId, StringComparison.Ordinal) && HasState(Str(data, "sprite") != null ? rsi : baseRsi ?? rsi, $"{state}-{speciesId}"))
                {
                    state = $"{state}-{speciesId}";
                    species = true;
                }
                layers.Add(new ClothingLayer(new SpriteRef(rsi, state), Color(Str(data, "color")), species || (speciesId != null && state.EndsWith(speciesId, StringComparison.Ordinal))));
            }
        }
        else if (baseRsi != null)
        {
            var slotName = SlotStates.GetValueOrDefault(slot, slot);
            var state = Str(clothing, "equippedState")
                ?? (Str(clothing, "equippedPrefix") is { Length: > 0 } prefix ? $"{prefix}-equipped-{slotName}" : $"equipped-{slotName}");
            if (speciesId != null && HasState(baseRsi, $"{state}-{speciesId}"))
                layers.Add(new ClothingLayer(new SpriteRef(baseRsi, $"{state}-{speciesId}"), Rgba.White, true));
            else if (HasState(baseRsi, state))
                layers.Add(new ClothingLayer(new SpriteRef(baseRsi, state), Rgba.White, false));
        }

        return new ClothingVisual(entityId, layers, HiddenLayers(entity));
    }

    /// <summary>Every sprite folder an item could draw from, for fetching before drawing.</summary>
    public IEnumerable<string> SpriteFolders(string entityId)
    {
        if (prototypes.Resolve("entity", entityId) is not { } entity)
            yield break;
        var clothing = Component(entity, "Clothing");
        if (clothing == null)
            yield break;
        if (Tex(Str(clothing, "sprite")) is { } clothingRsi)
            yield return clothingRsi;
        if (Tex(Str(Component(entity, "Sprite"), "sprite")) is { } itemRsi)
            yield return itemRsi;
        if (Map(clothing, "clothingVisuals") is { } visuals)
        {
            foreach (var layers in visuals.Children.Values.OfType<YamlSequenceNode>())
            {
                foreach (var data in layers.Children.OfType<YamlMappingNode>())
                {
                    if (Tex(Str(data, "sprite")) is { } rsi)
                        yield return rsi;
                }
            }
        }
    }

    // HideLayerClothing: "slots" (older) and "layers" (layer: slots it hides from) name body layers.
    private static HashSet<string> HiddenLayers(YamlMappingNode entity)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        if (Component(entity, "HideLayerClothing") is not { } hide)
            return hidden;
        if (hide.Children.TryGetValue(new YamlScalarNode("slots"), out var slots) && slots is YamlSequenceNode list)
            hidden.UnionWith(list.Children.OfType<YamlScalarNode>().Select(s => LayerName(s.Value!)));
        if (Map(hide, "layers") is { } layers)
            hidden.UnionWith(layers.Children.Keys.OfType<YamlScalarNode>().Select(k => LayerName(k.Value!)));
        return hidden;
    }

    private bool HasState(string rsi, string state) => meta(rsi)?.States.ContainsKey(state) == true;

    private static string LayerName(string value) =>
        value.StartsWith("enum.HumanoidVisualLayers.", StringComparison.Ordinal) ? value["enum.HumanoidVisualLayers.".Length..] : value;

    private static string? Tex(string? path) => path == null ? null : CharacterCatalog.TexturePath(path);

    private static Rgba Color(string? hex) => Rgba.TryParse(hex, out var color) ? color : Rgba.White;

    private static YamlMappingNode? Component(YamlMappingNode? entity, string type)
    {
        if (entity == null || !entity.Children.TryGetValue(new YamlScalarNode("components"), out var components) || components is not YamlSequenceNode list)
            return null;
        return list.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Str(c, "type") == type);
    }

    private static string? Str(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private static YamlMappingNode? Map(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlMappingNode : null;

    private static YamlSequenceNode? Seq(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlSequenceNode : null;
}
