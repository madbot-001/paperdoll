using System.Globalization;

namespace Paperdoll.Core.Rendering;

/// <summary>RGBA colour, 0 to 1 per channel.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static readonly Rgba White = new(1, 1, 1);

    /// <summary>Reads <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public static Rgba Parse(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length is not (6 or 8))
            throw new FormatException($"Not a colour: {hex}");
        byte Part(int i) => byte.Parse(text.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(Part(0) / 255f, Part(1) / 255f, Part(2) / 255f, text.Length == 8 ? Part(3) / 255f : 1f);
    }

    public static bool TryParse(string? hex, out Rgba color)
    {
        color = White;
        if (hex == null)
            return false;
        try
        {
            color = Parse(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
