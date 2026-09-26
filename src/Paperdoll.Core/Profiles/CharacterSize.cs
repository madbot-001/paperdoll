using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Profiles;

/// <summary>How big a character is drawn, by the fork's <see cref="SizeRule"/>.</summary>
public static class CharacterSize
{
    /// <summary>The profile key Delta-V keeps the character's height in (a scale, not centimetres).</summary>
    public const string HeightKey = "cosmaticDriftCharacterHeight";

    public static bool HasHeight(ForkInfo fork) => fork.SizeRule == SizeRule.SpeciesScaleTimesHeight;

    public static float? ReadHeight(CharacterFile file) =>
        float.TryParse(file.GetValue(HeightKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ? height : null;

    public static void WriteHeight(CharacterFile file, float height) =>
        file.SetValue(HeightKey, height.ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>Rounded to two decimals and clamped to the species' range.</summary>
    public static float CheckHeight(float height, SpeciesInfo species) =>
        Math.Clamp(MathF.Round(height, 2), species.MinHeight, species.MaxHeight);

    /// <summary>How much the sprite is scaled on screen, across and up.</summary>
    public static (float X, float Y) SpriteScale(ForkInfo fork, SpeciesInfo species, CharacterFile file)
    {
        if (!HasHeight(fork))
            return (1, 1);
        var height = CheckHeight(ReadHeight(file) ?? 1f, species);
        return (species.BaseScale.X * height, species.BaseScale.Y * height);
    }
}
