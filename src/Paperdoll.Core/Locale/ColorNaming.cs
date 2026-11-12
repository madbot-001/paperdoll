// Ported from RobustToolbox (Robust.Shared/ColorNaming/ColorNaming.cs, written 2025),
// Copyright (c) 2019 Space Station 14 Contributors, MIT licence. The Oklab conversion, by way of
// the engine's Robust.Shared.Maths/Color.cs, is Björn Ottosson's
// (https://bottosson.github.io/posts/oklab/), Copyright (c) 2020 Björn Ottosson, public domain
// or MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Text.RegularExpressions;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Locale;

/// <summary>
/// A colour in words, as the game's colour sliders name it ("very light yellow"): a hue, with
/// words for chroma and lightness, from the colour's Oklch values and the <c>color-</c> messages.
/// </summary>
public static partial class ColorNaming
{
    private static readonly (float Hue, string Loc)[] HueNames =
    [
        (float.DegreesToRadians(0f), "color-pink"),
        (float.DegreesToRadians(15f), "color-red"),
        (float.DegreesToRadians(45f), "color-orange"),
        (float.DegreesToRadians(90f), "color-yellow"),
        (float.DegreesToRadians(135f), "color-green"),
        (float.DegreesToRadians(180f), "color-cyan"),
        (float.DegreesToRadians(240f), "color-blue"),
        (float.DegreesToRadians(285f), "color-purple"),
        (float.DegreesToRadians(330f), "color-pink"),
    ];
    // One past 360, as the upper end is inclusive when testing for out of bounds.
    private static readonly (float Hue, string Loc) HueFallback = (float.DegreesToRadians(361f), "color-pink");

    private const float BrownLightnessThreshold = 0.675f;
    private const float VeryDarkLightnessThreshold = 0.25f;
    private const float DarkLightnessThreshold = 0.5f;
    private const float NeutralLightnessThreshold = 0.7f;
    private const float LightLightnessThreshold = 0.85f;

    private const float LowChromaThreshold = 0.07f;
    private const float HighChromaThreshold = 0.16f;
    private const float LightLowChromaThreshold = 0.6f;

    private const float WhiteLightnessThreshold = 0.99f;
    private const float BlackLightnessThreshold = 0.01f;
    private const float GrayChromaThreshold = 0.01f;

    /// <summary>
    /// The engine's English colour names, shipped with Paperdoll: the engine's strings are not
    /// part of a fork's content, so no fork has them.
    /// </summary>
    public static FluentStrings Bundled => BundledStrings.Value;

    private static readonly Lazy<FluentStrings> BundledStrings = new(() =>
    {
        var strings = new FluentStrings();
        using var stream = typeof(ColorNaming).Assembly.GetManifestResourceStream("Paperdoll.Core.Locale.color-naming.ftl");
        // Missing only in a broken build; then colours go unnamed rather than failing.
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            strings.Add(reader.ReadToEnd());
        }
        return strings;
    });

    /// <summary>
    /// The colour's name in the fork's words, then <paramref name="fallback"/>'s (such as
    /// <see cref="Bundled"/>) for messages the fork lacks; null when neither has a message the
    /// name needs, so that no raw id is ever shown.
    /// </summary>
    public static string? Describe(Rgba srgb, FluentStrings strings, FluentStrings? fallback = null)
    {
        var words = new Words(strings, fallback);
        var (lightness, chroma, hue) = Oklch(srgb);

        if (lightness >= WhiteLightnessThreshold)
            return words.Result(words.Get("color-white"));
        if (lightness <= BlackLightnessThreshold)
            return words.Result(words.Get("color-black"));

        var (hueName, adjustedLightness) = DescribeHue(lightness, hue, words);
        lightness = adjustedLightness;
        var chromaName = DescribeChroma(lightness, chroma, words);
        var lightnessName = DescribeLightness(lightness, words);

        if (chroma <= GrayChromaThreshold)
        {
            hueName = words.Get("color-gray");
            chromaName = null;
        }

        return words.Result((chromaName, lightnessName) switch
        {
            ({ } c, { } l) => words.Get("color-hue-chroma-lightness", ("hue", hueName), ("chroma", c), ("lightness", l)),
            ({ } c, null) => words.Get("color-hue-chroma", ("hue", hueName), ("chroma", c)),
            (null, { } l) => words.Get("color-hue-lightness", ("hue", hueName), ("lightness", l)),
            (null, null) => hueName,
        });
    }

    private static (string Name, float AdjustedLightness) DescribeHue(float lightness, float hue, Words words)
    {
        for (var i = 0; i < HueNames.Length; i++)
        {
            var prev = HueNames[i];
            var next = i + 1 < HueNames.Length ? HueNames[i + 1] : HueFallback;

            if (prev.Hue > hue || hue >= next.Hue)
                continue;

            var loc = prev.Loc;
            var adjustedLightness = lightness;

            if (prev.Loc == "color-orange" && lightness <= BrownLightnessThreshold)
                loc = "color-brown";
            else if (prev.Loc == "color-orange")
                adjustedLightness = lightness - BrownLightnessThreshold + DarkLightnessThreshold;

            if (hue >= (prev.Hue + next.Hue) / 2f && prev.Loc != next.Loc)
            {
                if (words.Find($"{loc}-{next.Loc}") is { } pair)
                    return (pair, adjustedLightness);
                return (words.Get("color-mixed-hue", ("a", words.Get(loc)), ("b", words.Get(next.Loc))), adjustedLightness);
            }

            return (words.Get(loc), adjustedLightness);
        }

        // The engine names this "color-unknown"; a hue is always within 0 to 2 pi here.
        return (words.Get("color-unknown"), lightness);
    }

    private static string? DescribeChroma(float lightness, float chroma, Words words)
    {
        if (chroma <= LowChromaThreshold)
            return words.Get(lightness >= LightLowChromaThreshold ? "color-pale" : "color-gray-adjective");
        if (chroma >= HighChromaThreshold)
            return words.Get("color-strong");
        return null;
    }

    private static string? DescribeLightness(float lightness, Words words) => lightness switch
    {
        < VeryDarkLightnessThreshold => words.Get("color-very-dark"),
        < DarkLightnessThreshold => words.Get("color-dark"),
        < NeutralLightnessThreshold => null,
        < LightLightnessThreshold => words.Get("color-light"),
        _ => words.Get("color-very-light"),
    };

    // sRGB to linear, to Oklab, to Oklch: lightness, chroma and hue in radians (0 to 2 pi).
    private static (float L, float C, float H) Oklch(Rgba srgb)
    {
        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / (1.0f + 0.055f), 2.4f);
        var (r, g, b) = (Linear(srgb.R), Linear(srgb.G), Linear(srgb.B));

        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

        var lightness = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
        var labA = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
        var labB = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;

        var hue = MathF.Atan2(labB, labA);
        if (hue < 0)
            hue += 2 * MathF.PI;
        return (lightness, MathF.Sqrt(labA * labA + labB * labB), hue);
    }

    // Reads messages, the fork's first, and fills in their variables; remembers whether any was
    // missing, which makes the whole name unknown.
    private sealed class Words(FluentStrings strings, FluentStrings? fallback)
    {
        private bool _missing;

        /// <summary>The message filled in, or null if neither table has one this can fill.</summary>
        public string? Find(string id, params (string Name, string Value)[] args) =>
            Fill(strings[id], args) ?? Fill(fallback?[id], args);

        public string Get(string id, params (string Name, string Value)[] args)
        {
            if (Find(id, args) is { } text)
                return text;
            _missing = true;
            return "";
        }

        public string? Result(string name) => _missing ? null : name;

        private static string? Fill(string? text, (string Name, string Value)[] args)
        {
            if (text == null)
                return null;
            var unknown = false;
            text = Variable().Replace(text, match =>
            {
                foreach (var (name, value) in args)
                {
                    if (name == match.Groups[1].Value)
                        return value;
                }
                unknown = true;
                return "";
            });
            // Anything else in braces is Fluent this reader does not follow.
            return unknown || text.Contains('{') ? null : text;
        }
    }

    [GeneratedRegex(@"\{\s*\$([A-Za-z][A-Za-z0-9_-]*)\s*\}")]
    private static partial Regex Variable();
}
