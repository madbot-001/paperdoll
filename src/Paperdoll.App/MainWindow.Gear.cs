using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Paperdoll.Core.Outfits;

namespace Paperdoll.App;

/// <summary>What the character spawns with: the Outfit tab and the list on the Outfit page.</summary>
public partial class MainWindow
{
    // Slots in the order the inventory shows them, with plain names.
    private static readonly (string Slot, string Name)[] Slots =
    [
        ("head", "Head"), ("eyes", "Eyes"), ("ears", "Ears"), ("mask", "Mask"), ("neck", "Neck"),
        ("outerClothing", "Suit"), ("jumpsuit", "Jumpsuit"), ("gloves", "Gloves"), ("belt", "Belt"),
        ("back", "Back"), ("shoes", "Shoes"), ("id", "ID"), ("pocket1", "Pocket"), ("pocket2", "Pocket"),
        ("suitstorage", "Suit storage"),
    ];

    private static string SlotName(string slot) => Slots.FirstOrDefault(s => s.Slot == slot).Name ?? Words(slot);

    private static int SlotOrder(string slot)
    {
        var index = Array.FindIndex(Slots, s => s.Slot == slot);
        return index < 0 ? Slots.Length : index;
    }

    private void OnShowOutfit(object? sender, RoutedEventArgs e) => Select(new Node(NodeKind.Outfit));

    /// <summary>Rows for everything the character spawns with, worn first, then held, then carried.</summary>
    private List<GearRow> GearRows()
    {
        var session = _session!;
        if (session.GearAtSpawn() is not { } gear)
            return [];
        var outfits = session.Content!.Outfits;
        var job = session.DressedJob()!;
        var groupOf = session.LoadoutFor(job).Groups
            .SelectMany(g => g.Loadouts.Select(l => (Loadout: l, g.Group)))
            .GroupBy(x => x.Loadout).ToDictionary(g => g.Key, g => g.First().Group);
        // The loadout group an item came from reads better than the loadout, which is often named after the item.
        string From(GearItem item) => item.Loadout == null ? "Job gear"
            : groupOf.TryGetValue(item.Loadout, out var group) && outfits.Groups.TryGetValue(group, out var info) ? session.Content.Strings.Get(info.NameKey)
            : session.LoadoutName(item.Loadout);
        GearRow Row(string where, GearItem item) =>
            new(where, session.EntityName(item.Entity), From(item), item.Entity, item.Loadout != null ? groupOf.GetValueOrDefault(item.Loadout) : null);

        var rows = new List<GearRow>();
        foreach (var (slot, item) in gear.Worn.OrderBy(kv => SlotOrder(kv.Key)))
            rows.Add(Row(SlotName(slot), item));
        foreach (var item in gear.InHand)
            rows.Add(Row("In hand", item));
        foreach (var (slot, items) in gear.Stored.OrderBy(kv => SlotOrder(kv.Key)))
        {
            var holder = gear.Worn[slot];
            foreach (var item in items)
            {
                rows.Add(Row($"In {session.EntityName(holder.Entity)}", item));
                // What comes inside a box, one level down.
                foreach (var fill in outfits.FillOf(item.Entity))
                    rows.Add(new GearRow($"In {session.EntityName(item.Entity)}", FillText(fill), "Comes inside it", fill.Entity, null));
            }
        }
        // What a worn item holds from the start (a filled belt or bag).
        foreach (var (slot, item) in gear.Worn.OrderBy(kv => SlotOrder(kv.Key)))
        {
            foreach (var fill in outfits.FillOf(item.Entity))
                rows.Add(new GearRow($"In {session.EntityName(item.Entity)}", FillText(fill), "Comes inside it", fill.Entity, null));
        }
        return rows;

        string FillText(FillItem fill) =>
            (fill.AmountRange != null ? $"{fill.AmountRange} × " : fill.Amount > 1 ? $"{fill.Amount} × " : "")
            + session.EntityName(fill.Entity)
            + (fill.OneOf ? " (one of these)" : "")
            + (fill.Chance < 1f ? $" ({fill.Chance:0%} chance)" : "");
    }

    private void BuildGearTable()
    {
        var session = _session!;
        var rows = GearRows();
        GearGrid.ItemsSource = rows;
        var job = session.DressedJob();
        var jobName = job != null && session.Content!.Outfits.Jobs.TryGetValue(job, out var info) ? session.Content.Strings.Get(info.NameKey) : job;
        OutfitSummary.Text = job == null
            ? "No job to dress for."
            : $"What {session.File?.Name ?? "the character"} spawns with as {jobName}: loadouts go on first, then the job's own gear fills the empty slots. Double-click an item to change its loadout.";
    }

    private void OnGearDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GearGrid.SelectedItem is GearRow { Group: { } group })
            Select(new Node(NodeKind.LoadoutGroup, Group: group));
    }

    /// <summary>The spawn list on the Outfit page, grouped by where each item is.</summary>
    private void AddSpawnList()
    {
        var rows = GearRows();
        if (rows.Count == 0)
            return;
        AddCategory("Spawns with");
        foreach (var group in rows.GroupBy(r => r.Where.StartsWith("In ", StringComparison.Ordinal) || r.Where == "In hand" ? r.Where : "Worn"))
        {
            if (group.Key == "Worn")
            {
                foreach (var row in group)
                    AddRow(row.Where, Text(row.Item), row.From);
            }
            else
            {
                var list = string.Join("\n", group.Select(r => r.Item));
                AddRow(group.Key, new TextBlock { Text = list, Margin = new Thickness(4, 2), TextWrapping = TextWrapping.Wrap });
            }
        }
        var table = new Button { Classes = { "small" }, Content = "Show in the Outfit tab", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(6, 4) };
        table.Click += (_, _) => BottomTabs.SelectedItem = OutfitTab;
        AddWide(table);
    }
}
