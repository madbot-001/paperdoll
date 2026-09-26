using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// How a plain entity looks from its <c>Sprite</c> component: its layers in order, each from its own
/// sprite or the entity's, skipping hidden ones. Used for jobs the lobby shows as their own body,
/// such as a borg.
/// </summary>
public static class EntitySprite
{
    public static IReadOnlyList<DrawnLayer> Layers(PrototypeIndex prototypes, string entityId)
    {
        if (Component(prototypes, entityId, "Sprite") is not { } sprite)
            return [];
        var baseRsi = Tex(Str(sprite, "sprite"));
        var result = new List<DrawnLayer>();
        if (sprite.Children.TryGetValue(new YamlScalarNode("layers"), out var node) && node is YamlSequenceNode layers)
        {
            foreach (var layer in layers.Children.OfType<YamlMappingNode>())
            {
                if (Str(layer, "visible") is "false" or "False" || Str(layer, "state") is not { } state)
                    continue;
                if ((Tex(Str(layer, "sprite")) ?? baseRsi) is { } rsi)
                    result.Add(new DrawnLayer("", new SpriteRef(rsi, state), Color(Str(layer, "color"))));
            }
        }
        else if (baseRsi != null && Str(sprite, "state") is { } state)
            result.Add(new DrawnLayer("", new SpriteRef(baseRsi, state), Color(Str(sprite, "color"))));
        return result;
    }

    /// <summary>Every sprite folder the entity draws from, for fetching.</summary>
    public static IEnumerable<string> SpriteFolders(PrototypeIndex prototypes, string entityId) =>
        Layers(prototypes, entityId).Select(l => l.Sprite.Rsi).Distinct(StringComparer.Ordinal);

    private static YamlMappingNode? Component(PrototypeIndex prototypes, string entityId, string type) =>
        prototypes.Resolve("entity", entityId) is { } entity && entity.Children.TryGetValue(new YamlScalarNode("components"), out var list) && list is YamlSequenceNode components
            ? components.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Str(c, "type") == type)
            : null;

    private static string? Str(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private static string? Tex(string? path) => path == null ? null : CharacterCatalog.TexturePath(path);

    private static Rgba Color(string? hex) => Rgba.TryParse(hex, out var color) ? color : Rgba.White;
}
