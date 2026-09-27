using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Profiles;

namespace Paperdoll.App;

/// <summary>The inspector pages for Delta-V's and Euphoria's character records and Euphoria's allergies.</summary>
public partial class MainWindow
{
    private static string RecordListName(string list) => list switch
    {
        "employmentEntries" => "Employment",
        "securityEntries" => "Security",
        "medicalEntries" => "Medical",
        _ => list,
    };

    private void InspectRecords()
    {
        var session = _session!;
        var file = session.File!;
        InspectorTitle.Text = "Records";

        AddCategory("Body");
        AddRow("Height", Number(CharacterRecords.Number(file, "height"), CharacterRecords.MaxHeight, value => Apply(s => s.SetRecordNumber("height", value))),
            "Centimetres, as written in the records. Separate from how tall the character is drawn.");
        var weight = CharacterRecords.Number(file, "weight");
        AddRow("Weight", Number(weight, CharacterRecords.MaxWeight, value => Apply(s => s.SetRecordNumber("weight", value))),
            $"Kilograms (about {weight * 2.2046226218:0} lb).");

        AddCategory("Personal");
        AddRow("Emergency contact", RecordText(file, "emergencyContactName", "Who to call"));
        var work = new CheckBox { Content = "Allowed to work here", IsChecked = CharacterRecords.WorkAuthorization(file), Margin = new Thickness(4, 0) };
        work.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && work.IsChecked is { } value)
                Apply(s => s.SetWorkAuthorization(value));
        };
        AddRow("Work authorisation", work);
        AddRow("Identifying features", RecordText(file, "identifyingFeatures", "Scars, tattoos, prosthetics..."));

        AddCategory("Medical");
        AddRow("Allergies", RecordText(file, "allergies", "None"));
        AddRow("Drug allergies", RecordText(file, "drugAllergies", "None"));
        AddRow("After death", RecordText(file, "postmortemInstructions", "Return home"), "Postmortem instructions.");

        AddCategory("Entries");
        foreach (var list in CharacterRecords.EntryLists)
        {
            var count = CharacterRecords.Entries(file, list).Count;
            var open = new Button { Classes = { "small" }, Content = $"{count} entr{(count == 1 ? "y" : "ies")}...", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => Select(new Node(NodeKind.RecordList, Group: list));
            AddRow(RecordListName(list), open);
        }
    }

    private void InspectRecordList(string list)
    {
        var session = _session!;
        var entries = CharacterRecords.Entries(session.File!, list).ToList();
        InspectorTitle.Text = $"Records: {RecordListName(list)}";

        void Save(List<RecordEntry> changed) => Apply(s => s.SetRecordEntries(list, changed));

        for (var i = 0; i < entries.Count; i++)
        {
            var index = i;
            var entry = entries[i];
            AddCategory(string.IsNullOrWhiteSpace(entry.Title) ? $"Entry {i + 1}" : entry.Title);
            AddRow("Title", EntryText(entry.Title, CharacterRecords.ShortText, false, text => Save(Replace(entries, index, entry with { Title = text }))));
            AddRow("Involved", EntryText(entry.Involved, CharacterRecords.ShortText, false, text => Save(Replace(entries, index, entry with { Involved = text }))));
            AddRow("Description", EntryText(entry.Description, CharacterRecords.LongText, true, text => Save(Replace(entries, index, entry with { Description = text }))));
            if (entry.Title.Length == 0 || entry.Involved.Length == 0 || entry.Description.Length == 0)
                AddWide(new TextBlock { Text = "The game's record editor asks for all three before it saves an entry.", Classes = { "hint", "warning" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2) });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 3) };
            actions.Children.Add(ActionButton("Move up", index > 0, () => Save(Move(entries, index, -1))));
            actions.Children.Add(ActionButton("Move down", index < entries.Count - 1, () => Save(Move(entries, index, 1))));
            actions.Children.Add(ActionButton("Remove", true, () => Save(entries.Where((_, j) => j != index).ToList())));
            AddWide(actions);
        }

        if (entries.Count == 0)
            AddWide(new TextBlock { Text = "No entries yet.", Classes = { "hint" }, Margin = new Thickness(6, 4) });
        var add = new Button { Classes = { "small" }, Content = "Add an entry", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 4) };
        add.Click += (_, _) => Save([.. entries, new RecordEntry("", "", "")]);
        AddWide(add);

        static List<RecordEntry> Replace(List<RecordEntry> list, int index, RecordEntry entry) => list.Select((e, j) => j == index ? entry : e).ToList();

        static List<RecordEntry> Move(List<RecordEntry> list, int index, int delta)
        {
            var moved = list.ToList();
            var entry = moved[index];
            moved.RemoveAt(index);
            moved.Insert(index + delta, entry);
            return moved;
        }
    }

    private void InspectAllergies()
    {
        var session = _session!;
        var strings = session.Content!.Strings;
        var allergies = Allergies.Read(session.File!).ToList();
        var reagents = session.Reagents().ToDictionary(r => r.Id);
        InspectorTitle.Text = "Allergies";
        string Name(string id) => reagents.TryGetValue(id, out var r) ? strings.Get(r.NameKey) : id;

        AddCategory("Allergic to");
        if (allergies.Count == 0)
            AddWide(new TextBlock { Text = "None. Add reagents below.", Classes = { "hint" }, Margin = new Thickness(6, 4) });
        for (var i = 0; i < allergies.Count; i++)
        {
            var index = i;
            var (reagent, amount) = allergies[i];
            var names = Allergies.Strengths.Select(s => s.Name).ToList();
            var custom = Allergies.StrengthName(amount) == null;
            if (custom)
                names.Add($"Custom ({amount.ToString("0.##", CultureInfo.InvariantCulture)})");
            var strength = new ComboBox
            {
                ItemsSource = names,
                SelectedIndex = custom ? names.Count - 1 : Array.FindIndex(Allergies.Strengths, s => s.Name == Allergies.StrengthName(amount)),
                MinWidth = 120,
            };
            strength.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && strength.SelectedIndex >= 0 && strength.SelectedIndex < Allergies.Strengths.Length)
                    Apply(s => s.SetAllergies(allergies.Select((a, j) => j == index ? (a.Reagent, Allergies.Strengths[strength.SelectedIndex].Amount) : a)));
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(strength);
            row.Children.Add(ActionButton("Remove", true, () => Apply(s => s.SetAllergies(allergies.Where((_, j) => j != index)))));
            if (!reagents.ContainsKey(reagent))
                ToolTip.SetTip(row, $"{reagent} is not a reagent in this fork; the game keeps it anyway.");
            AddRow(Name(reagent), row);
        }

        AddCategory("Add");
        var filter = new TextBox { PlaceholderText = "Filter reagents", Margin = new Thickness(4, 2) };
        var list = new ListBox { Height = 220, Margin = new Thickness(4, 2) };
        var taken = allergies.Select(a => a.Reagent).ToHashSet(StringComparer.Ordinal);
        var choices = reagents.Values.Where(r => !taken.Contains(r.Id))
            .Select(r => (r.Id, Name: strings.Get(r.NameKey), r.Group))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        void Fill()
        {
            var text = filter.Text?.Trim() ?? "";
            list.ItemsSource = choices
                .Where(c => text.Length == 0 || c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                    || c.Group.Contains(text, StringComparison.CurrentCultureIgnoreCase) || c.Id.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    // The reagent's name, with its group as a quiet column on the right.
                    var row = new DockPanel();
                    var group = new TextBlock { Text = c.Group, Classes = { "hint" }, Margin = new Thickness(8, 0, 0, 0) };
                    DockPanel.SetDock(group, Dock.Right);
                    row.Children.Add(group);
                    row.Children.Add(new TextBlock { Text = c.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                    return new ListBoxItem { Content = row, Tag = c.Id };
                }).ToList();
        }
        Fill();
        filter.TextChanged += (_, _) => Fill();
        void AddSelected()
        {
            if (list.SelectedItem is ListBoxItem { Tag: string id })
                Apply(s => s.SetAllergies([.. allergies, (id, Allergies.DefaultAmount)]));
        }
        list.DoubleTapped += (_, _) => AddSelected();
        var add = new Button { Classes = { "small" }, Content = "Add as Moderate", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2) };
        add.Click += (_, _) => AddSelected();
        var panel = new StackPanel();
        panel.Children.Add(filter);
        panel.Children.Add(list);
        panel.Children.Add(add);
        AddWide(panel);
    }

    private TextBox RecordText(CharacterFile file, string key, string placeholder)
    {
        var box = new TextBox { Text = CharacterRecords.Text(file, key), PlaceholderText = placeholder, MaxLength = CharacterRecords.ShortText };
        CommitOnEnterOrLeave(box, text => Apply(s => s.SetRecordText(key, text)));
        return box;
    }

    private TextBox EntryText(string text, int maxLength, bool multiline, Action<string> commit)
    {
        var box = new TextBox { Text = text, MaxLength = maxLength, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 60 : 0 };
        CommitOnEnterOrLeave(box, commit);
        return box;
    }

    private NumericUpDown Number(int value, int max, Action<int> commit)
    {
        var box = new NumericUpDown { Minimum = 0, Maximum = max, Value = value, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
        box.ValueChanged += (_, e) =>
        {
            if (!_refreshing && e.NewValue is { } number && (int)number != value)
                commit((int)number);
        };
        return box;
    }
}
