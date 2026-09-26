// Ported from Space Station 14 (Content.Shared/Preferences/HumanoidCharacterProfile.EnsureValid,
// Content.Shared/Humanoid/HumanoidCharacterAppearance.EnsureValid and the EnsureValid* methods of
// Content.Shared/Humanoid/Markings/MarkingManager.cs), Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Text.RegularExpressions;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Profiles;

/// <summary>Something the rules changed, and why, for showing to the user.</summary>
public sealed record RuleFix(string Field, string Message);

/// <summary>
/// The checks the game runs on every imported or saved character (<c>EnsureValid</c>), with the
/// server settings at their defaults. Jobs, antagonists, traits and loadouts are left as they are:
/// the server prunes those itself.
/// </summary>
public static partial class CharacterRules
{
    public const int MaxNameLength = 32;
    public const int MaxFlavorTextLength = 512;
    public const string DefaultSpecies = "Human";

    private static readonly string[] Sexes = ["Male", "Female", "Unsexed"];
    private static readonly string[] Genders = ["Epicene", "Female", "Male", "Neuter"];

    /// <summary>Applies the game's rules to the file in place and lists what changed.</summary>
    public static IReadOnlyList<RuleFix> EnsureValid(CharacterFile file, CharacterCatalog catalog, ForkInfo fork)
    {
        var fixes = new List<RuleFix>();

        // The look is read before the species or sex change, and checked against the sex as it
        // was written, as the game does.
        var look = file.ReadLook(catalog);

        var speciesId = file.Species;
        if (speciesId == null || !catalog.Species.TryGetValue(speciesId, out var species) || !species.RoundStart)
        {
            fixes.Add(new("species", $"{speciesId ?? "No species"} is not a round-start species here; set to {DefaultSpecies}."));
            speciesId = DefaultSpecies;
            species = catalog.Species[DefaultSpecies];
            file.Species = speciesId;
        }

        var sex = Sexes.Contains(file.Sex) ? file.Sex! : "Male";
        if (!species.Sexes.Contains(sex))
            sex = species.Sexes[0];
        if (sex != file.Sex)
        {
            fixes.Add(new("sex", $"{species.Id} can be {string.Join(", ", species.Sexes)}; set to {sex}."));
            file.Sex = sex;
        }

        var age = Math.Clamp(file.Age ?? species.MinAge, species.MinAge, species.MaxAge);
        if (age != file.Age)
        {
            fixes.Add(new("age", $"{species.Id} ages run from {species.MinAge} to {species.MaxAge}; set to {age}."));
            file.Age = age;
        }

        if (!Genders.Contains(file.Gender))
        {
            fixes.Add(new("gender", "Unknown pronouns; set to Epicene."));
            file.Gender = "Epicene";
        }

        var name = CheckName(file.Name ?? "", fork.NameRule);
        if (name != (file.Name ?? ""))
        {
            fixes.Add(new("name", name.Length == 0
                ? "The name is empty or has no allowed characters; the game will pick a random one."
                : $"Names are at most {MaxNameLength} characters, may use {fork.NameRule.Description}, and start words with capitals."));
            file.Name = name;
        }

        var flavor = file.FlavorText ?? "";
        var cleanFlavor = RemoveMarkup(flavor);
        if (cleanFlavor.Length > MaxFlavorTextLength)
            cleanFlavor = cleanFlavor[..MaxFlavorTextLength];
        if (cleanFlavor != flavor)
        {
            fixes.Add(new("flavorText", $"Description markup is removed and text cut to {MaxFlavorTextLength} characters."));
            file.FlavorText = cleanFlavor;
        }

        if (CharacterSize.HasHeight(fork))
        {
            var written = CharacterSize.ReadHeight(file);
            var height = CharacterSize.CheckHeight(written ?? 1f, species);
            if (written != null && written != height)
                fixes.Add(new("height", $"{species.Id} heights run from {species.MinHeight:0.##} to {species.MaxHeight:0.##}, in steps of 0.01; set to {height:0.##}."));
            if (written != height)
                CharacterSize.WriteHeight(file, height);
        }

        var checkedLook = EnsureValidLook(look, species, catalog, fixes);
        file.WriteLook(new CharacterLook
        {
            Species = speciesId,
            Sex = sex,
            SkinColor = checkedLook.SkinColor,
            EyeColor = checkedLook.EyeColor,
            Markings = checkedLook.Markings,
        });
        return fixes;
    }

    /// <summary>The name after the game's rules: cut to length, trimmed, filtered, capitalised.</summary>
    public static string CheckName(string name, NameRule rule)
    {
        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];
        name = rule.RemoveDisallowed(name.Trim());
        // Capitalise the first word and the last word, as the game's ic.name_case does.
        return NameCase().Replace(name, m => m.Groups["word"].Value.ToUpperInvariant());
    }

    /// <summary>
    /// The appearance after the game's rules: skin colour pulled into the species' range, markings
    /// on organs the species lacks dropped, and for each organ: colours padded or cut to the
    /// marking's sprites, group and sex restrictions, layer membership, per-layer limits, and
    /// required layers given their default markings. The sex used is the one on the look.
    /// </summary>
    public static CharacterLook EnsureValidLook(CharacterLook look, SpeciesInfo species, CharacterCatalog catalog, List<RuleFix>? fixes = null)
    {
        var skin = catalog.SkinRuleFor(species).EnsureValid(look.SkinColor);
        if (skin != look.SkinColor)
            fixes?.Add(new("skinColor", $"{species.Id} skin must follow its colour rule; moved to the nearest allowed colour."));
        var eyes = new Rgba(Round(look.EyeColor.R), Round(look.EyeColor.G), Round(look.EyeColor.B));

        var result = new Dictionary<string, Dictionary<string, List<MarkingEntry>>>();
        var organs = species.Organs.Where(o => o.MarkingGroup != null).ToDictionary(o => o.Category);

        foreach (var organ in look.Markings.Keys.Where(k => !organs.ContainsKey(k)))
            fixes?.Add(new("markings", $"{species.Id} has no {organ} for markings; they are removed."));

        foreach (var (category, organ) in organs)
        {
            var sets = look.Markings.TryGetValue(category, out var existing)
                ? existing.ToDictionary(kv => kv.Key, kv => kv.Value.ToList())
                : new Dictionary<string, List<MarkingEntry>>();
            catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup!, out var group);
            var before = Count(sets);

            ValidColors(sets, catalog);
            ValidGroupAndSex(sets, catalog, group, look.Sex);
            ValidLayers(sets, catalog, organ.MarkingLayers);
            ValidLimits(sets, catalog, group, organ.MarkingLayers, skin, eyes);

            if (Count(sets) < before)
                fixes?.Add(new("markings", $"Some {category} markings are not allowed for {species.Id} or are over a layer's limit; they are removed."));
            if (sets.Count > 0)
                result[category] = sets;
        }

        return new CharacterLook { Species = look.Species, Sex = look.Sex, SkinColor = skin, EyeColor = eyes, Markings = result };

        static int Count(Dictionary<string, List<MarkingEntry>> sets) => sets.Values.Sum(l => l.Count);
        static float Round(float v) => MathF.Round(Math.Clamp(v, 0, 1) * 255f) / 255f;
    }

    // Fewer colours than sprites: repeat the last (or white); more: drop the extras.
    private static void ValidColors(Dictionary<string, List<MarkingEntry>> sets, CharacterCatalog catalog)
    {
        foreach (var markings in sets.Values)
        {
            for (var i = markings.Count - 1; i >= 0; i--)
            {
                if (!catalog.Markings.TryGetValue(markings[i].Id, out var marking))
                {
                    markings.RemoveAt(i);
                    continue;
                }
                var colors = markings[i].Colors.ToList();
                var sprites = marking.Sprites.Count;
                if (colors.Count < sprites)
                    colors.AddRange(Enumerable.Repeat(colors.Count == 0 ? Rgba.White : colors[^1], sprites - colors.Count));
                else if (colors.Count > sprites)
                    colors = colors.Take(sprites).ToList();
                markings[i] = markings[i] with { Colors = colors };
            }
        }
    }

    private static void ValidGroupAndSex(Dictionary<string, List<MarkingEntry>> sets, CharacterCatalog catalog, MarkingsGroupInfo? group, string sex)
    {
        foreach (var markings in sets.Values)
        {
            for (var i = markings.Count - 1; i >= 0; i--)
            {
                if (!catalog.Markings.TryGetValue(markings[i].Id, out var marking) || !CanBeApplied(group, sex, marking))
                    markings.RemoveAt(i);
            }
        }
    }

    /// <summary>Whether the marking suits the markings group and sex (<c>MarkingManager.CanBeApplied</c>).</summary>
    public static bool CanBeApplied(MarkingsGroupInfo? group, string sex, MarkingInfo marking)
    {
        if (group == null)
            return false;
        var whitelisted = group.Limits.TryGetValue(marking.Layer, out var limit) && limit.OnlyGroupWhitelisted is { } layerOnly
            ? layerOnly
            : group.OnlyGroupWhitelisted;
        if (marking.GroupWhitelist == null ? whitelisted : !marking.GroupWhitelist.Contains(group.Id))
            return false;
        return marking.SexRestriction == null || marking.SexRestriction == sex;
    }

    private static void ValidLayers(Dictionary<string, List<MarkingEntry>> sets, CharacterCatalog catalog, IReadOnlyList<string> layers)
    {
        foreach (var (layer, markings) in sets.ToList())
        {
            for (var i = markings.Count - 1; i >= 0; i--)
            {
                if (!catalog.Markings.TryGetValue(markings[i].Id, out var marking) || !layers.Contains(marking.Layer))
                    markings.RemoveAt(i);
            }
            if (markings.Count == 0)
                sets.Remove(layer);
        }
    }

    // Counts from the end of each list, so when a layer is over its limit the earliest go.
    private static void ValidLimits(Dictionary<string, List<MarkingEntry>> sets, CharacterCatalog catalog, MarkingsGroupInfo? group,
        IReadOnlyList<string> layers, Rgba skin, Rgba eyes)
    {
        if (group == null)
            return;
        var counts = new Dictionary<string, int>();
        foreach (var markings in sets.Values)
        {
            for (var i = markings.Count - 1; i >= 0; i--)
            {
                if (!catalog.Markings.TryGetValue(markings[i].Id, out var marking))
                {
                    markings.RemoveAt(i);
                    continue;
                }
                if (!group.Limits.TryGetValue(marking.Layer, out var limit))
                    continue;
                var count = counts.GetValueOrDefault(marking.Layer);
                if (count >= limit.Limit)
                {
                    markings.RemoveAt(i);
                    continue;
                }
                counts[marking.Layer] = count + 1;
            }
        }

        foreach (var layer in layers)
        {
            if (!group.Limits.TryGetValue(layer, out var limit) || counts.GetValueOrDefault(layer) > 0 || !limit.Required)
                continue;
            foreach (var id in limit.Default)
            {
                if (!catalog.Markings.TryGetValue(id, out var marking))
                    continue;
                if (!sets.TryGetValue(layer, out var list))
                    sets[layer] = list = [];
                list.Add(new MarkingEntry(id, MarkingColoring.LayerColors(marking, skin, eyes, list)));
            }
        }
    }

    // Removes [tag], [tag=value] and [/tag] markup, keeping escaped brackets' text.
    private static string RemoveMarkup(string text) => Markup().Replace(text, string.Empty);

    [GeneratedRegex(@"(?<!\\)\[/?[A-Za-z][^\]]*\]")]
    private static partial Regex Markup();

    [GeneratedRegex(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)")]
    private static partial Regex NameCase();
}
