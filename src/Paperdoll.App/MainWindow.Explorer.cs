using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App;

public partial class MainWindow
{
    private enum NodeKind
    {
        Character,
        Organ,
        Layer,
        Marking,
        Outfit,
        LoadoutGroup,
        Traits,
        TraitCategory,
        Antags,
        Records,
        RecordList,
        Allergies,
    }

    private sealed record Node(NodeKind Kind, string? Organ = null, string? Layer = null, int Index = -1, string? Group = null)
    {
        public string Key => $"{Kind}/{Organ}/{Layer}/{Index}/{Group}";

        public Node? Parent => Kind switch
        {
            NodeKind.Marking => new Node(NodeKind.Layer, Organ, Layer),
            NodeKind.Layer => new Node(NodeKind.Organ, Organ),
            NodeKind.Organ or NodeKind.Outfit or NodeKind.Traits or NodeKind.Antags or NodeKind.Records or NodeKind.Allergies => new Node(NodeKind.Character),
            NodeKind.RecordList => new Node(NodeKind.Records),
            NodeKind.LoadoutGroup => new Node(NodeKind.Outfit),
            NodeKind.TraitCategory => new Node(NodeKind.Traits),
            _ => null,
        };
    }

    private Node _selected = new(NodeKind.Character);
    private readonly Dictionary<string, bool> _expanded = [];
    private Dictionary<string, TreeViewItem> _items = [];

    /// <summary>
    /// The open tab's parts as a tree, the side list. On Markings: organs that take markings,
    /// their layers with how many markings they hold, and the markings on each; layers that can
    /// take nothing are left out. Tabs without a side list leave it empty.
    /// </summary>
    // What the tree showed when last built. An edit that changes nothing in it (age, the
    // description) leaves the tree as it is: rebuilding and laying it out takes long.
    private string? _explorerShows;

    // Everything the tree shows, as text, to tell whether it needs building again.
    private string ExplorerShows()
    {
        var session = _session!;
        var look = session.Look!;
        var file = session.File!;
        var shows = new System.Text.StringBuilder()
            .Append(_tab).Append('|')
            .Append(session.Fork!.Id).Append(session.Content!.Commit).Append('|')
            .Append(file.Name).Append('|').Append(look.Species).Append('|').Append(look.Sex).Append('|')
            .Append(session.ShowClothes).Append('|');
        if (session.DressedJob() is { } job)
        {
            shows.Append(job).Append(':');
            foreach (var (group, chosen) in session.LoadoutFor(job).Groups)
                shows.Append(group).Append('=').AppendJoin(',', chosen).Append(';');
        }
        shows.Append('|').AppendJoin(',', session.SelectedTraits()).Append('|').AppendJoin(',', file.AntagPreferences).Append('|');
        if (session.Fork.Extras.HasFlag(ProfileExtras.Records))
            shows.AppendJoin(',', session.Fork.RecordFormat.EntryLists.Select(list => CharacterRecords.Entries(file, list, session.Fork.RecordFormat).Count));
        if (session.Fork.Extras.HasFlag(ProfileExtras.Allergies))
            shows.Append('|').Append(Allergies.Read(file).Count);
        foreach (var (organ, layers) in look.Markings)
        {
            foreach (var (layer, entries) in layers)
            {
                shows.Append('|').Append(organ).Append('/').Append(layer).Append(':');
                foreach (var entry in entries)
                    shows.Append(entry.Id).Append('=').AppendJoin(',', entry.Colors.Select(c => c.ToHex())).Append(';');
            }
        }
        return shows.ToString();
    }

    private void BuildExplorer()
    {
        var session = _session!;
        var look = session.Look!;
        var species = session.Content!.Characters.Species[look.Species];
        var shows = ExplorerShows();
        if (shows == _explorerShows && Explorer.Items.Count > 0)
        {
            KeepSelection();
            return;
        }
        _explorerShows = shows;
        var items = _items = new Dictionary<string, TreeViewItem>();
        Explorer.ItemsSource = null;
        Explorer.Items.Clear();

        if (_tab == EditorTab.Loadout && session.DressedJob() is { } job)
        {
            var outfits = session.Content.Outfits;
            var jobName = outfits.Jobs.TryGetValue(job, out var info) ? session.Content.Strings.Get(info.NameKey) : job;
            var outfitItem = Item(new Node(NodeKind.Outfit), HeaderFor("Outfit", jobName + (session.ShowClothes ? "" : ", hidden")), true, items);
            foreach (var (groupId, chosen) in session.LoadoutFor(job).Groups)
            {
                if (!outfits.Groups.TryGetValue(groupId, out var group) || group.Hidden)
                    continue;
                var groupItem = Item(new Node(NodeKind.LoadoutGroup, Group: groupId),
                    HeaderFor(session.Content.Strings.Get(group.NameKey), $"{chosen.Count}/{group.MaxLimit}"), false, items);
                foreach (var loadout in chosen)
                    groupItem.Items.Add(new TreeViewItem { Header = new TextBlock { Text = session.LoadoutName(loadout) }, Tag = new Node(NodeKind.LoadoutGroup, Group: groupId), Focusable = false });
                outfitItem.Items.Add(groupItem);
            }
            Explorer.Items.Add(outfitItem);
        }

        var traitCatalog = session.Content.Traits;
        if (_tab == EditorTab.Traits && traitCatalog.Traits.Count > 0)
        {
            var picked = session.SelectedTraits();
            var traitsItem = Item(new Node(NodeKind.Traits), HeaderFor("Traits", $"{picked.Count} picked"), true, items);
            foreach (var category in traitCatalog.Categories.Values.OrderBy(c => c.Priority).ThenBy(c => session.Content.Strings.Get(c.NameKey), StringComparer.CurrentCulture))
            {
                var inCategory = picked.Where(id => traitCatalog.Traits.TryGetValue(id, out var t) && t.Category == category.Id).ToList();
                var detail = category.MaxTraits is { } maxTraits ? $"{inCategory.Count}/{maxTraits}" : inCategory.Count.ToString();
                var categoryItem = Item(new Node(NodeKind.TraitCategory, Group: category.Id), HeaderFor(session.Content.Strings.Get(category.NameKey), detail), false, items);
                foreach (var id in inCategory)
                    categoryItem.Items.Add(new TreeViewItem { Header = new TextBlock { Text = session.Content.Strings.Get(traitCatalog.Traits[id].NameKey) }, Tag = new Node(NodeKind.TraitCategory, Group: category.Id), Focusable = false });
                traitsItem.Items.Add(categoryItem);
            }
            Explorer.Items.Add(traitsItem);
        }

        if (_tab == EditorTab.Records && session.Fork!.Extras.HasFlag(ProfileExtras.Records))
        {
            var recordsItem = Item(new Node(NodeKind.Records), HeaderFor("Records", null), true, items);
            var format = session.Fork.RecordFormat;
            foreach (var list in format.EntryLists)
            {
                var count = CharacterRecords.Entries(session.File!, list, format).Count;
                recordsItem.Items.Add(Item(new Node(NodeKind.RecordList, Group: list), HeaderFor(RecordListName(list), count.ToString()), false, items));
            }
            Explorer.Items.Add(recordsItem);
        }

        if (_tab == EditorTab.Markings)
        {
            foreach (var organ in species.Organs.Where(o => o.MarkingGroup != null))
            {
                var layers = organ.MarkingLayers
                    .Select(layer => (layer, applied: Applied(look, organ.Category, layer), limit: session.LayerLimit(organ, layer)))
                    .Where(l => l.applied.Count > 0 || (l.limit != 0 && session.AvailableMarkings(organ, l.layer).Count > 0))
                    .ToList();
                if (layers.Count == 0)
                    continue;

                // Old-model categories spanning several layers share one limit, shown on the category.
                var shared = session.SharesLimit(organ);
                var sharedLimit = shared ? session.LayerLimit(organ, organ.MarkingLayers[0]) : null;
                var organCount = sharedLimit is { } total ? $"{session.LimitCount(organ, organ.MarkingLayers[0])}/{total}" : null;
                var organItem = Item(new Node(NodeKind.Organ, organ.Category), HeaderFor(Words(organ.Category), organCount), true, items);
                foreach (var (layer, applied, limit) in layers)
                {
                    var count = limit is { } l && !shared ? $"{applied.Count}/{l}" : applied.Count.ToString();
                    var layerItem = Item(new Node(NodeKind.Layer, organ.Category, layer), HeaderFor(Words(layer), count), applied.Count > 0, items);
                    for (var i = 0; i < applied.Count; i++)
                    {
                        var markingItem = Item(new Node(NodeKind.Marking, organ.Category, layer, i),
                            MarkingHeader(session.MarkingName(applied[i].Id), OwnColors(session, applied[i])), false, items);
                        layerItem.Items.Add(markingItem);
                    }
                    organItem.Items.Add(layerItem);
                }
                Explorer.Items.Add(organItem);
            }
        }

        KeepSelection();
    }

    // Keeps the selection on the same part, or the nearest part that still exists, or else the
    // open tab's top row; the tab then shows that part when next opened.
    private void KeepSelection()
    {
        for (Node? node = _selected; node != null; node = node.Parent)
        {
            if (_items.TryGetValue(node.Key, out var item))
            {
                _selected = _lastNode[_tab] = node;
                if (!ReferenceEquals(Explorer.SelectedItem, item))
                    Explorer.SelectedItem = item;
                return;
            }
        }
        _selected = _lastNode[_tab] = TopNode(_tab);
        Explorer.SelectedItem = _items.GetValueOrDefault(_selected.Key);
    }

    private TreeViewItem Item(Node node, Control header, bool expandedByDefault, Dictionary<string, TreeViewItem> items)
    {
        var item = new TreeViewItem
        {
            Header = header,
            Tag = node,
            IsExpanded = _expanded.GetValueOrDefault(node.Key, expandedByDefault),
        };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty && !_refreshing)
                _expanded[node.Key] = item.IsExpanded;
        };
        items[node.Key] = item;
        return item;
    }

    private static Control HeaderFor(string text, string? detail)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = text });
        if (detail != null)
            panel.Children.Add(new TextBlock { Text = detail, Opacity = 0.65, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    // The colours a marking's sprites are chosen in, leaving out sprites linked to another's colour.
    private static List<Rgba> OwnColors(Core.Editing.EditorSession session, MarkingEntry entry) =>
        session.Content!.Characters.Markings.GetValueOrDefault(entry.Id) is { } marking
            ? entry.Colors.Where((_, i) => !marking.IsColorLinked(i)).ToList()
            : entry.Colors.ToList();

    private static Control MarkingHeader(string name, IReadOnlyList<Rgba> colors)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var color in colors.Take(3))
            panel.Children.Add(new Border { Width = 9, Height = 9, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brush(color), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = name, Margin = new Thickness(4, 0, 0, 0) });
        return panel;
    }

    private void OnExplorerSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Explorer.SelectedItem is not TreeViewItem { Tag: Node node })
            return;
        Select(node);
    }

    /// <summary>
    /// Shows a part of the character: opens the tab that holds it, shows the part on the page and
    /// scopes the markings table to it. A part on a tab the fork does not have opens Appearance.
    /// </summary>
    private void Select(Node node)
    {
        var tab = TabFor(node.Kind);
        if (!_tabsShown.Contains(tab))
            (tab, node) = (EditorTab.Appearance, new Node(NodeKind.Character));
        Open(tab, node);
    }

    // Opens a tab on a part of it.
    private void Open(EditorTab tab, Node node)
    {
        var tabChanged = tab != _tab;
        _tab = tab;
        _picking = false;
        _selected = node;
        _refreshing = true;
        try
        {
            // The side list shows the open tab only; its selection may settle on another part.
            if (tabChanged)
                BuildExplorer();
            if (_items.TryGetValue(_selected.Key, out var item) && !ReferenceEquals(Explorer.SelectedItem, item))
            {
                for (var parent = item.Parent as TreeViewItem; parent != null; parent = parent.Parent as TreeViewItem)
                    parent.IsExpanded = true;
                Explorer.SelectedItem = item;
            }
            _lastNode[_tab] = _selected;
            BuildInspector();
            BuildBreadcrumbs();
            ArrangeForTab();
            RefreshMarkingTableSoon();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Where the selection is: fork, species, organ, layer, marking. Each step can be clicked.</summary>
    private void BuildBreadcrumbs()
    {
        var session = _session!;
        var look = session.Look!;
        Breadcrumbs.Children.Clear();

        var steps = new List<(string Text, Node? Target)>
        {
            (session.Fork!.Name, null),
            (session.DisplayName(session.Content!.Characters.Species[look.Species]), new Node(NodeKind.Character)),
        };
        if (_selected.Kind is NodeKind.Outfit or NodeKind.LoadoutGroup)
            steps.Add(("Outfit", new Node(NodeKind.Outfit)));
        if (_selected.Kind is NodeKind.Traits or NodeKind.TraitCategory)
            steps.Add(("Traits", new Node(NodeKind.Traits)));
        if (_selected.Kind is NodeKind.Antags)
            steps.Add(("Antagonists", new Node(NodeKind.Antags)));
        if (_selected.Kind is NodeKind.Records or NodeKind.RecordList)
            steps.Add(("Records", new Node(NodeKind.Records)));
        if (_selected is { Kind: NodeKind.RecordList, Group: { } recordList })
            steps.Add((RecordListName(recordList), _selected));
        if (_selected.Kind is NodeKind.Allergies)
            steps.Add(("Allergies", _selected));
        if (_selected is { Kind: NodeKind.TraitCategory, Group: { } categoryId } && session.Content.Traits.Categories.TryGetValue(categoryId, out var shownCategory))
            steps.Add((session.Content.Strings.Get(shownCategory.NameKey), _selected));
        if (_selected is { Kind: NodeKind.LoadoutGroup, Group: { } groupId } && session.Content.Outfits.Groups.TryGetValue(groupId, out var shownGroup))
            steps.Add((session.Content.Strings.Get(shownGroup.NameKey), _selected));
        if (_selected.Organ != null)
            steps.Add((Words(_selected.Organ), new Node(NodeKind.Organ, _selected.Organ)));
        if (_selected.Layer != null)
            steps.Add((Words(_selected.Layer), new Node(NodeKind.Layer, _selected.Organ, _selected.Layer)));
        if (_selected.Kind == NodeKind.Marking && Applied(look, _selected.Organ!, _selected.Layer!) is var list && _selected.Index < list.Count)
            steps.Add((session.MarkingName(list[_selected.Index].Id), _selected));

        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0)
                Breadcrumbs.Children.Add(new TextBlock { Text = "›", Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) });
            var (text, target) = steps[i];
            if (target == null)
            {
                Breadcrumbs.Children.Add(new TextBlock { Text = text, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
                continue;
            }
            var crumb = new Button { Classes = { "crumb" }, Content = text, FontWeight = i == steps.Count - 1 ? FontWeight.SemiBold : FontWeight.Normal };
            crumb.Click += (_, _) => Select(target);
            Breadcrumbs.Children.Add(crumb);
        }
    }

    private static List<MarkingEntry> Applied(CharacterLook look, string organ, string layer) =>
        look.Markings.TryGetValue(organ, out var byLayer) && byLayer.TryGetValue(layer, out var list) ? list : [];

    // "FacialHair" -> "Facial hair", "ArmLeft" -> "Arm left", "LArm" -> "Left arm", "RFoot" -> "Right foot"
    private static string Words(string id)
    {
        if (id.Length > 2 && id[0] is 'L' or 'R' && char.IsUpper(id[1]))
            id = (id[0] == 'L' ? "Left" : "Right") + id[1..];
        var words = Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " ");
        return words.Length == 0 ? words : words[0] + words[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Selects a part by "Organ/Layer/Index" (any prefix), then opens the tab named, if any; for
    /// screenshots. A record list is named as the side list names it ("Records/Medical").
    /// </summary>
    public void ShowPart(string path, string? tab = null)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var node = parts is ["Antagonists"] ? new Node(NodeKind.Antags)
            : parts is ["Allergies"] ? new Node(NodeKind.Allergies)
            : parts is ["Records", ..] ? parts.Length > 1
                ? new Node(NodeKind.RecordList, Group: _session!.Fork!.RecordFormat.EntryLists.FirstOrDefault(list => RecordListName(list) == parts[1]) ?? parts[1])
                : new Node(NodeKind.Records)
            : parts is ["Traits", ..] ? parts.Length > 1 ? new Node(NodeKind.TraitCategory, Group: parts[1]) : new Node(NodeKind.Traits)
            : parts is ["Outfit", ..] ? parts.Length > 1 ? new Node(NodeKind.LoadoutGroup, Group: parts[1]) : new Node(NodeKind.Outfit) : parts.Length switch
        {
            0 => new Node(NodeKind.Character),
            1 => new Node(NodeKind.Organ, parts[0]),
            2 => new Node(NodeKind.Layer, parts[0], parts[1]),
            _ => new Node(NodeKind.Marking, parts[0], parts[1], int.Parse(parts[2])),
        };
        Select(node);
        if (Enum.TryParse<EditorTab>(tab, out var shown))
            ShowTab(shown);
    }

    private static IBrush Brush(Rgba color) =>
        new SolidColorBrush(Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)));
}
