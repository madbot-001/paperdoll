using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;

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
        if (SpriteInfo.Of(prototypes, entityId) is not { } sprite)
            return [];
        // The engine multiplies each layer's colour by the sprite's.
        var spriteColor = sprite.Color ?? Rgba.White;
        return sprite.Layers
            .Where(l => l.Visible && l.State != null && l.Rsi != null)
            .Select(l => new DrawnLayer("", new SpriteRef(l.Rsi!, l.State!), Multiply(spriteColor, l.Color ?? Rgba.White), Unshaded: l.Shader == "unshaded"))
            .ToList();
    }

    /// <summary>Every sprite folder the entity draws from, for fetching.</summary>
    public static IEnumerable<string> SpriteFolders(PrototypeIndex prototypes, string entityId) =>
        Layers(prototypes, entityId).Select(l => l.Sprite.Rsi).Distinct(StringComparer.Ordinal);

    /// <summary>The engine's Color * Color: channel by channel, alpha too.</summary>
    public static Rgba Multiply(Rgba a, Rgba b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
}
