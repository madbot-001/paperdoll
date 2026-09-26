// Ported from Space Station 14's HumanoidCharacterProfile.Random and HumanoidCharacterAppearance
// (Random and HumanoidCharacterAppearance.Markings.cs), Copyright (c) 2017-2026 Space Wizards
// Federation, and RobustToolbox's ColorExtensions, Copyright (c) 2019 Space Station 14
// Contributors; both MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Characters;

/// <summary>Which parts of a character the randomiser changes; the rest are kept (the lobby's locks).</summary>
[Flags]
public enum RandomParts
{
    None = 0,
    Name = 1,
    Species = 2,
    Age = 4,
    Sex = 8,
    Pronouns = 16,
    Eyes = 32,
    Skin = 64,
    Markings = 128,
    All = Name | Species | Age | Sex | Pronouns | Eyes | Skin | Markings,
}

/// <summary>
/// The lobby's randomise button: a random sex, age and name for the
/// species, a palette of three colours a set turn apart on the colour wheel (skin, hair and eyes,
/// fitted to the species' skin rule), and markings rolled layer by layer by weight.
/// </summary>
public sealed class Randomizer(CharacterCatalog catalog, Random random)
{
    /// <summary>The colours a character's skin, hair and eyes are drawn from.</summary>
    public sealed record Palette(Rgba Skin, Rgba Hair, Rgba Eyes);

    // HairStyles.RealisticHairColors: yellow, black, sandy brown, brown, wheat, grey.
    private static readonly Rgba[] RealisticHair = [Rgb(255, 255, 0), Rgb(0, 0, 0), Rgb(244, 164, 96), Rgb(165, 42, 42), Rgb(245, 222, 179), Rgb(128, 128, 128)];

    // HumanoidCharacterAppearance.RealisticEyeColors: brown, grey, azure, steel blue, black.
    private static readonly Rgba[] RealisticEyes = [Rgb(165, 42, 42), Rgb(128, 128, 128), Rgb(240, 255, 255), Rgb(70, 130, 180), Rgb(0, 0, 0)];

    public string Sex(SpeciesInfo species) => species.Sexes[random.Next(species.Sexes.Count)];

    public static string PronounsFor(string sex) => sex switch { "Male" => "Male", "Female" => "Female", _ => "Epicene" };

    /// <summary>From the youngest age up to, but not including, the species' old age.</summary>
    public int Age(SpeciesInfo species) => species.OldAge > species.MinAge ? random.Next(species.MinAge, species.OldAge) : species.MinAge;

    /// <summary>
    /// A random base colour and two more turned around the colour wheel (split complementary,
    /// triadic, analogous or complementary), then fitted to the species' rules.
    /// </summary>
    public Palette RandomPalette(SpeciesInfo species)
    {
        var baseColor = new Rgba(random.NextSingle(), random.NextSingle(), random.NextSingle());
        var delta = random.Next(4) switch
        {
            0 => 150f / 360f,
            1 => 120f / 360f,
            2 => 30f / 360f,
            _ => 180f / 360f,
        };
        // The engine means to vary saturation and lightness a little here, but its integer maths
        // makes the change zero, so only the hue turns.
        var (h, s, l, a) = baseColor.ToHsl();
        var plus = h + delta;
        plus -= MathF.Floor(plus);
        var minus = h - delta;
        if (minus < 0f)
            minus += 1f;
        return Fit(new Palette(baseColor, Rgba.FromHsl(plus, s, l, a), Rgba.FromHsl(minus, s, l, a)), species);
    }

    // ClampPaletteToStrategy: skin to the rule; realistic hair and eyes where the rule asks; hair and
    // eyes pulled to skin colours where it asks for that.
    private Palette Fit(Palette palette, SpeciesInfo species)
    {
        var rule = catalog.SkinRuleFor(species);
        var node = species.SkinColoration != null ? catalog.SkinColorations.GetValueOrDefault(species.SkinColoration) : null;
        var realistic = Flag(node, "realisticColors");
        var squash = Flag(node, "squashEyeHairColors");

        var hair = palette.Hair;
        if (realistic)
        {
            var pick = RealisticHair[random.Next(RealisticHair.Length)];
            hair = new Rgba(Nudge(pick.R), Nudge(pick.G), Nudge(pick.B), pick.A);
        }
        if (squash)
            hair = rule.Closest(hair);

        var eyes = palette.Eyes;
        if (realistic && !RealisticEyes.Contains(eyes))
            eyes = RealisticEyes[random.Next(RealisticEyes.Length)];
        if (squash)
            eyes = rule.EnsureValid(eyes);

        return new Palette(rule.EnsureValid(palette.Skin), hair, eyes);

        float Nudge(float channel) => Math.Clamp(channel + (random.NextSingle() * 0.5f - 0.25f), 0f, 1f);
    }

    /// <summary>
    /// Markings for every organ that takes them: each layer gets up to its limit, one roll of the
    /// layer's weight per place, each marking picked by its random weight; hair and facial hair get
    /// at most one, in the hair colour.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<MarkingEntry>>> Markings(SpeciesInfo species, string sex, Palette palette)
    {
        var result = new Dictionary<string, Dictionary<string, List<MarkingEntry>>>();
        foreach (var organ in species.Organs.Where(o => o.TakesMarkings))
        {
            if (organ.MarkingGroup == null || !catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group))
                continue;
            var layers = new Dictionary<string, List<MarkingEntry>>();
            foreach (var layer in organ.MarkingLayers)
            {
                var all = catalog.Markings.Values.Where(m => m.Layer == layer && Profiles.CharacterRules.CanBeApplied(group, sex, m)).ToList();
                if (all.Count == 0 || !group.Limits.TryGetValue(layer, out var limit) || limit.Limit <= 0)
                    continue;
                layers[layer] = layer is "Hair" or "FacialHair" ? Hair(limit, all, palette) : Layer(limit, all, palette);
            }
            result[organ.Category] = layers;
        }
        return result;
    }

    private List<MarkingEntry> Hair(LayerLimit limit, List<MarkingInfo> all, Palette palette)
    {
        if (random.NextDouble() >= limit.Weight || PickWeighted(all) is not { } hair)
            return [];
        return [new MarkingEntry(hair.Id, Enumerable.Repeat(palette.Hair, hair.Sprites.Count).ToList())];
    }

    private List<MarkingEntry> Layer(LayerLimit limit, List<MarkingInfo> all, Palette palette)
    {
        var pool = all.ToList();
        var picked = new List<MarkingEntry>();
        for (var i = 0; i < limit.Limit && pool.Count > 0; i++)
        {
            if (random.NextDouble() >= limit.Weight || PickWeighted(pool) is not { } marking)
                continue;
            pool.Remove(marking);
            var colors = MarkingColoring.RandomColors(marking, palette.Skin, palette.Eyes, picked,
                () => random.Next(2) == 0 ? palette.Hair : palette.Eyes);
            picked.Add(new MarkingEntry(marking.Id, colors));
        }
        return picked;
    }

    private MarkingInfo? PickWeighted(List<MarkingInfo> markings)
    {
        var total = markings.Sum(m => Math.Max(0f, m.RandomWeight));
        if (total <= 0f)
            return markings.Count > 0 ? markings[random.Next(markings.Count)] : null;
        var roll = random.NextSingle() * total;
        foreach (var marking in markings)
        {
            roll -= Math.Max(0f, marking.RandomWeight);
            if (roll <= 0f)
                return marking;
        }
        return markings[^1];
    }

    private static bool Flag(YamlDotNet.RepresentationModel.YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode(key), out var value)
        && value is YamlDotNet.RepresentationModel.YamlScalarNode { Value: "true" or "True" };

    private static Rgba Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f);
}
