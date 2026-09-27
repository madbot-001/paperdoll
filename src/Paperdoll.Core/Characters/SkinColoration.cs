// Ported from Space Station 14 (Content.Shared/Humanoid/SkinColorationPrototype.cs and
// HumanoidCharacterAppearance.DefaultWithSpecies), Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Characters;

/// <summary>
/// A species' rule for which skin colours are allowed, from its <c>skinColoration</c> prototype.
/// Either a single 0–100 slider (<see cref="IsUnary"/>) or any colour pulled into range.
/// </summary>
public abstract class SkinColoration
{
    /// <summary>One step of an 8-bit channel; colours this close to valid count as valid.</summary>
    public const float Epsilon = 0.003921568627451f;
    private const float EpsilonHue = 0.00277f;

    /// <summary>True when the editor shows a 0–100 slider rather than a colour picker.</summary>
    public virtual bool IsUnary => false;

    public abstract bool IsValid(Rgba color);

    /// <summary>The nearest allowed colour.</summary>
    public abstract Rgba Closest(Rgba color);

    public virtual Rgba FromUnary(float value) =>
        throw new InvalidOperationException("This skin rule has no slider.");

    public virtual float ToUnary(Rgba color) =>
        throw new InvalidOperationException("This skin rule has no slider.");

    /// <summary>
    /// The colour if it's allowed (or within one 8-bit step of it), else the nearest allowed one.
    /// </summary>
    public Rgba EnsureValid(Rgba color)
    {
        for (var i = 0; i < 8; i++)
        {
            var test = color with
            {
                R = (i & 1) != 0 ? MathF.Min(color.R + Epsilon, 1) : color.R,
                G = (i & 2) != 0 ? MathF.Min(color.G + Epsilon, 1) : color.G,
                B = (i & 4) != 0 ? MathF.Min(color.B + Epsilon, 1) : color.B,
            };
            if (IsValid(test))
                return color;
        }
        return Closest(color);
    }

    /// <summary>Default skin colour for a new character.</summary>
    public static Rgba DefaultFor(SpeciesInfo species, SkinColoration rule)
    {
        if (rule.IsUnary)
        {
            var tone = int.TryParse(Scalar(species.Node, "defaultHumanSkinTone"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 20;
            return rule.FromUnary(tone);
        }
        return rule.Closest(Rgba.TryParse(species.DefaultSkinTone, out var c) ? c : Rgba.White);
    }

    /// <summary>Reads a <c>skinColoration</c> prototype's strategy. Unknown strategies allow anything.</summary>
    public static SkinColoration Read(YamlMappingNode prototype)
    {
        if (!prototype.Children.TryGetValue(new YamlScalarNode("strategy"), out var node))
            return new AnyColor();
        var map = node as YamlMappingNode;
        return (node.Tag.IsEmpty ? "" : node.Tag.Value) switch
        {
            "!type:HumanTonedSkinColoration" => new HumanToned(),
            "!type:ClampedHsvColoration" => new ClampedHsv(Range(map, "hue"), Range(map, "saturation"), Range(map, "value")),
            "!type:ClampedHslColoration" => new ClampedHsl(Range(map, "hue"), Range(map, "saturation"), Range(map, "lightness")),
            "!type:HueNodeClampedHsvColoration" => new HueNodes(Nodes(map)),
            _ => new AnyColor(),
        };
    }

    internal static bool HueInRange(float hue, float min, float max) =>
        min > max // wraps past 1, as reds do
            ? hue >= min - EpsilonHue || hue <= max + EpsilonHue
            : hue >= min - EpsilonHue && hue <= max + EpsilonHue;

    internal static float ClampHue(float hue, float min, float max)
    {
        if (min <= max)
            return Math.Clamp(hue, min, max);
        if (hue >= min || hue <= max)
            return hue;
        return hue > (max + min) / 2f ? min : max;
    }

    private static (float Min, float Max)? Range(YamlMappingNode? map, string key)
    {
        if (map == null || !map.Children.TryGetValue(new YamlScalarNode(key), out var node) || node is not YamlSequenceNode { Children.Count: 2 } pair)
            return null;
        return (Float(pair.Children[0]), Float(pair.Children[1]));
    }

    private static List<HueNode> Nodes(YamlMappingNode? map)
    {
        if (map == null || !map.Children.TryGetValue(new YamlScalarNode("nodes"), out var node) || node is not YamlSequenceNode list)
            return [];
        return list.Children.OfType<YamlMappingNode>()
            .Select(n => new HueNode(
                float.Parse(Scalar(n, "hue") ?? "0", CultureInfo.InvariantCulture),
                Range(n, "saturation") ?? (0, 0),
                Range(n, "value") ?? (0, 0)))
            .ToList();
    }

    private static float Float(YamlNode node) => float.Parse(((YamlScalarNode)node).Value!, CultureInfo.InvariantCulture);

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private sealed class AnyColor : SkinColoration
    {
        public override bool IsValid(Rgba color) => true;
        public override Rgba Closest(Rgba color) => color;
    }

    /// <summary>A 0–100 slider: hue 45° down to 25° first, then more saturation and less value.</summary>
    private sealed class HumanToned : SkinColoration
    {
        public override bool IsUnary => true;

        public override bool IsValid(Rgba color)
        {
            var (h, s, v, _) = color.ToHsv();
            var hue = Math.Round(h * 360f);
            return hue is >= 25 and <= 45 && Math.Round(s * 100f) >= 20 && Math.Round(v * 100f) >= 20;
        }

        public override Rgba Closest(Rgba color) => IsValid(color) ? color : FromUnary(ToUnary(color));

        public override Rgba FromUnary(float value)
        {
            var offset = Math.Clamp(value, 0f, 100f) - 20f;
            float hue = 25, sat = 20, val = 100;
            if (offset <= 0)
                hue += Math.Abs(offset);
            else
            {
                sat += offset;
                val -= offset;
            }
            return Rgba.FromHsv(hue / 360f, sat / 100f, val / 100f);
        }

        public override float ToUnary(Rgba color)
        {
            var (h, s, v, _) = color.ToHsv();
            return h > 25f / 360f && v == 1f ? Math.Abs(45 - h * 360) : s * 100;
        }
    }

    private sealed class ClampedHsv((float Min, float Max)? hue, (float Min, float Max)? sat, (float Min, float Max)? val) : SkinColoration
    {
        public override bool IsValid(Rgba color)
        {
            var (h, s, v, _) = color.ToHsv();
            return (hue is not { } hr || HueInRange(h, hr.Min, hr.Max))
                && (sat is not { } sr || (s >= sr.Min - Epsilon && s <= sr.Max + Epsilon))
                && (val is not { } vr || (v >= vr.Min - Epsilon && v <= vr.Max + Epsilon));
        }

        public override Rgba Closest(Rgba color)
        {
            var (h, s, v, a) = color.ToHsv();
            var (nh, ns, nv) = (h, s, v);
            if (hue is { } hr) nh = ClampHue(h, hr.Min, hr.Max);
            if (sat is { } sr) ns = Math.Clamp(s, sr.Min, sr.Max);
            if (val is { } vr) nv = Math.Clamp(v, vr.Min, vr.Max);
            return (nh, ns, nv) == (h, s, v) ? color : Rgba.FromHsv(nh, ns, nv, a);
        }
    }

    private sealed class ClampedHsl((float Min, float Max)? hue, (float Min, float Max)? sat, (float Min, float Max)? light) : SkinColoration
    {
        public override bool IsValid(Rgba color)
        {
            var (h, s, l, _) = color.ToHsl();
            return (hue is not { } hr || HueInRange(h, hr.Min, hr.Max))
                && (sat is not { } sr || (s >= sr.Min - Epsilon && s <= sr.Max + Epsilon))
                && (light is not { } lr || (l >= lr.Min - Epsilon && l <= lr.Max + Epsilon));
        }

        public override Rgba Closest(Rgba color)
        {
            var (h, s, l, a) = color.ToHsl();
            var (nh, ns, nl) = (h, s, l);
            if (hue is { } hr) nh = ClampHue(h, hr.Min, hr.Max);
            if (sat is { } sr) ns = Math.Clamp(s, sr.Min, sr.Max);
            if (light is { } lr) nl = Math.Clamp(l, lr.Min, lr.Max);
            return (nh, ns, nl) == (h, s, l) ? color : Rgba.FromHsl(nh, ns, nl, a);
        }
    }

    private sealed record HueNode(float Hue, (float Min, float Max) Saturation, (float Min, float Max) Value);

    /// <summary>Hue points with saturation and value ranges, blended between neighbouring points.</summary>
    private sealed class HueNodes(List<HueNode> nodes) : SkinColoration
    {
        private const float Tolerance = 0.019f;

        public override bool IsValid(Rgba color)
        {
            if (nodes.Count == 0)
                return true;
            var (h, s, v, _) = color.ToHsv();
            if (RangeFor(ClampHue(h, nodes[0].Hue, nodes[^1].Hue)) is not { } range)
                return false;
            return s >= range.Saturation.Min - Tolerance && s <= range.Saturation.Max + Tolerance
                && v >= range.Value.Min - Tolerance && v <= range.Value.Max + Tolerance;
        }

        public override Rgba Closest(Rgba color)
        {
            if (nodes.Count == 0)
                return color;
            var (h, s, v, a) = color.ToHsv();
            var nh = ClampHue(h, nodes[0].Hue, nodes[^1].Hue);
            if (RangeFor(nh) is not { } range)
                return color;
            var ns = Math.Clamp(s, range.Saturation.Min, range.Saturation.Max);
            var nv = Math.Clamp(v, range.Value.Min, range.Value.Max);
            return (nh, ns, nv) == (h, s, v) ? color : Rgba.FromHsv(nh, ns, nv, a);
        }

        private HueNode? RangeFor(float hue)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var current = nodes[i];
                var next = i + 1 < nodes.Count ? nodes[i + 1] : nodes[0];
                if (nodes.Count > 1 && !HueInRange(hue, current.Hue, next.Hue))
                    continue;

                var weight = MathF.Abs(current.Hue - next.Hue) < 1e-7f ? 0f : (hue - current.Hue) / (next.Hue - current.Hue);
                float Lerp(float a, float b) => a + (b - a) * weight;
                return new HueNode(hue,
                    (Lerp(current.Saturation.Min, next.Saturation.Min), Lerp(current.Saturation.Max, next.Saturation.Max)),
                    (Lerp(current.Value.Min, next.Value.Min), Lerp(current.Value.Max, next.Value.Max)));
            }
            return null;
        }
    }
}
