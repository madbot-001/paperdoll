// Ported from Space Station 14's HumanoidCharacterProfile.EnsureValid (the loadout part) and
// RoleLoadout.EnsureValid, Copyright (c) 2017-2026 Space Wizards Federation, MIT licence.
// See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Outfits;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// The game's check of saved loadouts, applied to the file as read so that loadouts it leaves
/// alone are written back unchanged. Playtime needs are left to the server, which knows them.
/// </summary>
internal static class LoadoutRules
{
    public static void Check(CharacterFile file, OutfitCatalog outfits, string species, List<RuleFix> fixes)
    {
        if (!file.Profile.Children.TryGetValue(new YamlScalarNode("_loadouts"), out var node) || node is not YamlMappingNode roles)
            return;
        var notes = new List<string>();

        foreach (var (key, value) in roles.Children.ToList())
        {
            var roleId = ((YamlScalarNode)key).Value!;
            if (!outfits.RoleLoadouts.TryGetValue(roleId, out var groupIds))
            {
                roles.Children.Remove(key);
                notes.Add($"{roleId} is not a job here, so its loadout was removed");
                continue;
            }
            if (value is not YamlMappingNode role)
            {
                roles.Children.Remove(key);
                file.RoleLoadout(roleId, create: true);
                continue;
            }

            // The game sets the role from the key before checking, so a missing one is filled in.
            if (CharacterFile.Scalar(role, "role") != roleId)
                role.Children[new YamlScalarNode("role")] = new YamlScalarNode(roleId);

            CheckName(role, roleId, outfits, notes);
            CheckGroups(role, roleId, groupIds, outfits, species, notes);
        }

        if (notes.Count > 0)
            fixes.Add(new("loadouts", "Loadouts were fitted to the game's rules: " + string.Join("; ", notes) + "."));
    }

    // Only roles that allow it keep a name. As in the game, only a name over the length is trimmed
    // and cut (so it may end in a space); a shorter one is kept as written; a blank one is none.
    private static void CheckName(YamlMappingNode role, string roleId, OutfitCatalog outfits, List<string> notes)
    {
        var name = CharacterFile.NullableScalar(role, "entityName");
        if (!role.Children.ContainsKey(new YamlScalarNode("entityName")))
        {
            role.Children[new YamlScalarNode("entityName")] = CharacterFile.Null();
            return;
        }
        if (name == null)
            return;
        string? kept = null;
        if (outfits.NamedRoles.ContainsKey(roleId))
        {
            var trimmed = name.Trim();
            kept = trimmed.Length == 0 ? null
                : trimmed.Length > CharacterRules.MaxNameLength ? trimmed[..CharacterRules.MaxNameLength]
                : name;
        }
        if (kept == name)
            return;
        role.Children[new YamlScalarNode("entityName")] = kept == null ? CharacterFile.Null() : CharacterFile.Text(kept);
        notes.Add(outfits.NamedRoles.ContainsKey(roleId) ? $"the {roleId} name was trimmed to fit" : $"{roleId} takes no custom name, so it was cleared");
    }

    private static void CheckGroups(YamlMappingNode role, string roleId, IReadOnlyList<string> groupIds, OutfitCatalog outfits, string species, List<string> notes)
    {
        if (!role.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selectedNode) || selectedNode is not YamlMappingNode selected)
            role.Children[new YamlScalarNode("selectedLoadouts")] = selected = new YamlMappingNode();

        // Every group of the role gets an entry, as the game adds any it had not picked up.
        foreach (var groupId in groupIds.Where(g => !selected.Children.ContainsKey(new YamlScalarNode(g))))
            selected.Children[new YamlScalarNode(groupId)] = new YamlSequenceNode();

        foreach (var (key, value) in selected.Children.ToList())
        {
            var groupId = ((YamlScalarNode)key).Value!;
            if (!groupIds.Contains(groupId) || !outfits.Groups.TryGetValue(groupId, out var group))
            {
                selected.Children.Remove(key);
                notes.Add($"{roleId} has no group {groupId}");
                continue;
            }

            var entries = (value as YamlSequenceNode)?.Children.OfType<YamlMappingNode>().ToList() ?? [];
            var kept = group.MaxLimit > 0 ? entries.Take(group.MaxLimit).ToList() : entries.ToList();
            if (kept.Count < entries.Count)
                notes.Add($"{groupId} takes at most {group.MaxLimit}");
            for (var i = kept.Count - 1; i >= 0; i--)
            {
                var id = CharacterFile.Scalar(kept[i], "prototype");
                var reason = id == null || !outfits.Loadouts.TryGetValue(id, out var loadout) ? $"{id ?? "an entry"} is not a loadout here"
                    : !group.Loadouts.Contains(id) ? $"{id} is not in {groupId}"
                    : outfits.Check(loadout, species) == LoadoutCheck.WrongSpecies ? $"{id} is not for {species}"
                    : null;
                if (reason != null)
                {
                    kept.RemoveAt(i);
                    notes.Add(reason);
                }
            }

            // Too few: the first loadouts that suit, as the game fills them (skipping those that need playtime).
            foreach (var id in group.Loadouts)
            {
                if (kept.Count >= group.MinLimit)
                    break;
                if (kept.Any(e => CharacterFile.Scalar(e, "prototype") == id) || !outfits.Loadouts.TryGetValue(id, out var loadout)
                    || outfits.Check(loadout, species) != LoadoutCheck.Allowed)
                    continue;
                kept.Add(new YamlMappingNode { { "prototype", id } });
                notes.Add($"{groupId} needs {group.MinLimit}, so {id} was added");
            }

            if (value is not YamlSequenceNode || !kept.SequenceEqual(entries))
                selected.Children[key] = new YamlSequenceNode(kept);
        }
    }
}
