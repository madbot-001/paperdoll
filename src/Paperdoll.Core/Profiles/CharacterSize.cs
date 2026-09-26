using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// How big a character is drawn, by the fork's <see cref="SizeRule"/>. Delta-V and Goob code is
/// AGPLv3, so none of it is copied; the ranges, defaults and checks match theirs.
/// </summary>
public static class CharacterSize
{
    /// <summary>The profile key Delta-V keeps the character's height in (a scale, not centimetres).</summary>
    public const string DeltaVHeightKey = "cosmaticDriftCharacterHeight";

    /// <summary>The profile keys Goob keeps height and width in (scales).</summary>
    public const string HeightKey = "height";
    public const string WidthKey = "width";

    public static bool HasHeight(ForkInfo fork) => fork.SizeRule != SizeRule.None;

    public static bool HasWidth(ForkInfo fork) => fork.SizeRule == SizeRule.HeightAndWidth;

    public static float? ReadHeight(CharacterFile file, ForkInfo fork) =>
        Read(file, fork.SizeRule == SizeRule.HeightAndWidth ? HeightKey : DeltaVHeightKey);

    public static float? ReadWidth(CharacterFile file) => Read(file, WidthKey);

    /// <summary>
    /// What the game takes when the file has no size: 1 in Delta-V; 0 in Goob, which its check
    /// then raises to the species' smallest.
    /// </summary>
    public static float Missing(ForkInfo fork) => fork.SizeRule == SizeRule.HeightAndWidth ? 0f : 1f;

    /// <summary>Delta-V keeps two decimals; Goob writes the number as the game does, in full.</summary>
    public static void WriteHeight(CharacterFile file, ForkInfo fork, float height)
    {
        if (fork.SizeRule == SizeRule.HeightAndWidth)
            file.SetValue(HeightKey, height.ToString(CultureInfo.InvariantCulture));
        else
            file.SetValue(DeltaVHeightKey, height.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public static void WriteWidth(CharacterFile file, float width) =>
        file.SetValue(WidthKey, width.ToString(CultureInfo.InvariantCulture));

    /// <summary>Clamped to the species' range; Delta-V also rounds to two decimals.</summary>
    public static float CheckHeight(float height, SpeciesInfo species, ForkInfo fork) =>
        Math.Clamp(fork.SizeRule == SizeRule.HeightAndWidth ? height : MathF.Round(height, 2), species.MinHeight, species.MaxHeight);

    public static float CheckWidth(float width, SpeciesInfo species) =>
        Math.Clamp(width, species.MinWidth, species.MaxWidth);

    /// <summary>The height as the game reads the file.</summary>
    public static float Height(CharacterFile file, ForkInfo fork, SpeciesInfo species) =>
        CheckHeight(ReadHeight(file, fork) ?? Missing(fork), species, fork);

    /// <summary>The width as the game reads the file (Goob).</summary>
    public static float Width(CharacterFile file, ForkInfo fork, SpeciesInfo species) =>
        CheckWidth(ReadWidth(file) ?? Missing(fork), species);

    /// <summary>
    /// Goob's lobby sliders hold height and width within the species' size ratio of each other.
    /// The value the player moved stays where it was put; the other is brought just inside the band
    /// that value allows. The game's own check does not do this, so files may hold other pairs.
    /// </summary>
    /// <param name="heightMoved">Which one the player moved; the other is the one pulled.</param>
    public static (float Height, float Width) KeepRatio(float height, float width, SpeciesInfo species, bool heightMoved)
    {
        height = Math.Clamp(height, species.MinHeight, species.MaxHeight);
        width = CheckWidth(width, species);
        // The ratio limits height over width from either side, so one written below 1 means the same
        // as its inverse; a zero or broken one limits nothing.
        var spread = species.SizeRatio >= 1 ? species.SizeRatio : 1 / species.SizeRatio;
        if (spread >= 1 && float.IsFinite(spread))
        {
            if (heightMoved)
                width = Math.Clamp(width, height / spread, height * spread);
            else
                height = Math.Clamp(height, width / spread, width * spread);
        }
        return (Math.Clamp(height, species.MinHeight, species.MaxHeight), CheckWidth(width, species));
    }

    /// <summary>How much the sprite is scaled on screen, across and up.</summary>
    public static (float X, float Y) SpriteScale(ForkInfo fork, SpeciesInfo species, CharacterFile file) => fork.SizeRule switch
    {
        SizeRule.SpeciesScaleTimesHeight => (species.BaseScale.X * Height(file, fork, species), species.BaseScale.Y * Height(file, fork, species)),
        SizeRule.HeightAndWidth => (Width(file, fork, species), Height(file, fork, species)),
        _ => (1, 1),
    };

    /// <summary>The scale a species is drawn at in lists, at its default size.</summary>
    public static (float X, float Y) DefaultScale(ForkInfo fork, SpeciesInfo species) => fork.SizeRule switch
    {
        SizeRule.SpeciesScaleTimesHeight => species.BaseScale,
        SizeRule.HeightAndWidth => (species.DefaultWidth, species.DefaultHeight),
        _ => (1, 1),
    };

    /// <summary>Height and shoulder width in centimetres, as Goob's lobby shows them.</summary>
    public static (int Height, int Width) Centimetres(SpeciesInfo species, float height, float width) =>
        ((int)MathF.Round(species.AverageHeight * height), (int)MathF.Round(species.AverageWidth * width));

    /// <summary>
    /// Weight in kilograms as Goob's lobby shows it: the species' mass times the mean of height and
    /// width, or a flat 71 when the species has no simple shape.
    /// </summary>
    public static int Kilograms(SpeciesInfo species, float height, float width) =>
        species.Mass is { } mass ? (int)(mass * (height + width) / 2) : 71;

    private static float? Read(CharacterFile file, string key) =>
        float.TryParse(file.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
