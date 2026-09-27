using System.Globalization;

namespace Paperdoll.Core.Rendering;

/// <summary>RGBA colour, 0 to 1 per channel.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static readonly Rgba White = new(1, 1, 1);

    /// <summary>Reads a colour as <see cref="TryParse"/> does, or throws.</summary>
    public static Rgba Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"Not a colour: {text}");

    /// <summary>
    /// Reads a colour as the engine does (<c>Color.TryFromName</c>, then <c>TryFromHex</c>): a
    /// colour name, or <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>. Six or
    /// eight hex digits without the <c>#</c> are taken too. Anything else gives white.
    /// </summary>
    public static bool TryParse(string? text, out Rgba color)
    {
        color = White;
        if (string.IsNullOrEmpty(text))
            return false;
        if (text[0] != '#' && Named(text) is { } named)
        {
            color = named;
            return true;
        }

        var hex = text.StartsWith('#') ? text.AsSpan(1) : text.Length is 6 or 8 ? text.AsSpan() : [];
        Span<byte> parts = stackalloc byte[4];
        parts[3] = 255;
        switch (hex.Length)
        {
            // One digit a channel, doubled: #F80 is #FF8800.
            case 3 or 4:
                for (var i = 0; i < hex.Length; i++)
                {
                    if (!byte.TryParse(hex.Slice(i, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var digit))
                        return false;
                    parts[i] = (byte)(digit * 17);
                }
                break;
            case 6 or 8:
                for (var i = 0; i < hex.Length / 2; i++)
                {
                    if (!byte.TryParse(hex.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parts[i]))
                        return false;
                }
                break;
            default:
                return false;
        }
        color = new Rgba(parts[0] / 255f, parts[1] / 255f, parts[2] / 255f, parts[3] / 255f);
        return true;
    }

    // The engine's colour names: the web's (which .NET knows) and four of its own (from
    // RobustToolbox's Color.cs, MIT; see THIRD-PARTY-NOTICES.md).
    private static Rgba? Named(string name)
    {
        var known = System.Drawing.Color.FromName(name);
        // .NET also knows RebeccaPurple, which the engine does not.
        if (known.IsKnownColor && !known.IsSystemColor && known.ToKnownColor() != System.Drawing.KnownColor.RebeccaPurple)
            return new Rgba(known.R / 255f, known.G / 255f, known.B / 255f, known.A / 255f);
        return name.ToLowerInvariant() switch
        {
            "betterviolet" => new Rgba(126 / 255f, 3 / 255f, 168 / 255f),
            "ruber" => new Rgba(204 / 255f, 71 / 255f, 120 / 255f),
            "seablue" => new Rgba(0, 66 / 255f, 153 / 255f),
            "vividgamboge" => new Rgba(1, 153 / 255f, 0),
            _ => null,
        };
    }

    public string ToHex() =>
        $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}{Byte(A):X2}";

    public Rgba WithAlpha(float alpha) => this with { A = alpha };

    private static byte Byte(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    /// <summary>Hue (0 to 1), saturation, value and alpha.</summary>
    public (float H, float S, float V, float A) ToHsv()
    {
        var max = MathF.Max(R, MathF.Max(G, B));
        var min = MathF.Min(R, MathF.Min(G, B));
        var chroma = max - min;

        float sector = 0;
        if (chroma > 0)
        {
            if (max == R)
                sector = ((G - B) / chroma % 6 + 6) % 6;
            else if (max == G)
                sector = (B - R) / chroma + 2;
            else
                sector = (R - G) / chroma + 4;
        }
        return (sector / 6f, max == 0 ? 0 : chroma / max, max, A);
    }

    /// <summary>Hue (0 to 1), saturation, lightness and alpha.</summary>
    public (float H, float S, float L, float A) ToHsl()
    {
        var max = MathF.Max(R, MathF.Max(G, B));
        var min = MathF.Min(R, MathF.Min(G, B));
        var light = (max + min) / 2f;
        var chroma = max - min;
        var sat = chroma == 0 ? 0 : chroma / (1 - MathF.Abs(2 * light - 1));
        return (ToHsv().H, sat, light, A);
    }

    public static Rgba FromHsl(float h, float s, float l, float a = 1f)
    {
        var chroma = (1 - MathF.Abs(2 * l - 1)) * s;
        var value = l + chroma / 2f;
        var hsvSat = value == 0 ? 0 : chroma / value;
        return FromHsv(h, hsvSat, value, a);
    }

    public static Rgba FromHsv(float h, float s, float v, float a = 1f)
    {
        var sector = (h - MathF.Floor(h)) * 6f;
        var chroma = v * s;
        var x = chroma * (1 - MathF.Abs(sector % 2 - 1));
        var m = v - chroma;
        var (r, g, b) = (int)sector switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        return new Rgba(r + m, g + m, b + m, a);
    }
}
