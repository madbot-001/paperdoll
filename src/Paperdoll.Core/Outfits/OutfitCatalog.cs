// The outfit rules follow Space Station 14's lobby preview
// (Content.Client/Lobby/UI/ProfileEditorControls/ProfilePreviewSpriteView.Humanoid.cs),
// RoleLoadout.SetDefault and SharedStationSpawningSystem.EquipStartingGear,
// Copyright (c) 2017-2026 Space Wizards Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Outfits;

public sealed record JobInfo(string Id, string NameKey, string? StartingGear, bool SetPreference, string? PreviewEntity);

public sealed record DepartmentInfo(string Id, string NameKey, IReadOnlyList<string> Roles);

/// <summary>An antagonist role players can say they want (<c>setPreference</c>); the server checks any playtime it needs.</summary>
public sealed record AntagInfo(string Id, string NameKey, string? ObjectiveKey, bool SetPreference, bool HasRequirements);

public sealed record LoadoutGroupInfo(string Id, string NameKey, int MinLimit, int MaxLimit, int DefaultSelected, bool Hidden, IReadOnlyList<string> Loadouts);

/// <summary>A loadout: gear it equips (its starting gear first, then its own), and its conditions.</summary>
public sealed record LoadoutInfo(string Id, string? StartingGear, IReadOnlyDictionary<string, string> Equipment, YamlMappingNode Node)
{
    /// <summary>Items it puts in the hands and into the item in a slot (usually the backpack).</summary>
    public GearContents Contents { get; init; } = GearContents.Empty;
}

/// <summary>What a set of gear carries besides what it wears: items for the hands, and items put into the item in a slot.</summary>
public sealed record GearContents(IReadOnlyList<string> InHand, IReadOnlyDictionary<string, IReadOnlyList<string>> Storage)
{
    public static readonly GearContents Empty = new([], new Dictionary<string, IReadOnlyList<string>>());
}

/// <summary>One item a character starts with, and the gear or loadout it came from (null for the job's own gear).</summary>
public sealed record GearItem(string Entity, string? Loadout);

/// <summary>
/// Everything a character spawns with: worn items by slot, items in hand, and items put into the
/// item in a slot (the backpack, a belt) by that slot.
/// </summary>
public sealed record SpawnGear(IReadOnlyDictionary<string, GearItem> Worn, IReadOnlyList<GearItem> InHand, IReadOnlyDictionary<string, IReadOnlyList<GearItem>> Stored);

/// <summary>An item an entity spawns holding: how many, and the chance when not certain.</summary>
public sealed record FillItem(string Entity, int Amount, float Chance)
{
    /// <summary>One of several items of which only one is picked (an entity table's group).</summary>
    public bool OneOf { get; init; }

    /// <summary>A random count, such as "1 to 3", when the amount is not fixed.</summary>
    public string? AmountRange { get; init; }
}

/// <summary>Whether a loadout can be picked, as far as Paperdoll can tell.</summary>
public enum LoadoutCheck
{
    Allowed,

    /// <summary>Needs playtime or a whitelist, which only the server knows.</summary>
    ServerChecks,

    /// <summary>Not for this species.</summary>
    WrongSpecies,
}

/// <summary>A job's loadout as the character has it: selected loadouts by group, in group order.</summary>
public sealed record RoleLoadout(string Role, IReadOnlyList<(string Group, IReadOnlyList<string> Loadouts)> Groups);

/// <summary>Jobs, their starting gear and loadouts in one fork.</summary>
public sealed class OutfitCatalog
{
    /// <summary>The job players fall back to when they pick none (upstream's overflow job).</summary>
    public const string FallbackJob = "Passenger";

    private readonly PrototypeIndex _prototypes;

    private OutfitCatalog(PrototypeIndex prototypes) => _prototypes = prototypes;

    public required IReadOnlyDictionary<string, JobInfo> Jobs { get; init; }
    public required IReadOnlyList<DepartmentInfo> Departments { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> StartingGear { get; init; }
    public IReadOnlyDictionary<string, GearContents> StartingGearContents { get; init; } = new Dictionary<string, GearContents>();
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> RoleLoadouts { get; init; }
    public required IReadOnlyDictionary<string, LoadoutGroupInfo> Groups { get; init; }
    public required IReadOnlyDictionary<string, LoadoutInfo> Loadouts { get; init; }
    public IReadOnlyDictionary<string, AntagInfo> Antags { get; init; } = new Dictionary<string, AntagInfo>();

    public static OutfitCatalog Build(PrototypeIndex index)
    {
        var gear = index.OfKind("startingGear").Where(p => !p.Abstract)
            .ToDictionary(p => p.Id, p => (IReadOnlyDictionary<string, string>)Equipment(index.Resolve("startingGear", p.Id)!), StringComparer.Ordinal);

        return new OutfitCatalog(index)
        {
            Jobs = index.OfKind("job").ToDictionary(p => p.Id, p =>
            {
                var node = index.Resolve("job", p.Id)!;
                return new JobInfo(p.Id, Str(node, "name") ?? p.Id, Str(node, "startingGear"),
                    Str(node, "setPreference") is not ("false" or "False"), Str(node, "jobPreviewEntity") ?? Str(node, "jobEntity"));
            }, StringComparer.Ordinal),
            Departments = index.OfKind("department").Select(p =>
            {
                var node = index.Resolve("department", p.Id)!;
                return new DepartmentInfo(p.Id, Str(node, "name") ?? p.Id, Strings(node, "roles"));
            }).ToList(),
            StartingGear = gear,
            StartingGearContents = index.OfKind("startingGear").Where(p => !p.Abstract)
                .ToDictionary(p => p.Id, p => Contents(index.Resolve("startingGear", p.Id)!), StringComparer.Ordinal),
            RoleLoadouts = index.OfKind("roleLoadout")
                .ToDictionary(p => p.Id, p => (IReadOnlyList<string>)Strings(index.Resolve("roleLoadout", p.Id)!, "groups"), StringComparer.Ordinal),
            Groups = index.OfKind("loadoutGroup").Where(p => !p.Abstract).ToDictionary(p => p.Id, p =>
            {
                var node = index.Resolve("loadoutGroup", p.Id)!;
                return new LoadoutGroupInfo(p.Id, Str(node, "name") ?? p.Id,
                    Int(node, "minLimit") ?? 1, Int(node, "maxLimit") ?? 1, Int(node, "defaultSelected") ?? 0,
                    Str(node, "hidden") is "true" or "True", Strings(node, "loadouts"));
            }, StringComparer.Ordinal),
            Loadouts = index.OfKind("loadout").ToDictionary(p => p.Id, p =>
            {
                var node = index.Resolve("loadout", p.Id)!;
                return new LoadoutInfo(p.Id, Str(node, "startingGear"), Equipment(node), node) { Contents = Contents(node) };
            }, StringComparer.Ordinal),
            Antags = index.OfKind("antag").ToDictionary(p => p.Id, p =>
            {
                var node = index.Resolve("antag", p.Id)!;
                return new AntagInfo(p.Id, Str(node, "name") ?? p.Id, Str(node, "objective"),
                    Str(node, "setPreference") is "true" or "True", node.Children.ContainsKey(new YamlScalarNode("requirements")));
            }, StringComparer.Ordinal),
        };
    }

    /// <summary>The role loadout id for a job (the game's <c>LoadoutSystem.GetJobPrototype</c>).</summary>
    public static string RoleFor(string jobId) => "Job" + jobId;

    /// <summary>Jobs a player can put a preference on, by department order then id.</summary>
    public IEnumerable<JobInfo> SelectableJobs() => Jobs.Values.Where(j => j.SetPreference).OrderBy(j => j.Id, StringComparer.Ordinal);

    /// <summary>
    /// What a loadout puts on, slot by slot: its starting gear's items, then its own, each only
    /// into a slot that is still empty (as the game equips them).
    /// </summary>
    public IReadOnlyDictionary<string, string> GearOf(LoadoutInfo loadout)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (loadout.StartingGear != null && StartingGear.TryGetValue(loadout.StartingGear, out var fromGear))
        {
            foreach (var (slot, item) in fromGear)
                result.TryAdd(slot, item);
        }
        foreach (var (slot, item) in loadout.Equipment)
            result.TryAdd(slot, item);
        return result;
    }

    /// <summary>Whether a loadout suits the species, and whether the server has more to check.</summary>
    public LoadoutCheck Check(LoadoutInfo loadout, string species)
    {
        var serverChecks = false;
        foreach (var effect in Effects(loadout.Node))
        {
            switch (effect.Tag)
            {
                case "!type:SpeciesLoadoutEffect":
                    var allowed = Strings(effect.Node, "species");
                    var inverted = Str(effect.Node, "inverted") is "true" or "True";
                    if (allowed.Contains(species) == inverted)
                        return LoadoutCheck.WrongSpecies;
                    break;
                case "!type:JobRequirementLoadoutEffect":
                    serverChecks = true;
                    break;
            }
        }
        return serverChecks ? LoadoutCheck.ServerChecks : LoadoutCheck.Allowed;
    }

    /// <summary>
    /// The character's loadout for a job as the lobby shows it: what they saved, with groups they
    /// have nothing saved for given their defaults (<c>RoleLoadout.SetDefault</c>). Defaults skip
    /// loadouts that need playtime, as a new player would see.
    /// </summary>
    public RoleLoadout LoadoutFor(string jobId, string species, IReadOnlyDictionary<string, IReadOnlyList<string>>? saved)
    {
        var role = RoleFor(jobId);
        var groups = new List<(string, IReadOnlyList<string>)>();
        if (!RoleLoadouts.TryGetValue(role, out var groupIds))
            return new RoleLoadout(role, groups);

        foreach (var groupId in groupIds)
        {
            if (!Groups.TryGetValue(groupId, out var group))
                continue;
            if (saved != null && saved.TryGetValue(groupId, out var chosen))
            {
                groups.Add((groupId, chosen.Where(Loadouts.ContainsKey).ToList()));
                continue;
            }

            var defaults = new List<string>();
            var wanted = Math.Max(group.MinLimit, group.DefaultSelected);
            foreach (var id in group.Loadouts)
            {
                if (defaults.Count >= wanted)
                    break;
                if (Loadouts.TryGetValue(id, out var loadout) && Check(loadout, species) == LoadoutCheck.Allowed)
                    defaults.Add(id);
            }
            groups.Add((groupId, defaults));
        }
        return new RoleLoadout(role, groups);
    }

    /// <summary>
    /// The outfit the lobby preview shows for a job: its starting gear first, then each selected
    /// loadout's gear into slots still empty. Slot name to entity id.
    /// </summary>
    public IReadOnlyDictionary<string, string> OutfitFor(string jobId, RoleLoadout loadout)
    {
        var outfit = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Jobs.TryGetValue(jobId, out var job) && job.StartingGear != null && StartingGear.TryGetValue(job.StartingGear, out var gear))
        {
            foreach (var (slot, item) in gear)
                outfit[slot] = item;
        }
        foreach (var (_, loadoutIds) in loadout.Groups)
        {
            foreach (var id in loadoutIds)
            {
                if (!Loadouts.TryGetValue(id, out var info))
                    continue;
                foreach (var (slot, item) in GearOf(info))
                    outfit.TryAdd(slot, item);
            }
        }
        return outfit;
    }

    /// <summary>
    /// What the character spawns with, as the game equips it (<c>StationSpawningSystem</c>): each
    /// selected loadout in the role's group order, then the job's own gear. Each fills only empty
    /// slots, then puts its items in the hands (two at most) and into the item already worn in a
    /// slot, if that item can hold things.
    /// </summary>
    public SpawnGear GearAtSpawn(string jobId, RoleLoadout loadout)
    {
        var worn = new Dictionary<string, GearItem>(StringComparer.Ordinal);
        var inHand = new List<GearItem>();
        var stored = new Dictionary<string, List<GearItem>>(StringComparer.Ordinal);

        void Apply(IReadOnlyDictionary<string, string> equipment, GearContents contents, string? source)
        {
            foreach (var (slot, item) in equipment)
                worn.TryAdd(slot, new GearItem(item, source));
            foreach (var item in contents.InHand)
            {
                if (inHand.Count < 2)
                    inHand.Add(new GearItem(item, source));
            }
            foreach (var (slot, items) in contents.Storage)
            {
                if (items.Count == 0 || !worn.TryGetValue(slot, out var holder) || !CanHold(holder.Entity))
                    continue;
                if (!stored.TryGetValue(slot, out var list))
                    stored[slot] = list = [];
                list.AddRange(items.Select(i => new GearItem(i, source)));
            }
        }

        foreach (var (_, loadoutIds) in loadout.Groups)
        {
            foreach (var id in loadoutIds)
            {
                if (Loadouts.TryGetValue(id, out var info))
                    Apply(info.Equipment, info.Contents, id);
            }
        }
        if (Jobs.TryGetValue(jobId, out var job) && job.StartingGear != null && StartingGear.TryGetValue(job.StartingGear, out var gear))
            Apply(gear, StartingGearContents.GetValueOrDefault(job.StartingGear) ?? GearContents.Empty, null);

        return new SpawnGear(worn, inHand, stored.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GearItem>)kv.Value));
    }

    /// <summary>Whether an item can hold others (it has a <c>Storage</c> component).</summary>
    public bool CanHold(string entityId) => Component(entityId, "Storage") != null;

    /// <summary>
    /// What an item spawns holding, such as a survival box's contents: an older <c>StorageFill</c>
    /// list, or an entity table (<c>EntityTableContainerFill</c>), whose nested tables are opened and
    /// whose groups, which pick one item, are marked.
    /// </summary>
    public IReadOnlyList<FillItem> FillOf(string entityId)
    {
        var result = new List<FillItem>();
        if (Component(entityId, "StorageFill") is { } fill && fill.Children.TryGetValue(new YamlScalarNode("contents"), out var contents) && contents is YamlSequenceNode list)
        {
            foreach (var entry in list.Children.OfType<YamlMappingNode>().Where(e => Str(e, "id") != null))
                result.Add(new FillItem(Str(entry, "id")!, Int(entry, "amount") ?? 1, Chance(entry)));
        }
        if (Component(entityId, "EntityTableContainerFill") is { } table && table.Children.TryGetValue(new YamlScalarNode("containers"), out var containers) && containers is YamlMappingNode byContainer)
        {
            foreach (var selector in byContainer.Children.Values)
                Walk(selector, 1f, false, 0, result);
        }
        return result;
    }

    // An entity table selector: all of its children, one of them (a group), another table, or an item.
    private void Walk(YamlNode node, float chance, bool oneOf, int depth, List<FillItem> result)
    {
        if (depth > 8 || node is not YamlMappingNode map)
            return;
        chance *= Chance(map);
        var tag = map.Tag.IsEmpty ? "" : map.Tag.Value;
        switch (tag)
        {
            case "!type:AllSelector" or "!type:GroupSelector":
                if (map.Children.TryGetValue(new YamlScalarNode("children"), out var children) && children is YamlSequenceNode seq)
                {
                    foreach (var child in seq.Children)
                        Walk(child, chance, oneOf || tag == "!type:GroupSelector", depth + 1, result);
                }
                break;
            case "!type:NestedSelector":
                if (Str(map, "tableId") is { } tableId && _prototypes.Resolve("entityTable", tableId) is { } nested
                    && nested.Children.TryGetValue(new YamlScalarNode("table"), out var inner))
                    Walk(inner, chance, oneOf, depth + 1, result);
                break;
            default:
                if (Str(map, "id") is { } id)
                {
                    var amount = map.Children.TryGetValue(new YamlScalarNode("amount"), out var amountNode) ? amountNode : null;
                    var range = amount is YamlMappingNode ranged && Str(ranged, "range") is { } text ? text.Replace(",", " to", StringComparison.Ordinal) : null;
                    result.Add(new FillItem(id, amount is YamlScalarNode { Value: { } n } && int.TryParse(n, out var count) ? count : 1, chance)
                    {
                        OneOf = oneOf,
                        AmountRange = range,
                    });
                }
                break;
        }
    }

    private static float Chance(YamlMappingNode node) =>
        float.TryParse(Str(node, "prob"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var chance) ? chance : 1f;

    private YamlMappingNode? Component(string entityId, string type) =>
        _prototypes.Resolve("entity", entityId) is { } entity && entity.Children.TryGetValue(new YamlScalarNode("components"), out var components) && components is YamlSequenceNode list
            ? list.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Str(c, "type") == type)
            : null;

    /// <summary>Every entity any starting gear or loadout puts on, for fetching their sprites.</summary>
    public IReadOnlySet<string> AllGearEntities()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gear in StartingGear.Values)
            set.UnionWith(gear.Values);
        foreach (var loadout in Loadouts.Values)
            set.UnionWith(loadout.Equipment.Values);
        return set;
    }

    private sealed record Effect(string Tag, YamlMappingNode Node);

    // A loadout's effects, with effect groups opened up.
    private IEnumerable<Effect> Effects(YamlMappingNode node, int depth = 0)
    {
        if (depth > 5 || !node.Children.TryGetValue(new YamlScalarNode("effects"), out var list) || list is not YamlSequenceNode seq)
            yield break;
        foreach (var item in seq.Children.OfType<YamlMappingNode>())
        {
            var tag = item.Tag.IsEmpty ? "" : item.Tag.Value;
            if (tag == "!type:GroupLoadoutEffect" && Str(item, "proto") is { } groupId
                && _prototypes.Resolve("loadoutEffectGroup", groupId) is { } group)
            {
                foreach (var inner in Effects(group, depth + 1))
                    yield return inner;
            }
            else
                yield return new Effect(tag, item);
        }
    }

    private static GearContents Contents(YamlMappingNode node)
    {
        var storage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (node.Children.TryGetValue(new YamlScalarNode("storage"), out var storageNode) && storageNode is YamlMappingNode map)
        {
            foreach (var (slot, items) in map.Children)
            {
                if (items is YamlSequenceNode seq)
                    storage[((YamlScalarNode)slot).Value!] = seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList();
            }
        }
        return new GearContents(Strings(node, "inhand"), storage);
    }

    private static Dictionary<string, string> Equipment(YamlMappingNode node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.Children.TryGetValue(new YamlScalarNode("equipment"), out var equipment) && equipment is YamlMappingNode map)
        {
            foreach (var (slot, item) in map.Children)
            {
                if (item is YamlScalarNode { Value: { Length: > 0 } id })
                    result[((YamlScalarNode)slot).Value!] = id;
            }
        }
        return result;
    }

    private static string? Str(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private static int? Int(YamlMappingNode node, string key) => int.TryParse(Str(node, key), out var value) ? value : null;

    private static List<string> Strings(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList()
            : [];
}
