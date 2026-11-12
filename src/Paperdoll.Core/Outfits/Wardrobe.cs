// Which slots an item is worn in follows Space Station 14's ClothingComponent.Slots
// (Content.Shared/Clothing/Components/ClothingComponent.cs), SlotFlags
// (Content.Shared/Inventory/SlotFlags.cs), the slot flags of the human inventory
// (Resources/Prototypes/InventoryTemplates/human_inventory_template.yml) and
// InventorySystem.CanEquip, Copyright (c) 2017-2026 Space Wizards Federation, MIT licence.
// See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Outfits;

/// <summary>
/// The fork's wearable clothing, for trying on in the preview: every entity that is not abstract
/// and has a <c>Clothing</c> component, under each inventory slot its <c>slots</c> flags fit.
/// </summary>
public sealed class Wardrobe
{
    // Slot flags to the inventory slots that take them. LEGS and PREVENTEQUIP have no slot.
    private static readonly Dictionary<string, string[]> FlagSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HEAD"] = ["head"],
        ["EYES"] = ["eyes"],
        ["EARS"] = ["ears"],
        ["MASK"] = ["mask"],
        ["OUTERCLOTHING"] = ["outerClothing"],
        ["INNERCLOTHING"] = ["jumpsuit"],
        ["NECK"] = ["neck"],
        ["BACK"] = ["back"],
        ["BELT"] = ["belt"],
        ["GLOVES"] = ["gloves"],
        ["IDCARD"] = ["id"],
        ["POCKET"] = ["pocket1", "pocket2"],
        ["FEET"] = ["shoes"],
        ["SUITSTORAGE"] = ["suitstorage"],
    };

    /// <summary>Entity ids by slot, in prototype id order.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> BySlot { get; init; }

    /// <summary>The items that fit a slot.</summary>
    public IReadOnlyList<string> Items(string slot) => BySlot.GetValueOrDefault(slot) ?? [];

    /// <summary>Every wearable item, once each.</summary>
    public IEnumerable<string> Entities() => BySlot.Values.SelectMany(items => items).Distinct(StringComparer.Ordinal);

    public static Wardrobe Build(PrototypeIndex index)
    {
        var bySlot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // Only entities with clothing somewhere in their parents are worked out in full: there are
        // thousands of entities, and working each out takes most of a second on upstream.
        var hasClothing = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var prototype in index.OfKind("entity").Where(p => !p.Abstract).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (!HasClothing(index, prototype.Id, hasClothing, 0) || Clothing(index.Resolve("entity", prototype.Id)) is not { } clothing)
                continue;
            foreach (var slot in SlotsOf(clothing))
            {
                if (!bySlot.TryGetValue(slot, out var list))
                    bySlot[slot] = list = [];
                list.Add(prototype.Id);
            }
        }
        return new Wardrobe { BySlot = bySlot.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal) };
    }

    private static bool HasClothing(PrototypeIndex index, string id, Dictionary<string, bool> known, int depth)
    {
        if (known.TryGetValue(id, out var has))
            return has;
        if (depth > 16 || !index.TryGet("entity", id, out var prototype))
            return false;
        has = Clothing(prototype.Node) != null || prototype.Parents.Any(parent => HasClothing(index, parent, known, depth + 1));
        return known[id] = has;
    }

    // The flags as the game reads them: one name, several joined by commas, or a list, any case.
    private static IEnumerable<string> SlotsOf(YamlMappingNode clothing)
    {
        if (!clothing.Children.TryGetValue(new YamlScalarNode("slots"), out var node))
            return [];
        var names = node switch
        {
            YamlScalarNode { Value: { } text } => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            YamlSequenceNode list => list.Children.OfType<YamlScalarNode>().Select(s => s.Value?.Trim() ?? ""),
            _ => [],
        };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.Equals("All", StringComparison.OrdinalIgnoreCase))
                flags.UnionWith(FlagSlots.Keys);
            else if (name.Equals("WITHOUT_POCKET", StringComparison.OrdinalIgnoreCase))
                flags.UnionWith(FlagSlots.Keys.Where(k => k != "POCKET"));
            else
                flags.Add(name);
        }
        return flags.Where(FlagSlots.ContainsKey).SelectMany(f => FlagSlots[f]);
    }

    private static YamlMappingNode? Clothing(YamlMappingNode? entity) =>
        entity != null && entity.Children.TryGetValue(new YamlScalarNode("components"), out var components) && components is YamlSequenceNode list
            ? list.Children.OfType<YamlMappingNode>().FirstOrDefault(c =>
                c.Children.TryGetValue(new YamlScalarNode("type"), out var type) && type is YamlScalarNode { Value: "Clothing" })
            : null;
}
