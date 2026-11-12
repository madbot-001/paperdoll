using Avalonia.Controls;
using Avalonia.Input;
using Paperdoll.App.Controls;

namespace Paperdoll.App;

/// <summary>
/// The tab strip, as the lobby's character editor has it: which tab is open, the part each tab
/// last showed, and which of the side list, page and table the open tab uses.
/// </summary>
public partial class MainWindow
{
    private EditorTab _tab = EditorTab.Appearance;
    private IReadOnlyList<EditorTab> _tabsShown = [];
    private readonly Dictionary<EditorTab, RadioButton> _tabButtons = [];

    // The part each tab showed when last left, to show again when it is opened. Kept for this run only.
    private readonly Dictionary<EditorTab, Node> _lastNode = [];

    // How wide the user left the side list, kept while a tab without one is open.
    private GridLength _sideWidth = new(220);

    // The tab and table last arranged. Arranging again for the same leaves the sizes the user
    // dragged the splitters to. Choosing a species swaps the Appearance page for the species
    // table, so it changes the table.
    private (EditorTab Tab, Control? Table)? _arranged;

    // The page and table heights the user left each arrangement with, kept for this run only.
    private readonly Dictionary<(EditorTab Tab, Control? Table), (GridLength Page, GridLength Table)> _splits = [];

    // Choosing a species: the species table fills the editor area in place of the Appearance page.
    private bool _picking;

    /// <summary>Whether the species table is showing in place of the Appearance page.</summary>
    public bool PickingSpecies => _picking;

    /// <summary>The open tab.</summary>
    public EditorTab ShownTab => _tab;

    /// <summary>The tabs in the strip, in strip order.</summary>
    public IReadOnlyList<EditorTab> TabsShown => _tabsShown;

    /// <summary>Opens a tab on the part it last showed, or its top row. Does nothing if the strip has no such tab.</summary>
    public void ShowTab(EditorTab tab)
    {
        if (!_tabsShown.Contains(tab))
            return;
        CommitTyping();
        Open(tab, _lastNode.GetValueOrDefault(tab) ?? TopNode(tab));
    }

    // The tab that holds a part.
    private static EditorTab TabFor(NodeKind kind) => kind switch
    {
        NodeKind.Organ or NodeKind.Layer or NodeKind.Marking => EditorTab.Markings,
        NodeKind.Outfit or NodeKind.LoadoutGroup => EditorTab.Loadout,
        NodeKind.Traits or NodeKind.TraitCategory => EditorTab.Traits,
        NodeKind.Antags => EditorTab.Antags,
        NodeKind.Records or NodeKind.RecordList => EditorTab.Records,
        NodeKind.Allergies => EditorTab.Allergies,
        NodeKind.WardrobeSlot => EditorTab.Wardrobe,
        _ => EditorTab.Appearance,
    };

    // The part a tab opens on before the user has picked one in it. On Markings, the first organ
    // in the side list, once the list shows Markings.
    private Node TopNode(EditorTab tab) => tab switch
    {
        EditorTab.Traits => new Node(NodeKind.Traits),
        EditorTab.Loadout => new Node(NodeKind.Outfit),
        EditorTab.Records => new Node(NodeKind.Records),
        EditorTab.Antags => new Node(NodeKind.Antags),
        EditorTab.Allergies => new Node(NodeKind.Allergies),
        EditorTab.Wardrobe => new Node(NodeKind.WardrobeSlot, Group: WardrobeSlots().FirstOrDefault()),
        EditorTab.Markings => Explorer.Items.OfType<TreeViewItem>().Select(i => i.Tag).OfType<Node>().FirstOrDefault(n => n.Kind == NodeKind.Organ)
            ?? new Node(NodeKind.Character),
        _ => new Node(NodeKind.Character),
    };

    /// <summary>Puts a button in the strip and an item in the View menu for each tab the fork has, when those changed.</summary>
    private void BuildStrip()
    {
        var tabs = EditorTabs.Visible(_session!);
        OutfitButton.IsEnabled = tabs.Contains(EditorTab.Loadout);
        if (tabs.SequenceEqual(_tabsShown))
            return;
        _tabsShown = tabs;
        TabStrip.Children.Clear();
        _tabButtons.Clear();
        ViewMenu.Items.Clear();
        var accessKeys = new HashSet<char>();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var button = new RadioButton { Classes = { "tab" }, Content = EditorTabs.Title(tab), Tag = tab };
            TabStripPanel.SetIsUtility(button, EditorTabs.IsUtility(tab));
            // Click also fires on the tab already open, which leaves the species table.
            button.Click += (_, _) => ShowTab(tab);
            TabStrip.Children.Add(button);
            _tabButtons[tab] = button;

            // The first letter of the title no other item has taken is the access key.
            var title = EditorTabs.Title(tab);
            var key = Math.Max(0, title.ToList().FindIndex(c => accessKeys.Add(char.ToUpperInvariant(c))));
            var item = new MenuItem { Header = title.Insert(key, "_") };
            // Ctrl+1 to Ctrl+9 open the first nine tabs.
            if (i < 9)
                item.InputGesture = item.HotKey = new KeyGesture(Key.D1 + i, KeyModifiers.Control);
            item.Click += (_, _) => ShowTab(tab);
            ViewMenu.Items.Add(item);
        }
    }

    /// <summary>Shows the side list, page and table the open tab uses, and gives the space of those it does not use to the others.</summary>
    private void ArrangeForTab()
    {
        // Choosing a species belongs to Appearance; leaving it ends the choice.
        if (_tab != EditorTab.Appearance)
            _picking = false;
        if (_tabButtons.TryGetValue(_tab, out var button))
            button.IsChecked = true;

        Control? table = _tab switch
        {
            EditorTab.Appearance when _picking => SpeciesPanel,
            EditorTab.Jobs => JobsPanel,
            EditorTab.Traits => TraitsPanel,
            EditorTab.Markings => MarkingsPanel,
            EditorTab.Loadout when _selected.Kind == NodeKind.Outfit => OutfitPanel,
            EditorTab.Wardrobe when _selected.Group != null => WardrobePanel,
            EditorTab.Credits => CreditsGrid,
            EditorTab.Messages => MessagesList,
            EditorTab.Source => SourceText,
            _ => null,
        };
        if (_arranged == (_tab, table))
            return;
        if (_arranged is { } left)
            _splits[left] = (PageArea.RowDefinitions[0].Height, PageArea.RowDefinitions[2].Height);
        _arranged = (_tab, table);
        foreach (var child in TableHost.Children)
            child.IsVisible = child == table;

        var side = EditorTabs.HasSideList(_tab);
        if (SideList.IsVisible)
            _sideWidth = EditorArea.ColumnDefinitions[0].Width;
        SideList.IsVisible = SideSplitter.IsVisible = side;
        EditorArea.ColumnDefinitions[0].Width = side ? _sideWidth : new GridLength(0);
        EditorArea.ColumnDefinitions[1].Width = new GridLength(side ? 5 : 0);

        var page = !EditorTabs.IsUtility(_tab) && !_picking;
        PagePane.IsVisible = page;
        TablePane.IsVisible = table != null;
        TableSplitter.IsVisible = page && table != null;
        PageArea.RowDefinitions[0].Height = !page ? new GridLength(0)
            : _tab == EditorTab.Jobs ? GridLength.Auto
            : GridLength.Star;
        PageArea.RowDefinitions[1].Height = new GridLength(TableSplitter.IsVisible ? 5 : 0);
        PageArea.RowDefinitions[2].Height = table != null ? GridLength.Star : new GridLength(0);
        if (TableSplitter.IsVisible && _splits.TryGetValue((_tab, table), out var kept))
            (PageArea.RowDefinitions[0].Height, PageArea.RowDefinitions[2].Height) = kept;
    }

    /// <summary>Swaps the Appearance page for the species table, with the character's species highlighted.</summary>
    public void ShowSpeciesPicker()
    {
        if (_tab != EditorTab.Appearance || _session?.Look == null)
            return;
        _picking = true;
        SelectCurrentSpecies();
        ArrangeForTab();
    }

    // Shows the Appearance page again.
    private void EndSpeciesPicker()
    {
        _picking = false;
        ArrangeForTab();
    }
}
