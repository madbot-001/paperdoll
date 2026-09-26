// The old appearance model's drawing follows Space Station 14 at 27bb73d, the last upstream
// version on it (Content.Client/Humanoid/HumanoidAppearanceSystem.cs and
// Content.Shared/Humanoid/Markings/MarkingsSet.cs), Copyright (c) 2017-2026 Space Wizards
// Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Characters;
using Paperdoll.Core.Profiles;

namespace Paperdoll.Core.Rendering;

public sealed partial class PaperdollRenderer
{
    /// <summary>
    /// A species on the old appearance model, drawn as the lobby does:
    /// <list type="bullet">
    /// <item>Each body layer takes the species' base sprite for it (the per-sex one on the chest and
    /// head), tinted with the skin colour where it matches the skin; the eyes take the eye colour.</item>
    /// <item>Markings are gathered as the lobby does (see <see cref="OldMarkings"/>) and each is inserted
    /// just above its body layer, so later ones end up below earlier ones. Layers that do not allow
    /// markings, or that the species has no base sprite for, show none.</item>
    /// </list>
    /// </summary>
    private List<Slot> OldSlots(SpeciesInfo species, CharacterLook look)
    {
        var old = species.Old!;
        var slots = BaseSlots(species);
        var allowsMarkings = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var bodyLayer in old.Layers)
        {
            var layer = bodyLayer.ForSex(look.Sex);
            var slot = slots[ReserveSlot(slots, layer.Layer)];
            allowsMarkings[layer.Layer] = layer.AllowsMarkings;
            slot.BodyLayer = layer.Layer;
            if (layer.MatchSkin)
                slot.Color = look.SkinColor.WithAlpha(layer.LayerAlpha);
            if (layer.Sprite is { } sprite)
                slot.Sprite = sprite;
        }
        slots[ReserveSlot(slots, "Eyes")].Color = look.EyeColor;

        var drawn = new Dictionary<string, Slot>(StringComparer.Ordinal);
        foreach (var (marking, entry) in OldMarkings(species, look))
        {
            var index = FindSlot(slots, marking.Layer);
            if (index < 0 || !allowsMarkings.GetValueOrDefault(marking.Layer))
                continue;
            var displacement = marking.CanBeDisplaced ? old.MarkingsDisplacement.GetValueOrDefault(marking.Layer) : null;
            for (var i = 0; i < marking.Sprites.Count; i++)
            {
                var color = i < entry.Colors.Count ? entry.Colors[i] : Rgba.White;
                var key = $"{marking.Id}-{marking.Sprites[i].State}";
                // A marking listed twice keeps its first place and takes the later colours.
                if (drawn.TryGetValue(key, out var existing))
                {
                    existing.Color = color;
                    continue;
                }
                var slot = new Slot([key])
                {
                    Sprite = marking.Sprites[i],
                    Color = color,
                    Displacement = displacement,
                    BodyLayer = marking.Layer,
                };
                slots.Insert(index + i + 1, slot);
                drawn[key] = slot;
            }
        }
        return slots;
    }

    // The game adds a layer at the top for a key the doll's sprite does not have.
    private static int ReserveSlot(List<Slot> slots, string layer)
    {
        var index = FindSlot(slots, layer);
        if (index >= 0)
            return index;
        slots.Add(new Slot([LayerPrefix + layer]));
        return slots.Count - 1;
    }

    /// <summary>
    /// The markings the lobby draws, in drawing order (<c>LoadProfile</c>): markings without forced
    /// colours as saved, then hair and facial hair if the species may have them, then markings with
    /// forced colours, coloured by their rules; each category takes as many as its points allow.
    /// Then markings for other species or another sex go, ones on layers that match the skin take
    /// its colour, and categories with points left get their default markings.
    /// </summary>
    private List<(MarkingInfo Marking, MarkingEntry Entry)> OldMarkings(SpeciesInfo species, CharacterLook look)
    {
        var old = species.Old!;
        var points = old.Points.ToDictionary(kv => kv.Key, kv => kv.Value.Points);
        var set = new Dictionary<string, List<(MarkingInfo Marking, MarkingEntry Entry)>>(StringComparer.Ordinal);

        var forced = new List<(MarkingInfo, MarkingEntry)>();
        foreach (var (category, byLayer) in look.Markings)
        {
            if (category is "Hair" or "FacialHair")
                continue;
            foreach (var entry in byLayer.Values.SelectMany(l => l))
            {
                if (!catalog.Markings.TryGetValue(entry.Id, out var marking) || marking.Category == null)
                    continue;
                if (marking.ForcedColoring)
                    forced.Add((marking, entry));
                else
                    AddBack(marking, entry);
            }
        }

        foreach (var category in new[] { "Hair", "FacialHair" })
        {
            if (look.Markings.GetValueOrDefault(category)?.Values.SelectMany(l => l).FirstOrDefault() is not { } hair
                || !catalog.Markings.TryGetValue(hair.Id, out var style))
                continue;
            if (!OldAppearanceRules.SuitsSpecies(style, species) || (style.SexRestriction != null && style.SexRestriction != look.Sex))
                continue;
            var color = old.Layer(category) is { MarkingsMatchSkin: true } layer
                ? look.SkinColor.WithAlpha(layer.LayerAlpha)
                : hair.Colors.FirstOrDefault(Rgba.White);
            // The game adds the style as a marking of the hair category, whatever its own is.
            AddBack(style with { Category = category }, hair with { Colors = [color] });
        }

        foreach (var (marking, entry) in forced)
            AddBack(marking, entry with { Colors = MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, [], FirstIn) });

        foreach (var list in set.Values)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var (marking, entry) = list[i];
                if (!OldAppearanceRules.SuitsSpecies(marking, species) || (marking.SexRestriction != null && marking.SexRestriction != look.Sex))
                {
                    list.RemoveAt(i);
                    if (points.TryGetValue(marking.Category!, out var left))
                        points[marking.Category!] = left + 1;
                    continue;
                }
                if (old.Layer(marking.Layer) is { MarkingsMatchSkin: true } layer)
                    list[i] = (marking, entry with { Colors = entry.Colors.Select(_ => look.SkinColor.WithAlpha(layer.LayerAlpha)).ToList() });
            }
        }

        // EnsureDefault: defaults in order while the category has points left.
        foreach (var (category, limit) in old.Points)
        {
            foreach (var id in limit.Defaults)
            {
                if (points[category] <= 0)
                    break;
                if (catalog.Markings.TryGetValue(id, out var marking))
                    AddBack(marking with { Category = category }, new MarkingEntry(id, MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, [], FirstIn)));
            }
        }

        return set.Values.SelectMany(l => l).ToList();

        void AddBack(MarkingInfo marking, MarkingEntry entry)
        {
            var category = marking.Category!;
            if (points.TryGetValue(category, out var left))
            {
                if (left <= 0)
                    return;
                points[category] = left - 1;
            }
            if (!set.TryGetValue(category, out var list))
                set[category] = list = [];
            list.Add((marking, entry));
        }

        MarkingEntry? FirstIn(string category) =>
            set.TryGetValue(category, out var list) && list.Count > 0 ? list[0].Entry : null;
    }
}
