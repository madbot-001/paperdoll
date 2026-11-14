// Ported from Space Station 14: which lights its systems switch on (Content.Shared/Light/EntitySystems/
// SharedPoweredLightSystem.cs, Content.Server/Light/EntitySystems/EmergencyLightSystem.cs and
// LitOnPoweredSystem.cs), Copyright (c) 2017-2026 Space Wizards Federation, and RobustToolbox's light
// placement and limits (Robust.Client/Graphics/Clyde/Clyde.LightRendering.cs, LightQuery) and mask
// cones (Robust.Shared/Light/LightLevelSystem.cs), Copyright (c) 2019 Space Station 14 Contributors;
// both MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Numerics;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Rooms;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Lighting;

/// <summary>
/// One beam of a light mask, as a <c>lightMask</c> prototype's <c>lightCones</c> give it: the
/// direction it points (0 is south, counter-clockwise) and the half-widths, in radians, within
/// which it is full and beyond which it is dark.
/// </summary>
public readonly record struct LightCone(double Direction, double InnerWidth, double OuterWidth);

/// <summary>
/// A light that shines at round start: where it is in room tiles (the entity's position plus its
/// offset turned with it), its radius, energy, falloff and curve, its colour as written (sRGB),
/// whether it casts shadows, its mask's cones (none for a round light) and their turn, and how soft
/// its shadows' edges are.
/// </summary>
public sealed record LightSource(Vector2 Position, float Radius, float Energy, float Falloff, float CurveFactor, Rgba Color, bool CastShadows,
    IReadOnlyList<LightCone> Cones, double MaskRotation, float Softness = 1);

/// <summary>
/// The lights of a room that shine at round start on a station with power everywhere and the
/// alert at green. What switches each is its components: a fixture (<c>PoweredLight</c>) shines
/// with the values of the bulb it spawns with while that is whole and the fixture is switched on
/// (<c>SharedPoweredLightSystem.UpdateLight</c>); consoles and machines with <c>LitOnPowered</c>
/// shine; a camera's <c>SlimPoweredLight</c> shines when enabled; emergency lights are off while
/// powered at green (<c>EmergencyLightSystem</c>); firelock warning lights, holopads, hand-held
/// lights and generators' circulators are off until played with; any other light shines as its
/// <c>PointLight.enabled</c> says.
/// </summary>
public static class LightSources
{
    /// <summary>
    /// The most shadow-casting lights the engine draws in a view (<c>light.max_shadowcasting_lights</c>,
    /// <c>Robust.Shared/CVars.cs</c>); beyond them it drops those farthest from the view's centre.
    /// </summary>
    public const int MaxShadowCasters = 128;

    /// <summary>
    /// The lights that shine in the room. A shadow-casting light inside an occluder is left out, as
    /// the engine leaves it out ("cannot work consistently", <c>Clyde.LightRendering.cs</c>, LightQuery);
    /// past <see cref="MaxShadowCasters"/> shadow-casting lights, those farthest from
    /// <paramref name="centre"/> are left out too.
    /// </summary>
    public static IReadOnlyList<LightSource> Collect(Room room, PrototypeIndex prototypes, Occluders occluders, Vector2 centre)
    {
        var lights = new List<LightSource>();
        foreach (var placed in room.Entities)
        {
            if (prototypes.Resolve("entity", placed.Proto) is not { } entity
                || PrototypeYaml.Component(entity, "PointLight") is not { } light
                || Shine(entity, light, prototypes) is not { } shine)
                continue;

            // RobustToolbox/Robust.Shared/GameObjects/Components/Light/SharedPointLightComponent.cs
            // for the defaults: falloff 6.8, curveFactor 0, castShadows true, autoRot false, softness 1.
            var (offsetX, offsetY) = PrototypeYaml.Vector(PrototypeYaml.Scalar(light, "offset")) ?? (0, 0);
            var (sin, cos) = Math.SinCos(placed.Rotation);
            // LightQuery: the entity's position plus its rotation applied to the offset.
            var position = new Vector2(placed.X, placed.Y) + new Vector2((float)(offsetX * cos - offsetY * sin), (float)(offsetX * sin + offsetY * cos));
            var source = new LightSource(
                position,
                shine.Radius,
                shine.Energy,
                Float(light, "falloff", 6.8f),
                Float(light, "curveFactor", 0f),
                shine.Color,
                PrototypeYaml.Bool(light, "castShadows", true),
                Cones(light, prototypes),
                // SharedPointLightSystem.GetMaskWorldRotation: the light's own rotation (0 until
                // something turns it) plus the entity's when autoRot.
                PrototypeYaml.Bool(light, "autoRot", false) ? placed.Rotation : 0,
                shine.Softness);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || (source.CastShadows && occluders.Contains(position)))
                continue;
            lights.Add(source);
        }

        var casters = lights.Where(l => l.CastShadows).ToList();
        if (casters.Count <= MaxShadowCasters)
            return lights;
        var dropped = casters.OrderBy(l => Vector2.DistanceSquared(l.Position, centre)).Skip(MaxShadowCasters).ToHashSet(ReferenceEqualityComparer.Instance);
        return lights.Where(l => !dropped.Contains(l)).ToList();
    }

    // The colour, radius, energy and softness a light shines with at round start, or null when it is off.
    private static (Rgba Color, float Radius, float Energy, float Softness)? Shine(YamlMappingNode entity, YamlMappingNode light, PrototypeIndex prototypes)
    {
        bool Has(string type) => PrototypeYaml.Component(entity, type) != null;
        var own = (Rgba.TryParse(PrototypeYaml.Scalar(light, "color"), out var color) ? color : Rgba.White,
            Float(light, "radius", 5f), Float(light, "energy", 1f), Float(light, "softness", 1f));

        if (Has("EmergencyLight"))
            return null;
        if (Has("PoweredLight"))
        {
            // The bulb's colour, radius, energy and softness, while it is whole and the fixture is on
            // (Content.Shared/Light/Components/LightBulbComponent.cs for the defaults).
            if (RoundStartLook.Bulb(entity, prototypes) is not { } bulb
                || (PrototypeYaml.Scalar(bulb, "startingState") ?? "Normal") != "Normal"
                || !PrototypeYaml.Bool(PrototypeYaml.Component(entity, "PoweredLight"), "on", true))
                return null;
            return (Rgba.TryParse(PrototypeYaml.Scalar(bulb, "color"), out var bulbColor) ? bulbColor : Rgba.White,
                Float(bulb, "lightRadius", 10f), Float(bulb, "lightEnergy", 0.8f), Float(bulb, "lightSoftness", 1f));
        }
        if (Has("LitOnPowered"))
            return own;
        if (Has("SlimPoweredLight"))
            return PrototypeYaml.Bool(PrototypeYaml.Component(entity, "SlimPoweredLight"), "enabled", true) ? own : null;
        if (Has("Firelock") || Has("Holopad") || Has("HandheldLight") || Has("ItemTogglePointLight") || Has("ExpendableLight")
            || Has("UnpoweredFlashlight") || Has("TegCirculator"))
            return null;
        return PrototypeYaml.Bool(light, "enabled", true) ? own : null;
    }

    /// <summary>
    /// The cones of the light's mask: its <c>lightMask</c> prototype's, or for an older engine's
    /// <c>mask</c> texture path, the cones of the <c>lightMask</c> prototype with that texture. A mask
    /// whose cones cannot be found draws the light round.
    /// </summary>
    private static IReadOnlyList<LightCone> Cones(YamlMappingNode light, PrototypeIndex prototypes)
    {
        YamlMappingNode? mask = null;
        if (PrototypeYaml.Scalar(light, "lightMask") is { Length: > 0 } id)
            mask = prototypes.Resolve("lightMask", id);
        else if (PrototypeYaml.Scalar(light, "mask") is { Length: > 0 } path)
            mask = prototypes.OfKind("lightMask").Select(p => p.Node).FirstOrDefault(n => PrototypeYaml.Scalar(n, "maskPath") == path);
        var cones = new List<LightCone>();
        foreach (var cone in PrototypeYaml.Sequence(mask, "lightCones")?.Children.OfType<YamlMappingNode>() ?? [])
        {
            if (PrototypeYaml.Angle(PrototypeYaml.Scalar(cone, "direction") ?? "0") is { } direction
                && PrototypeYaml.Angle(PrototypeYaml.Scalar(cone, "innerWidth")) is { } inner
                && PrototypeYaml.Angle(PrototypeYaml.Scalar(cone, "outerWidth")) is { } outer
                && double.IsFinite(direction + inner + outer))
                cones.Add(new LightCone(direction, inner, outer));
        }
        return cones;
    }

    /// <summary>
    /// How much of a masked light reaches an offset from it, by the engine's own CPU reading of the
    /// mask (<c>LightLevelSystem.GetColourFromLight</c>): full within a cone's inner width, fading to
    /// nothing at its outer width; the strongest cone counts. 1 for a light without cones.
    /// </summary>
    public static float ConeFactor(IReadOnlyList<LightCone> cones, double maskRotation, float dx, float dy)
    {
        if (cones.Count == 0)
            return 1;
        // Angle.FromWorldVec turns atan2 by a quarter so that 0 is south; at the light itself, 0.
        var angle = dx * dx + dy * dy < 1e-12 ? 0 : Math.Atan2(dy, dx) + Math.PI / 2 - maskRotation;
        var best = 0d;
        foreach (var cone in cones)
        {
            var delta = Math.Abs(Math.IEEERemainder(angle - cone.Direction, 2 * Math.PI));
            if (delta > cone.OuterWidth)
                continue;
            if (delta < cone.InnerWidth)
                return 1;
            best = Math.Max(best, (cone.OuterWidth - delta) / (cone.OuterWidth - cone.InnerWidth));
        }
        return (float)Math.Min(1, best);
    }

    private static float Float(YamlMappingNode? node, string key, float fallback) =>
        PrototypeYaml.Float(PrototypeYaml.Scalar(node, key)) is { } value ? value : fallback;
}
