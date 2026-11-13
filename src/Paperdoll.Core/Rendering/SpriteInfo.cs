using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// One layer of a <c>Sprite</c> component as the prototype writes it
/// (<c>RobustToolbox/Robust.Shared/GameObjects/Components/Renderable/SpriteLayerData.cs</c>). Paths
/// are relative to <c>Textures/</c>. <see cref="Shader"/> is kept so lighting can leave
/// <c>unshaded</c> layers lit.
/// </summary>
public sealed record SpriteLayerInfo(string? Rsi, string? State, string? Texture, Rgba? Color, bool Visible,
    float OffsetX, float OffsetY, float ScaleX, float ScaleY, float Rotation, IReadOnlyList<string> MapKeys, string? Shader)
{
    /// <summary>Drawn at full brightness whatever the light (<c>shader: unshaded</c>).</summary>
    public bool Unshaded => Shader == "unshaded";
}

/// <summary>
/// A <c>Sprite</c> component's fields that decide where and how it draws
/// (<c>RobustToolbox/Robust.Client/GameObjects/Components/Renderable/SpriteComponent.cs</c> for the
/// fields and their defaults). <see cref="Rsi"/> is the sprite's own RSI, relative to <c>Textures/</c>.
/// </summary>
public sealed record SpriteInfo(int DrawDepth, float OffsetX, float OffsetY, bool NoRot, bool SnapCardinals, Rgba? Color,
    float ScaleX, float ScaleY, float Rotation, bool Visible, IReadOnlyList<SpriteLayerInfo> Layers)
{
    public string? Rsi { get; init; }

    /// <summary>An entity prototype's sprite, parents applied; null when it has none.</summary>
    public static SpriteInfo? Of(PrototypeIndex prototypes, string entityId) =>
        prototypes.Resolve("entity", entityId) is { } entity ? Read(PrototypeYaml.Component(entity, "Sprite")) : null;

    /// <summary>
    /// Reads a <c>Sprite</c> component. A field that cannot be read takes its default; nothing
    /// here throws for a broken prototype.
    /// </summary>
    public static SpriteInfo? Read(YamlMappingNode? sprite)
    {
        if (sprite == null)
            return null;
        var baseRsi = Path(PrototypeYaml.Scalar(sprite, "sprite"));
        var (offsetX, offsetY) = PrototypeYaml.Vector(PrototypeYaml.Scalar(sprite, "offset")) ?? (0, 0);
        var (scaleX, scaleY) = PrototypeYaml.Vector(PrototypeYaml.Scalar(sprite, "scale")) ?? (1, 1);

        var layers = new List<SpriteLayerInfo>();
        if (PrototypeYaml.Sequence(sprite, "layers") is { Children.Count: > 0 } layerList)
        {
            foreach (var layer in layerList.Children.OfType<YamlMappingNode>())
                layers.Add(ReadLayer(layer, baseRsi));
        }
        else
        {
            // With no layers, a top-level state or texture makes the one layer
            // (SpriteComponent.AfterDeserialization); with layers they are ignored.
            var state = Blank(PrototypeYaml.Scalar(sprite, "state"));
            var texture = Blank(PrototypeYaml.Scalar(sprite, "texture"));
            if (state != null || texture != null)
                layers.Add(new SpriteLayerInfo(baseRsi, state, state == null ? Path(texture) : null, null, true, 0, 0, 1, 1, 0, [], null));
        }

        return new SpriteInfo(
            DrawDepths.Parse(PrototypeYaml.Scalar(sprite, "drawdepth")) ?? DrawDepths.Default,
            offsetX, offsetY,
            PrototypeYaml.Bool(sprite, "noRot", false),
            PrototypeYaml.Bool(sprite, "snapCardinals", false),
            ParseColor(PrototypeYaml.Scalar(sprite, "color")),
            scaleX, scaleY,
            (float)(PrototypeYaml.Angle(PrototypeYaml.Scalar(sprite, "rotation")) ?? 0),
            PrototypeYaml.Bool(sprite, "visible", true),
            layers)
        {
            Rsi = baseRsi,
        };
    }

    // SpriteComponent.LayerSetData: a layer's own RSI, else the sprite's; a texture only counts
    // when there is no state.
    private static SpriteLayerInfo ReadLayer(YamlMappingNode layer, string? baseRsi)
    {
        var state = Blank(PrototypeYaml.Scalar(layer, "state"));
        var (offsetX, offsetY) = PrototypeYaml.Vector(PrototypeYaml.Scalar(layer, "offset")) ?? (0, 0);
        var (scaleX, scaleY) = PrototypeYaml.Vector(PrototypeYaml.Scalar(layer, "scale")) ?? (1, 1);
        return new SpriteLayerInfo(
            Path(Blank(PrototypeYaml.Scalar(layer, "sprite"))) ?? baseRsi,
            state,
            state == null ? Path(Blank(PrototypeYaml.Scalar(layer, "texture"))) : null,
            ParseColor(PrototypeYaml.Scalar(layer, "color")),
            PrototypeYaml.Bool(layer, "visible", true),
            offsetX, offsetY, scaleX, scaleY,
            (float)(PrototypeYaml.Angle(PrototypeYaml.Scalar(layer, "rotation")) ?? 0),
            PrototypeYaml.Strings(layer, "map"),
            Blank(PrototypeYaml.Scalar(layer, "shader")));
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? Path(string? path) => path == null ? null : CharacterCatalog.TexturePath(path);

    private static Rgba? ParseColor(string? text) => Rgba.TryParse(text, out var color) ? color : null;
}

/// <summary>Reading prototype fields as the engine's serializers read them; anything unreadable gives null or the fallback.</summary>
internal static class PrototypeYaml
{
    public static string? Scalar(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    public static YamlSequenceNode? Sequence(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlSequenceNode : null;

    public static YamlMappingNode? Mapping(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlMappingNode : null;

    public static IReadOnlyList<string> Strings(YamlMappingNode? node, string key) =>
        Sequence(node, key)?.Children.OfType<YamlScalarNode>().Select(s => s.Value).OfType<string>().ToList() ?? [];

    public static bool Bool(YamlMappingNode? node, string key, bool fallback) =>
        bool.TryParse(Scalar(node, key), out var value) ? value : fallback;

    /// <summary>An entity's component of a type, or null.</summary>
    public static YamlMappingNode? Component(YamlMappingNode entity, string type) =>
        Sequence(entity, "components")?.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Scalar(c, "type") == type);

    public static float? Float(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// An "x,y" pair as <c>Vector2Serializer</c> reads it
    /// (<c>RobustToolbox/Robust.Shared/Serialization/TypeSerializers/Implementations/Vector2Serializer.cs</c>).
    /// </summary>
    public static (float X, float Y)? Vector(string? text)
    {
        var parts = text?.Split(',');
        return parts is { Length: 2 } && Float(parts[0].Trim()) is { } x && Float(parts[1].Trim()) is { } y ? (x, y) : null;
    }

    /// <summary>
    /// An angle in radians, as <c>AngleSerializer</c> reads it: degrees, or radians when it ends
    /// in "rad" (<c>RobustToolbox/Robust.Shared/Serialization/TypeSerializers/Implementations/AngleSerializer.cs</c>).
    /// </summary>
    public static double? Angle(string? text)
    {
        if (text == null)
            return null;
        if (text.EndsWith("rad", StringComparison.Ordinal))
            return double.TryParse(text[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rad) ? rad : null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) ? deg * Math.PI / 180.0 : null;
    }
}
