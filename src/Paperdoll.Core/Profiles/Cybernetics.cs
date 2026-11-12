// Starlight's roundstart cybernetics: which parts a character starts with, what they cost, and
// how parts come attached to each other. Written for Paperdoll from how Starlight behaves (its
// profile's EnsureValid and the lobby's cybernetics picker); none of its code is copied.

using Paperdoll.Core.Characters;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// The <c>cybernetics</c> list of a Starlight profile: entity ids of limb parts. Installing a part
/// brings the parts attached to it; a part another installed part brought cannot be removed on
/// its own. Together they may cost no more than the species' capacity.
/// </summary>
public static class Cybernetics
{
    public const string Key = "cybernetics";

    /// <summary>What the parts cost, each counted once, as the game counts them. Unknown ids cost nothing.</summary>
    public static int Cost(IEnumerable<string> ids, IReadOnlyDictionary<string, CyberneticInfo> all) =>
        ids.Distinct(StringComparer.Ordinal).Sum(id => all.TryGetValue(id, out var part) ? part.Cost : 0);

    /// <summary>The installed parts with a part added after them, then the parts attached to it and theirs; none twice.</summary>
    public static List<string> With(IReadOnlyList<string> installed, string id, IReadOnlyDictionary<string, CyberneticInfo> all)
    {
        var result = installed.ToList();
        Add(id);
        return result;

        void Add(string part)
        {
            if (result.Contains(part) || !all.TryGetValue(part, out var info))
                return;
            result.Add(part);
            foreach (var attached in info.AttachedParts)
                Add(attached);
        }
    }

    /// <summary>The installed part that brought this one with it, which has to be removed instead; null if none did.</summary>
    public static string? AttachedBy(IReadOnlyList<string> installed, string id, IReadOnlyDictionary<string, CyberneticInfo> all) =>
        installed.FirstOrDefault(other => other != id && all.TryGetValue(other, out var info) && info.AttachedParts.Contains(id));

    /// <summary>The installed parts without a part and the parts attached to it, except any another remaining part holds.</summary>
    public static List<string> Without(IReadOnlyList<string> installed, string id, IReadOnlyDictionary<string, CyberneticInfo> all)
    {
        var result = installed.ToList();
        Remove(id);
        return result;

        void Remove(string part)
        {
            if (result.RemoveAll(p => p == part) == 0 || !all.TryGetValue(part, out var info))
                return;
            foreach (var attached in info.AttachedParts.Where(a => AttachedBy(result, a, all) == null))
                Remove(attached);
        }
    }

    /// <summary>
    /// As the game checks them: only limb parts the fork has are kept, each once; if together they
    /// cost more than the species' capacity, none are kept. Kept parts stay in the file's order.
    /// </summary>
    public static void Check(CharacterFile file, IReadOnlyDictionary<string, CyberneticInfo> all, SpeciesInfo species, List<RuleFix> fixes)
    {
        var written = file.Cybernetics;
        var unknown = written.Where(id => !all.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        var organs = written.Where(id => all.TryGetValue(id, out var part) && part.Type == CyberneticType.Organ).Distinct(StringComparer.Ordinal).ToList();
        var kept = written.Where(id => all.TryGetValue(id, out var part) && part.Type == CyberneticType.Limb).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            fixes.Add(new(Key, $"Cybernetics this fork does not have were removed: {string.Join(", ", unknown)}."));
        if (organs.Count > 0)
            fixes.Add(new(Key, $"Organ cybernetics were removed, as the game keeps only limbs: {string.Join(", ", organs.Select(id => all[id].Name))}."));
        var cost = Cost(kept, all);
        if (cost > species.CyberwareCapacity)
        {
            fixes.Add(new(Key, species.CyberwareCapacity == 0
                ? $"{species.Id} cannot start with cybernetics, so the game removes them all."
                : $"The cybernetics cost {cost} points, more than {species.Id}'s {species.CyberwareCapacity}, so the game removes them all."));
            kept = [];
        }
        else if (unknown.Count == 0 && organs.Count == 0 && kept.Count < written.Count)
            fixes.Add(new(Key, "Cybernetics listed twice were merged."));
        if (!kept.SequenceEqual(written))
            file.SetCybernetics(kept);
    }
}
