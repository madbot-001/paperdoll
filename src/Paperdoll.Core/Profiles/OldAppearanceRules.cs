// Ported from Space Station 14 at 27bb73d, the last upstream version on the old appearance model
// (Content.Shared/Humanoid/HumanoidCharacterAppearance.EnsureValid and
// Content.Shared/Humanoid/Markings/MarkingsSet.cs), Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Characters;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// The game's appearance checks for species on the old appearance model, where hair and facial
/// hair are fields of their own and every other marking sits in one list, limited by points per
/// marking category rather than per layer.
/// </summary>
public static class OldAppearanceRules
{
    /// <summary>
    /// The appearance after the game's rules:
    /// <list type="bullet">
    /// <item>Hair and facial hair must be a known style of their kind, for any species; else none.</item>
    /// <item>Unknown markings go, and each category keeps as many as its points allow, first ones first.</item>
    /// <item>A marking whose colours do not match its sprites is reset to white.</item>
    /// <item>Skin follows the species' colour rule.</item>
    /// <item>Markings for other species, or for another sex, go; markings on layers that match the
    /// skin (slimes) take the skin colour.</item>
    /// </list>
    /// </summary>
    /// <param name="fileOrder">
    /// The file's marking list, when the look was read from an old-model file: the game counts
    /// points in that order, which the look's grouping by layer does not keep.
    /// </param>
    public static CharacterLook EnsureValidLook(CharacterLook look, SpeciesInfo species, CharacterCatalog catalog, List<RuleFix>? fixes = null,
        IReadOnlyList<MarkingEntry>? fileOrder = null)
    {
        var old = species.Old ?? throw new ArgumentException($"{species.Id} is not on the old appearance model.", nameof(species));
        var skin = catalog.SkinRuleFor(species).EnsureValid(look.SkinColor);
        if (skin != look.SkinColor)
            fixes?.Add(new("skinColor", $"{species.Id} skin must follow its colour rule; moved to the nearest allowed colour."));
        var eyes = Clamp(look.EyeColor);

        var result = new Dictionary<string, Dictionary<string, List<MarkingEntry>>>();
        foreach (var category in new[] { "Hair", "FacialHair" })
        {
            var hair = look.Markings.GetValueOrDefault(category)?.Values.SelectMany(l => l).FirstOrDefault();
            if (hair == null)
                continue;
            if (!catalog.Markings.TryGetValue(hair.Id, out var style) || style.Category != category)
            {
                fixes?.Add(new(category == "Hair" ? "hair" : "facialHair", $"{hair.Id} is not a {Words(category).ToLowerInvariant()} style here; set to none."));
                continue;
            }
            Add(style, hair with { Colors = [hair.Colors.Count > 0 ? Clamp(hair.Colors[0]) : Rgba.White] });
        }

        // Each marking goes in its own category, in the order they came; a category with points
        // takes that many and no more.
        var points = old.Points.ToDictionary(kv => kv.Key, kv => kv.Value.Points);
        var unknown = new List<string>();
        var overPoints = new SortedSet<string>(StringComparer.Ordinal);
        var reset = new List<string>();
        var entries = fileOrder ?? look.Markings.Where(kv => kv.Key is not ("Hair" or "FacialHair")).SelectMany(kv => kv.Value.Values).SelectMany(l => l).ToList();
        foreach (var entry in entries)
        {
            if (!catalog.Markings.TryGetValue(entry.Id, out var marking) || marking.Category == null)
            {
                unknown.Add(entry.Id);
                continue;
            }
            // Hair kept in the list is read as the character's hair.
            if (marking.Category is "Hair" or "FacialHair")
                continue;
            if (points.TryGetValue(marking.Category, out var left))
            {
                if (left <= 0)
                {
                    overPoints.Add(marking.Category);
                    continue;
                }
                points[marking.Category] = left - 1;
            }
            var colors = entry.Colors;
            if (colors.Count != marking.Sprites.Count)
            {
                colors = Enumerable.Repeat(Rgba.White, marking.Sprites.Count).ToList();
                reset.Add(entry.Id);
            }
            Add(marking, entry with { Colors = colors });
        }
        if (unknown.Count > 0)
            fixes?.Add(new("markings", $"Markings this fork does not have were removed: {string.Join(", ", unknown.Distinct())}."));
        foreach (var category in overPoints)
            fixes?.Add(new("markings", $"{Words(category)} takes {old.Points[category].Points} marking{(old.Points[category].Points == 1 ? "" : "s")}; the extra ones were removed."));
        if (reset.Count > 0)
            fixes?.Add(new("markings", $"Colours that did not match the marking's sprites were reset to white: {string.Join(", ", reset.Distinct())}."));

        // EnsureSpecies and EnsureSexes: hair is not in the game's marking list, so these leave it be.
        var wrongSpecies = new List<string>();
        var wrongSex = new List<string>();
        foreach (var (category, byLayer) in result)
        {
            if (category is "Hair" or "FacialHair")
                continue;
            foreach (var list in byLayer.Values)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var marking = catalog.Markings[list[i].Id];
                    if (!SuitsSpecies(marking, species))
                    {
                        wrongSpecies.Add(list[i].Id);
                        list.RemoveAt(i);
                        continue;
                    }
                    if (marking.SexRestriction != null && marking.SexRestriction != look.Sex)
                    {
                        wrongSex.Add(list[i].Id);
                        list.RemoveAt(i);
                        continue;
                    }
                    if (old.Layer(marking.Layer) is { MarkingsMatchSkin: true } layer)
                        list[i] = list[i] with { Colors = list[i].Colors.Select(_ => skin.WithAlpha(layer.LayerAlpha)).ToList() };
                }
            }
        }
        if (wrongSpecies.Count > 0)
            fixes?.Add(new("markings", $"Markings {species.Id} cannot have were removed: {string.Join(", ", wrongSpecies.Distinct())}."));
        if (wrongSex.Count > 0)
            fixes?.Add(new("markings", $"Markings for another sex were removed: {string.Join(", ", wrongSex.Distinct())}."));

        foreach (var byLayer in result.Values)
        {
            foreach (var layer in byLayer.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
                byLayer.Remove(layer);
        }
        foreach (var category in result.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            result.Remove(category);

        return new CharacterLook { Species = look.Species, Sex = look.Sex, SkinColor = skin, EyeColor = eyes, Markings = result };

        void Add(MarkingInfo marking, MarkingEntry entry)
        {
            if (!result.TryGetValue(marking.Category!, out var byLayer))
                result[marking.Category!] = byLayer = [];
            if (!byLayer.TryGetValue(marking.Layer, out var list))
                byLayer[marking.Layer] = list = [];
            list.Add(entry);
        }
    }

    /// <summary>Whether a species may have the marking (<c>MarkingManager.CanBeApplied</c>, without the sex check).</summary>
    public static bool SuitsSpecies(MarkingInfo marking, SpeciesInfo species) =>
        marking.SpeciesRestriction == null ? !species.Old!.OnlyWhitelisted : marking.SpeciesRestriction.Contains(species.Id);

    // The game keeps whole-byte colours, without alpha.
    private static Rgba Clamp(Rgba color) =>
        new(MathF.Round(Math.Clamp(color.R, 0, 1) * 255f) / 255f, MathF.Round(Math.Clamp(color.G, 0, 1) * 255f) / 255f, MathF.Round(Math.Clamp(color.B, 0, 1) * 255f) / 255f);

    // "FacialHair" to "Facial hair".
    private static string Words(string id) =>
        string.Concat(id.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
