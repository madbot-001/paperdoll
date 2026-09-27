using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App;

public partial class MainWindow
{
    private static readonly (string Value, string Label)[] Pronouns =
    [
        ("Epicene", "They / them"), ("Male", "He / him"), ("Female", "She / her"), ("Neuter", "It / its"),
    ];

    /// <summary>Fills the inspector for whatever is selected in the explorer.</summary>
    private void BuildInspector()
    {
        Inspector.Children.Clear();
        Inspector.RowDefinitions.Clear();
        var look = _session!.Look!;
        var species = _session.Content!.Characters.Species[look.Species];
        var organ = _selected.Organ != null ? species.Organs.FirstOrDefault(o => o.Category == _selected.Organ) : null;

        switch (_selected.Kind)
        {
            case NodeKind.Organ when organ != null:
                InspectOrgan(organ);
                break;
            case NodeKind.Layer when organ != null:
                InspectLayer(organ, _selected.Layer!);
                break;
            case NodeKind.Marking when organ != null && _selected.Index < Applied(look, organ.Category, _selected.Layer!).Count:
                InspectMarking(organ, _selected.Layer!, _selected.Index);
                break;
            case NodeKind.Outfit:
                InspectOutfit();
                break;
            case NodeKind.LoadoutGroup when _selected.Group != null && _session.Content.Outfits.Groups.ContainsKey(_selected.Group):
                InspectLoadoutGroup(_selected.Group);
                break;
            case NodeKind.Traits:
                InspectTraits();
                break;
            case NodeKind.TraitCategory when _selected.Group != null && _session.Content.Traits.Categories.ContainsKey(_selected.Group):
                InspectTraitCategory(_selected.Group);
                break;
            case NodeKind.Antags:
                InspectAntags();
                break;
            case NodeKind.Records when _session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.Records):
                InspectRecords();
                break;
            case NodeKind.RecordList when _selected.Group != null && _session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.Records):
                InspectRecordList(_selected.Group);
                break;
            case NodeKind.Allergies when _session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.Allergies):
                InspectAllergies();
                break;
            default:
                InspectCharacter(species);
                break;
        }
    }

    private void InspectCharacter(SpeciesInfo species)
    {
        var session = _session!;
        var file = session.File!;
        var look = session.Look!;
        InspectorTitle.Text = "Character";

        AddCategory("Identity");
        var name = new TextBox { Text = file.Name ?? "", PlaceholderText = "Name (required in the game)" };
        CommitOnEnterOrLeave(name, text => Apply(s => s.Edit(f => f.Name = text)));
        var random = new Button { Classes = { "small" }, Content = "Random", Margin = new Thickness(4, 0, 2, 0) };
        ToolTip.SetTip(random, "Pick a random name as the game does for this species");
        random.Click += (_, _) => Apply(s => s.RandomizeName());
        var nameRow = new DockPanel();
        DockPanel.SetDock(random, Dock.Right);
        nameRow.Children.Add(random);
        nameRow.Children.Add(name);
        AddRow("Name", nameRow);

        var speciesRow = new DockPanel();
        var change = new Button { Classes = { "small" }, Content = "Change...", Margin = new Thickness(4, 0, 2, 0) };
        change.Click += (_, _) => BottomTabs.SelectedIndex = 0;
        DockPanel.SetDock(change, Dock.Right);
        speciesRow.Children.Add(change);
        speciesRow.Children.Add(Text(session.DisplayName(species)));
        AddRow("Species", speciesRow);

        // Euphoria: a name shown in place of the species' own.
        if (session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.CustomSpeciesName))
        {
            var custom = new TextBox
            {
                Text = file.GetValue(CustomSpeciesName.Key) ?? "",
                PlaceholderText = session.DisplayName(species),
                MaxLength = CharacterRules.MaxNameLength,
                IsEnabled = species.CustomName,
            };
            CommitOnEnterOrLeave(custom, text => Apply(s => s.SetCustomSpeciesName(text)));
            AddRow("Species name", custom, species.CustomName
                ? "Shown instead of the species' name. Leave it empty to use that."
                : $"{session.DisplayName(species)} cannot have a custom species name.");
        }

        var sex = new ComboBox { ItemsSource = species.Sexes, SelectedItem = file.Sex, HorizontalAlignment = HorizontalAlignment.Stretch };
        sex.SelectionChanged += (_, _) =>
        {
            if (sex.SelectedItem is string value && value != session.File?.Sex)
                Apply(s => s.SetSex(value));
        };
        AddRow("Sex", sex);

        var pronouns = new ComboBox
        {
            ItemsSource = Pronouns.Select(p => p.Label).ToList(),
            SelectedIndex = Array.FindIndex(Pronouns, p => p.Value == file.Gender),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        pronouns.SelectionChanged += (_, _) =>
        {
            if (pronouns.SelectedIndex >= 0 && Pronouns[pronouns.SelectedIndex].Value != session.File?.Gender)
                Apply(s => s.Edit(f => f.Gender = Pronouns[pronouns.SelectedIndex].Value));
        };
        AddRow("Pronouns", pronouns);

        // Forks with upstream's voice choice (emote sounds) list the species' voices.
        if (session.Content!.Characters.HasVoices && species.Voices.Count > 0)
        {
            var voices = species.Voices.ToList();
            var voice = new ComboBox
            {
                ItemsSource = voices.Select(session.VoiceName).ToList(),
                SelectedIndex = voices.IndexOf(file.Voice ?? ""),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = voices.Count > 1,
            };
            voice.SelectionChanged += (_, _) =>
            {
                if (voice.SelectedIndex >= 0 && voices[voice.SelectedIndex] != session.File?.Voice)
                    Apply(s => s.SetVoice(voices[voice.SelectedIndex]));
            };
            ToolTip.SetTip(voice, file.Voice);
            AddRow("Voice", voice);
        }

        var age = new NumericUpDown
        {
            Minimum = species.MinAge,
            Maximum = species.MaxAge,
            Value = file.Age ?? species.MinAge,
            Increment = 1,
            FormatString = "0",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 21,
            Padding = new Thickness(4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        age.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } value && (int)value != session.File?.Age)
                Apply(s => s.Edit(f => f.Age = (int)value));
        };
        AddRow("Age", age, $"{species.MinAge} to {species.MaxAge}");

        if (Core.Profiles.CharacterSize.HasWidth(session.Fork!))
            AddHeightAndWidth(session, species, file);
        else if (Core.Profiles.CharacterSize.HasHeight(session.Fork!))
        {
            var current = Core.Profiles.CharacterSize.Height(file, session.Fork!, species);
            var (panel, value, slider) = SizeSlider(species.MinHeight, species.MaxHeight, current);
            var note = new TextBlock { Classes = { "hint" }, Margin = new Thickness(4, 0, 4, 2), TextWrapping = TextWrapping.Wrap };
            void Describe(float h) => note.Text = $"{species.MinHeight:0.00} to {species.MaxHeight:0.00}; drawn at {species.BaseScale.Y * h:0.00}x"
                + (species.BaseScale.Y != 1 ? $" (species {species.BaseScale.Y:0.##}x)" : "");
            Describe(current);
            slider.ValueChanged += (_, e) =>
            {
                var height = (float)Math.Round(e.NewValue, 2);
                value.Text = height.ToString("0.00");
                Describe(height);
                Apply(s => s.SetHeight(height), keepInspector: true);
            };
            var stack = new StackPanel();
            stack.Children.Add(panel);
            stack.Children.Add(note);
            AddRow("Height", stack);
        }

        AddCategory("Colours");
        var rule = session.Content!.Characters.SkinRuleFor(species);
        if (rule.IsUnary)
        {
            var swatch = Swatch(look.SkinColor);
            var tone = new Slider { Minimum = 0, Maximum = 100, Value = rule.ToUnary(look.SkinColor), MinHeight = 18 };
            var panel = new DockPanel();
            DockPanel.SetDock(swatch, Dock.Right);
            panel.Children.Add(swatch);
            panel.Children.Add(tone);
            tone.ValueChanged += (_, e) =>
            {
                Apply(s => s.SetSkin(rule.FromUnary((float)e.NewValue)), keepInspector: true);
                swatch.Background = Brush(session.Look!.SkinColor);
            };
            AddRow("Skin tone", panel, $"{species.SkinColoration}: one slider");
        }
        else
            AddRow("Skin", ColorField(look.SkinColor, color => Apply(s => s.SetSkin(color))), $"{species.SkinColoration}: nearest allowed colour is used");
        AddRow("Eyes", ColorField(look.EyeColor, color => Apply(s => s.SetEyes(color))));

        AddCategory("Round start");
        var spawnLabels = CharacterRules.SpawnPriorities
            .Select(p => session.Content!.Strings[$"humanoid-profile-editor-preference-spawn-priority-{p.ToLowerInvariant()}"] ?? p).ToList();
        var spawn = new ComboBox
        {
            ItemsSource = spawnLabels,
            SelectedIndex = Array.IndexOf(CharacterRules.SpawnPriorities, file.SpawnPriority ?? "None"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        spawn.SelectionChanged += (_, _) =>
        {
            if (spawn.SelectedIndex >= 0 && CharacterRules.SpawnPriorities[spawn.SelectedIndex] != session.File?.SpawnPriority)
                Apply(s => s.Edit(f => f.SpawnPriority = CharacterRules.SpawnPriorities[spawn.SelectedIndex]));
        };
        AddRow("Spawn priority", spawn, "Where you arrive when joining mid-round.");

        var fallback = session.Fork!.FallbackJob;
        var overflow = session.Content!.Outfits.Jobs.TryGetValue(fallback, out var overflowJob)
            ? session.Content.Strings[overflowJob.NameKey] ?? overflowJob.Id
            : fallback;
        string[] unavailableValues = ["SpawnAsOverflow", "StayInLobby"];
        var unavailable = new ComboBox
        {
            ItemsSource = new[] { $"Join as {overflow}", "Stay in the lobby" },
            SelectedIndex = Array.IndexOf(unavailableValues, file.PreferenceUnavailable ?? "SpawnAsOverflow"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        unavailable.SelectionChanged += (_, _) =>
        {
            if (unavailable.SelectedIndex >= 0 && unavailableValues[unavailable.SelectedIndex] != session.File?.PreferenceUnavailable)
                Apply(s => s.Edit(f => f.PreferenceUnavailable = unavailableValues[unavailable.SelectedIndex]));
        };
        AddRow("No job free", unavailable, "When none of your chosen jobs has room at round start.");

        AddCategory("Description");
        var description = new TextBox
        {
            Text = file.FlavorText ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
            PlaceholderText = "What others see when they examine the character",
            BorderThickness = new Thickness(0),
        };
        CommitOnEnterOrLeave(description, text => Apply(s => s.Edit(f => f.FlavorText = text)));
        AddWide(description);

        AddCategory("File");
        AddRow("Fork label", Mono(file.ForkId ?? ""));
        AddRow("Format", Text("Version 2, new appearance model"));
    }

    private void InspectOrgan(OrganInfo organ)
    {
        var session = _session!;
        InspectorTitle.Text = $"Organ: {Words(organ.Category)}";

        AddCategory("Organ");
        AddRow("Category", Mono(organ.Category));
        AddRow("Entity", Mono(organ.EntityId));
        if (organ.Layer != null)
            AddRow("Drawn on", Mono(organ.Layer));
        if (organ.Sprite is { } sprite)
            AddRow("Sprite", Mono($"{sprite.Rsi}\n{sprite.State}"));
        if (organ.SexStates.Count > 0)
            AddRow("Per sex", Mono(string.Join("\n", organ.SexStates.Select(kv => $"{kv.Key}: {kv.Value}"))));
        AddRow("Markings group", Mono(organ.MarkingGroup ?? ""));

        var shared = session.SharesLimit(organ);
        if (shared && session.LayerLimit(organ, organ.MarkingLayers[0]) is { } total)
            AddRow("Limit", Text($"{session.LimitCount(organ, organ.MarkingLayers[0])} of {total}, shared by its layers"));

        AddCategory("Marking layers");
        foreach (var layer in organ.MarkingLayers)
        {
            var applied = Applied(session.Look!, organ.Category, layer).Count;
            var limit = shared ? null : session.LayerLimit(organ, layer);
            var available = session.AvailableMarkings(organ, layer).Count;
            var open = new Button { Classes = { "crumb" }, Content = Words(layer), HorizontalAlignment = HorizontalAlignment.Left };
            var target = new Node(NodeKind.Layer, organ.Category, layer);
            open.Click += (_, _) => Select(target);
            var held = limit is { } max ? $"{applied} of {max}" : shared ? $"{applied}" : $"{applied} of any";
            AddRow("", open, $"{held}, {available} to choose from");
        }
    }

    private void InspectLayer(OrganInfo organ, string layer)
    {
        var session = _session!;
        InspectorTitle.Text = $"Layer: {Words(organ.Category)} › {Words(layer)}";
        var limit = session.LimitFor(organ, layer);
        var applied = Applied(session.Look!, organ.Category, layer);
        var shared = session.SharesLimit(organ);
        var counted = session.LimitCount(organ, layer);

        AddCategory("Layer");
        AddRow("Layer", Mono(layer));
        AddRow("Limit", Text(limit == null ? "No limit"
            : $"{limit.Limit}" + (shared ? $" for all of {Words(organ.Category)}" : "") + (limit.Required ? ", required" : "")));
        if (limit is { Default.Count: > 0 })
            AddRow("Default", Mono(string.Join("\n", limit.Default)));
        AddRow("To choose from", Text($"{session.AvailableMarkings(organ, layer).Count} markings"));

        AddCategory(applied.Count == 0 ? "Markings" : "Markings (first is drawn on top)");
        for (var i = 0; i < applied.Count; i++)
            AddWide(MarkingLine(organ.Category, layer, i, applied[i], applied.Count));

        var add = new Button { Classes = { "small" }, Content = "Add a marking...", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 4) };
        // A one-marking layer stays open: picking another swaps it.
        add.IsEnabled = limit == null || limit.Limit == 1 || counted < limit.Limit;
        if (limit is { Limit: 1 } && counted == 1)
            add.Content = "Swap for another...";
        add.Click += (_, _) => BottomTabs.SelectedIndex = 1;
        AddWide(add);
    }

    private void InspectMarking(OrganInfo organ, string layer, int index)
    {
        var session = _session!;
        var entry = Applied(session.Look!, organ.Category, layer)[index];
        var marking = session.Content!.Characters.Markings[entry.Id];
        InspectorTitle.Text = $"Marking: {session.MarkingName(entry.Id)}";

        AddCategory("Marking");
        if (MarkingPicture(organ.Category, layer, entry.Id, 64) is { } picture)
            AddRow("Picture", new Image { Source = picture, Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2) });
        AddRow("Name", Text(session.MarkingName(entry.Id)));
        AddRow("Id", Mono(entry.Id));
        AddRow("Layer", Mono(marking.Layer));
        if (marking.SexRestriction != null)
            AddRow("Only for", Text(marking.SexRestriction));

        AddCategory("Colours");
        for (var c = 0; c < entry.Colors.Count; c++)
        {
            // A sprite linked to another's colour has no picker, as in the lobby.
            if (marking.IsColorLinked(c))
                continue;
            var colorIndex = c;
            var label = c < marking.Sprites.Count ? marking.Sprites[c].State ?? $"Sprite {c + 1}" : $"Sprite {c + 1}";
            AddRow(label, ColorField(entry.Colors[c], color => Apply(s => s.SetMarkingColor(organ.Category, layer, index, colorIndex, color))));
        }

        AddCategory("Sprites");
        foreach (var sprite in marking.Sprites)
        {
            var meta = session.Renderer!.Meta(sprite.Rsi);
            AddRow(sprite.State ?? "", Mono(sprite.Rsi), meta?.License);
        }

        AddCategory("Source");
        AddRow("Defined in", Mono(session.SourceOf("marking", entry.Id) ?? "unknown"));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6) };
        actions.Children.Add(ActionButton("Move up", index > 0, () => { _selected = _selected with { Index = index - 1 }; Apply(s => s.MoveMarking(organ.Category, layer, index, -1)); }));
        actions.Children.Add(ActionButton("Move down", index < Applied(session.Look!, organ.Category, layer).Count - 1, () => { _selected = _selected with { Index = index + 1 }; Apply(s => s.MoveMarking(organ.Category, layer, index, 1)); }));
        actions.Children.Add(ActionButton("Remove", true, () => { _selected = _selected.Parent!; Apply(s => s.RemoveMarking(organ.Category, layer, index)); }));
        AddWide(actions);
    }

    private static readonly string[] Priorities = ["High", "Medium", "Low", "Never"];

    private void InspectOutfit()
    {
        var session = _session!;
        var outfits = session.Content!.Outfits;
        var job = session.DressedJob();
        InspectorTitle.Text = "Outfit";

        AddCategory("Job");
        var jobs = new ComboBox { ItemsSource = _jobs.Select(j => j.Name).ToList(), SelectedIndex = _jobs.FindIndex(j => j.Id == job), HorizontalAlignment = HorizontalAlignment.Stretch };
        jobs.SelectionChanged += (_, _) =>
        {
            if (jobs.SelectedIndex >= 0 && _jobs[jobs.SelectedIndex].Id != session.DressedJob())
            {
                session.PreviewJob = _jobs[jobs.SelectedIndex].Id;
                SelectDressedJob();
                RefreshAll();
            }
        };
        AddRow("Shown as", jobs, session.PreviewEntity() is { } body
            ? $"The lobby shows this job as its own body ({session.EntityName(body)}), not the character."
            : "The lobby shows the High priority job; pick another to try its clothes.");
        if (job == null)
            return;

        var priority = new ComboBox { ItemsSource = Priorities, SelectedItem = session.JobPriority(job), HorizontalAlignment = HorizontalAlignment.Stretch };
        priority.SelectionChanged += (_, _) =>
        {
            if (priority.SelectedItem is string value && value != session.JobPriority(job))
                Apply(s => s.SetJobPriority(job, value));
        };
        AddRow("Priority", priority, "Setting High moves another High job to Medium.");

        // Roles such as borgs let the player name what they spawn as.
        if (outfits.NamedRoles.ContainsKey(Core.Outfits.OutfitCatalog.RoleFor(job)))
        {
            var roleName = new TextBox
            {
                Text = session.File!.RoleName(Core.Outfits.OutfitCatalog.RoleFor(job)) ?? "",
                PlaceholderText = "Random when you spawn",
                MaxLength = CharacterRules.MaxNameLength,
            };
            CommitOnEnterOrLeave(roleName, text => Apply(s => s.SetRoleName(job, text)));
            AddRow("Name", roleName, "What you are called in this role, instead of your character's name.");
        }

        AddCategory("Loadout");
        foreach (var (groupId, chosen) in session.LoadoutFor(job).Groups)
        {
            if (!outfits.Groups.TryGetValue(groupId, out var group) || group.Hidden)
                continue;
            var open = new Button { Classes = { "crumb" }, Content = session.Content.Strings.Get(group.NameKey), HorizontalAlignment = HorizontalAlignment.Left };
            var target = new Node(NodeKind.LoadoutGroup, Group: groupId);
            open.Click += (_, _) => Select(target);
            AddRow("", open, chosen.Count == 0 ? "Nothing" : string.Join(", ", chosen.Select(session.LoadoutName)));
        }

        AddSpawnList();
    }

    private void InspectLoadoutGroup(string groupId)
    {
        var session = _session!;
        var outfits = session.Content!.Outfits;
        var group = outfits.Groups[groupId];
        var job = session.DressedJob()!;
        var chosen = session.LoadoutFor(job).Groups.FirstOrDefault(g => g.Group == groupId).Loadouts ?? [];
        InspectorTitle.Text = $"Loadout: {session.Content.Strings.Get(group.NameKey)}";

        AddCategory("Group");
        AddRow("Takes", Text(group.MinLimit == group.MaxLimit ? $"{group.MaxLimit}" : $"{group.MinLimit} to {group.MaxLimit}"));
        AddRow("Id", Mono(groupId));

        AddCategory(group.MaxLimit == 1 ? group.MinLimit > 0 ? "Choose one (required)" : "Choose one or nothing" : $"Choose up to {group.MaxLimit}");
        if (group.MinLimit > 0)
            AddWide(new TextBlock
            {
                Text = $"The game needs at least {group.MinLimit} here; with fewer, it puts back the first it can.",
                Classes = { "hint" },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 2),
            });
        else if (group.MaxLimit == 1)
        {
            // An optional pick-one group can be emptied, which a radio button alone cannot do.
            var nothing = new RadioButton { Content = "Nothing", IsChecked = chosen.Count == 0, GroupName = "loadout-" + groupId };
            nothing.IsCheckedChanged += (_, _) =>
            {
                if (!_refreshing && nothing.IsChecked == true && chosen.Count > 0)
                    Apply(s => s.ToggleLoadout(job, groupId, chosen[0]));
            };
            AddWide(LoadoutRow(nothing, null, 0));
        }

        // As in the lobby, loadouts sharing a groupBy key are one entry: the chosen one (or the
        // first) with a button that opens the rest below it.
        var subLists = group.Loadouts
            .Where(outfits.Loadouts.ContainsKey)
            .GroupBy(id => outfits.Loadouts[id].GroupBy ?? id, StringComparer.Ordinal);
        foreach (var subList in subLists)
        {
            var ids = subList.ToList();
            if (ids.Count == 1)
            {
                AddLoadoutChoice(job, groupId, group, ids[0], chosen, null, 0, null);
                continue;
            }
            var first = ids.FirstOrDefault(chosen.Contains) ?? ids[0];
            var others = ids.Where(id => id != first).ToList();
            var key = $"{groupId}/{subList.Key}";
            var open = _openLoadoutSubLists.Contains(key);
            var toggle = new Button { Classes = { "small" }, Content = open ? "▼" : "▶", Width = 18, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(toggle, open ? "Hide the others" : $"Show {others.Count} more like this");
            toggle.Click += (_, _) =>
            {
                if (!_openLoadoutSubLists.Remove(key))
                    _openLoadoutSubLists.Add(key);
                Select(_selected);
            };
            // "and 2 other items" when some of the rest are chosen too (loadouts-count-items-in-group).
            var alsoChosen = others.Count(chosen.Contains);
            var label = alsoChosen == 0 ? null : $"{session.LoadoutName(first)} and {alsoChosen} other {(alsoChosen == 1 ? "item" : "items")}";
            AddLoadoutChoice(job, groupId, group, first, chosen, toggle, 0, label);
            if (open)
            {
                foreach (var id in others)
                    AddLoadoutChoice(job, groupId, group, id, chosen, null, 1, null);
            }
        }
    }

    // Sub-lists opened on loadout pages, by group and key, kept while the inspector redraws.
    private readonly HashSet<string> _openLoadoutSubLists = [];

    private void AddLoadoutChoice(string job, string groupId, Core.Outfits.LoadoutGroupInfo group, string loadoutId, IReadOnlyList<string> chosen,
        Button? toggle, int depth, string? label)
    {
        var session = _session!;
        var outfits = session.Content!.Outfits;
        var loadout = outfits.Loadouts[loadoutId];
        var check = outfits.Check(loadout, session.Look!.Species);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            Child = LoadoutPicture(loadoutId) is { } picture
                ? new Image { Source = picture, Stretch = Stretch.Uniform }
                : null,
        });
        content.Children.Add(new TextBlock { Text = label ?? session.LoadoutName(loadoutId), VerticalAlignment = VerticalAlignment.Center });
        Avalonia.Controls.Primitives.ToggleButton choice = group.MaxLimit == 1
            ? new RadioButton { Content = content, IsChecked = chosen.Contains(loadoutId), GroupName = "loadout-" + groupId }
            : new CheckBox { Content = content, IsChecked = chosen.Contains(loadoutId) };
        choice.IsEnabled = check != Core.Outfits.LoadoutCheck.WrongSpecies;
        ToolTip.SetTip(choice, loadoutId);
        choice.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && (choice.IsChecked == true) != chosen.Contains(loadoutId))
                Apply(s => s.ToggleLoadout(job, groupId, loadoutId));
        };

        var note = check switch
        {
            Core.Outfits.LoadoutCheck.WrongSpecies => $"Not for {session.DisplayName(session.Content.Characters.Species[session.Look!.Species])}",
            Core.Outfits.LoadoutCheck.ServerChecks => "Needs playtime; the server checks",
            _ => null,
        };
        AddWide(LoadoutRow(choice, toggle, depth));
        if (note != null)
            AddWide(new TextBlock { Text = note, Classes = { "hint", check == Core.Outfits.LoadoutCheck.ServerChecks ? "warning" : "muted" }, Margin = new Thickness(LoadoutIndent(depth) + 60, 0, 4, 2) });
        if (session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.ItemCustomization) && session.CustomizationOf(job, groupId, loadoutId) is { } entry)
            AddCustomization(job, groupId, loadout, entry);
    }

    // A choice with room on its left for a sub-list's open button, so every choice lines up.
    private static DockPanel LoadoutRow(Control choice, Button? toggle, int depth)
    {
        var row = new DockPanel { Margin = new Thickness(LoadoutIndent(depth), 1, 4, 1) };
        Control left = toggle is null ? new Border { Width = 18 } : toggle;
        left.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(left, Dock.Left);
        row.Children.Add(left);
        row.Children.Add(choice);
        return row;
    }

    private static double LoadoutIndent(int depth) => 4 + depth * 22;

    // Loadout pictures by loadout id, drawn once per fork; null for one with no picture.
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _loadoutPictures = [];

    private void ClearLoadoutPictures()
    {
        foreach (var picture in _loadoutPictures.Values)
            picture?.Dispose();
        _loadoutPictures.Clear();
    }

    private Avalonia.Media.Imaging.Bitmap? LoadoutPicture(string loadoutId)
    {
        if (_loadoutPictures.TryGetValue(loadoutId, out var cached))
            return cached;
        Avalonia.Media.Imaging.Bitmap? picture = null;
        try
        {
            using var sprite = _session!.LoadoutPicture(loadoutId);
            if (sprite != null)
            {
                using var icon = Preview.Thumbnail.Fit(sprite, 32);
                picture = Preview.FloorCanvas.ToAvalonia(icon);
            }
        }
        catch (Exception)
        {
            // A loadout whose item cannot be drawn simply has no picture.
        }
        _loadoutPictures[loadoutId] = picture;
        return picture;
    }

    // A marking alone in the colours it has (or would get), cut to what is drawn and enlarged to fill a square.
    private Avalonia.Media.Imaging.Bitmap? MarkingPicture(string organ, string layer, string markingId, int size)
    {
        try
        {
            using var sprite = _session!.MarkingPicture(organ, layer, markingId);
            using var icon = Preview.Thumbnail.Fit(sprite, size);
            return Preview.FloorCanvas.ToAvalonia(icon);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Euphoria's custom name, description and colour tint for a loadout item. Only single-item loadouts can be customised.</summary>
    private void AddCustomization(string job, string groupId, Core.Outfits.LoadoutInfo loadout, LoadoutEntry entry)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(28, 0, 6, 4) };
        if (Core.Outfits.OutfitCatalog.SpawnCount(loadout) != 1)
        {
            panel.Children.Add(new TextBlock { Text = "Gives more than one item, so the game will not rename or colour it.", Classes = { "hint" }, TextWrapping = TextWrapping.Wrap });
            AddWide(panel);
            return;
        }

        void Save(LoadoutEntry changed) => Apply(s => s.SetCustomization(job, groupId, changed));
        var name = new TextBox { Text = entry.Name ?? "", PlaceholderText = "Custom name", MaxLength = 96 };
        CommitOnEnterOrLeave(name, text => Save(entry with { Name = string.IsNullOrWhiteSpace(text) ? null : text }));
        var description = new TextBox { Text = entry.Description ?? "", PlaceholderText = "Custom description", MaxLength = 512, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44 };
        CommitOnEnterOrLeave(description, text => Save(entry with { Description = string.IsNullOrWhiteSpace(text) ? null : text }));

        var tinted = Rgba.TryParse(entry.Color, out var current);
        var tint = new CheckBox { Content = "Colour tint", IsChecked = tinted, MinHeight = 0 };
        tint.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && (tint.IsChecked == true) != tinted)
                Save(entry with { Color = tint.IsChecked == true ? Rgba.White.ToHex() : null });
        };
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        colorRow.Children.Add(tint);
        if (tinted)
            colorRow.Children.Add(ColorField(current, color => Save(entry with { Color = Core.Editing.EditorSession.CustomizationColor(color).ToHex() })));

        panel.Children.Add(name);
        panel.Children.Add(description);
        panel.Children.Add(colorRow);
        panel.Children.Add(new TextBlock { Text = "The game keeps colours solid and between dark grey and white in lightness.", Classes = { "hint" }, TextWrapping = TextWrapping.Wrap });
        AddWide(panel);
    }

    private void InspectTraits()
    {
        var session = _session!;
        var catalog = session.Content!.Traits;
        var rules = session.Fork!.TraitRules;
        var picked = session.SelectedTraits().Where(catalog.Traits.ContainsKey).Select(id => catalog.Traits[id]).ToList();
        var context = session.TraitContext();
        InspectorTitle.Text = "Traits";

        AddCategory("Totals");
        AddRow("Picked", Text(rules.MaxCount is { } maxCount ? $"{picked.Count(t => t.UsesSlots)} of {maxCount}" : $"{picked.Count}"));
        if (rules.MaxPoints is { } maxPoints)
            AddRow("Points", Text($"{picked.Sum(t => t.Cost)} of {maxPoints}"), "Some traits give points back.");
        AddRow("Checked as", Text($"{session.DisplayName(session.Content.Characters.Species[context.Species])}"
            + (context.Job != null ? $", {context.Job}" : "") + (context.Department != null ? $" ({context.Department})" : "")),
            "Conditions on job or department use the job shown in the preview.");

        AddCategory("Categories");
        foreach (var category in catalog.Categories.Values.OrderBy(c => c.Priority).ThenBy(c => session.Content.Strings.Get(c.NameKey), StringComparer.CurrentCulture))
        {
            var inCategory = picked.Where(t => t.Category == category.Id).ToList();
            var open = new Button { Classes = { "crumb" }, Content = session.Content.Strings.Get(category.NameKey), HorizontalAlignment = HorizontalAlignment.Left };
            var target = new Node(NodeKind.TraitCategory, Group: category.Id);
            open.Click += (_, _) => Select(target);
            var limits = new List<string> { inCategory.Count == 0 ? "none picked" : string.Join(", ", inCategory.Select(t => session.Content.Strings.Get(t.NameKey))) };
            if (category.MaxTraits is { } maxTraits)
                limits.Add($"up to {maxTraits}");
            if (category.MaxPoints is { } categoryPoints)
                limits.Add($"{inCategory.Sum(t => t.Cost)} of {categoryPoints} points");
            AddRow("", open, string.Join("; ", limits));
        }
    }

    /// <summary>Antagonists the character is willing to be.</summary>
    private void InspectAntags()
    {
        var session = _session!;
        var strings = session.Content!.Strings;
        var wanted = session.File!.AntagPreferences;
        InspectorTitle.Text = "Antagonists";

        AddCategory("Willing to be");
        foreach (var antag in session.Content.Outfits.Antags.Values.Where(a => a.SetPreference).OrderBy(a => strings.Get(a.NameKey), StringComparer.CurrentCulture))
        {
            var isWanted = wanted.Contains(antag.Id);
            var box = new CheckBox { Content = strings.Get(antag.NameKey), IsChecked = isWanted, Margin = new Thickness(6, 2, 4, 0) };
            ToolTip.SetTip(box, antag.Id);
            box.IsCheckedChanged += (_, _) =>
            {
                if (!_refreshing && (box.IsChecked == true) != isWanted)
                    Apply(s => s.ToggleAntag(antag.Id));
            };
            var stack = new StackPanel();
            stack.Children.Add(box);
            if (antag.ObjectiveKey != null)
                stack.Children.Add(new TextBlock { Text = strings.Get(antag.ObjectiveKey), Classes = { "hint" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 6, 2) });
            if (antag.HasRequirements)
                stack.Children.Add(new TextBlock { Text = "Needs playtime; the server checks and may turn this off.", Classes = { "hint", "warning" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 6, 3) });
            AddWide(stack);
        }
    }

    private void InspectTraitCategory(string categoryId)
    {
        var session = _session!;
        var strings = session.Content!.Strings;
        var catalog = session.Content.Traits;
        var category = catalog.Categories[categoryId];
        var picked = session.SelectedTraits();
        var context = session.TraitContext();
        InspectorTitle.Text = $"Traits: {strings.Get(category.NameKey)}";

        AddCategory("Category");
        var inCategory = picked.Where(id => catalog.Traits.TryGetValue(id, out var t) && t.Category == categoryId).Select(id => catalog.Traits[id]).ToList();
        AddRow("Traits", Text(category.MaxTraits is { } maxTraits ? $"{inCategory.Count(t => t.UsesSlots)} of {maxTraits}" : $"{inCategory.Count}, no limit"));
        if (category.MaxPoints is { } maxPoints)
            AddRow("Points", Text($"{inCategory.Sum(t => t.Cost)} of {maxPoints}"));

        AddCategory("Pick");
        foreach (var trait in catalog.Traits.Values.Where(t => t.Category == categoryId).OrderBy(t => strings.Get(t.NameKey), StringComparer.CurrentCulture))
        {
            var isPicked = picked.Contains(trait.Id);
            var status = catalog.Evaluate(trait, context);
            var whyNot = isPicked ? null : catalog.WhyNot(trait, context, session.Fork!.TraitRules);
            var cost = trait.Cost == 0 ? "" : trait.Cost > 0 ? $"  ({trait.Cost} pt)" : $"  (gives {-trait.Cost} pt)";
            var box = new CheckBox { Content = strings.Get(trait.NameKey) + cost, IsChecked = isPicked, IsEnabled = isPicked || whyNot == null, Margin = new Thickness(6, 2, 4, 0) };
            ToolTip.SetTip(box, trait.Id);
            box.IsCheckedChanged += (_, _) =>
            {
                if (!_refreshing && (box.IsChecked == true) != isPicked)
                    Apply(s => s.ToggleTrait(trait.Id));
            };
            var stack = new StackPanel();
            stack.Children.Add(box);
            if (trait.DescriptionKey != null)
                stack.Children.Add(new TextBlock { Text = strings.Get(trait.DescriptionKey), Classes = { "hint" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 6, 2) });
            var note = whyNot ?? (status.Availability == Core.Traits.TraitAvailability.Depends ? $"This trait {status.Reason}." : null);
            if (note != null)
                stack.Children.Add(new TextBlock { Text = note, Classes = { "hint", "warning" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 0, 6, 3) });
            AddWide(stack);
        }
    }

    /// <summary>One applied marking in a layer's list: picture, name with its colours below, order buttons, remove.</summary>
    private Control MarkingLine(string organ, string layer, int index, MarkingEntry entry, int count)
    {
        var row = new DockPanel { Margin = new Thickness(6, 2, 4, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(ActionButton("↑", index > 0, () => Apply(s => s.MoveMarking(organ, layer, index, -1)), "Move up (drawn above the next)"));
        buttons.Children.Add(ActionButton("↓", index < count - 1, () => Apply(s => s.MoveMarking(organ, layer, index, 1)), "Move down"));
        buttons.Children.Add(ActionButton("×", true, () => Apply(s => s.RemoveMarking(organ, layer, index)), "Remove"));
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(4, 1, 4, 0) };
        var marking = _session!.Content!.Characters.Markings.GetValueOrDefault(entry.Id);
        for (var c = 0; c < entry.Colors.Count; c++)
        {
            if (marking?.IsColorLinked(c) == true)
                continue;
            var colorIndex = c;
            colors.Children.Add(ColorField(entry.Colors[c], color => Apply(s => s.SetMarkingColor(organ, layer, index, colorIndex, color)), compact: true));
        }

        var picture = new Border { Width = 32, Height = 32, Margin = new Thickness(0, 0, 4, 0), Child = MarkingPicture(organ, layer, entry.Id, 32) is { } image ? new Image { Source = image } : null };
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);

        var name = new Button { Classes = { "crumb" }, Content = _session!.MarkingName(entry.Id), HorizontalAlignment = HorizontalAlignment.Left };
        var target = new Node(NodeKind.Marking, organ, layer, index);
        name.Click += (_, _) => Select(target);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(name);
        if (colors.Children.Count > 0)
            details.Children.Add(colors);
        row.Children.Add(details);
        return row;
    }

    private static Button ActionButton(string text, bool enabled, Action click, string? tip = null)
    {
        var button = new Button { Classes = { "small" }, Content = text, IsEnabled = enabled };
        if (tip != null)
            ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private void AddCategory(string title)
    {
        var row = NextRow();
        var cell = new Border { Classes = { "category" }, Child = new TextBlock { Text = title } };
        Grid.SetRow(cell, row);
        Grid.SetColumnSpan(cell, 2);
        Inspector.Children.Add(cell);
    }

    private void AddRow(string label, Control editor, string? note = null)
    {
        var row = NextRow();
        var labelCell = new Border { Classes = { "label" }, Child = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
        Control value = editor;
        if (note != null)
        {
            var stack = new StackPanel();
            stack.Children.Add(editor);
            stack.Children.Add(new TextBlock { Text = note, Classes = { "hint" }, Margin = new Thickness(4, 0, 4, 2), TextWrapping = TextWrapping.Wrap });
            value = stack;
        }
        var valueCell = new Border { Classes = { "value" }, Child = value };
        Grid.SetRow(labelCell, row);
        Grid.SetRow(valueCell, row);
        Grid.SetColumn(valueCell, 1);
        Inspector.Children.Add(labelCell);
        Inspector.Children.Add(valueCell);
    }

    private void AddWide(Control control)
    {
        var row = NextRow();
        var cell = new Border { Classes = { "value" }, Child = control };
        Grid.SetRow(cell, row);
        Grid.SetColumnSpan(cell, 2);
        Inspector.Children.Add(cell);
    }

    private int NextRow()
    {
        Inspector.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        return Inspector.RowDefinitions.Count - 1;
    }

    private static TextBlock Text(string text) =>
        new() { Text = text, Margin = new Thickness(4, 2), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private static SelectableTextBlock Mono(string text) =>
        new() { Text = text, Classes = { "mono" }, Margin = new Thickness(4, 3), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    /// <summary>A colour swatch with its hex value; Enter or leaving the field applies it.</summary>
    private Control ColorField(Rgba color, Action<Rgba> changed, bool compact = false)
    {
        var text = new TextBox
        {
            Text = color.ToHex()[..7],
            Classes = { "mono" },
            Width = compact ? 64 : 84,
            MinHeight = compact ? 18 : 21,
            Padding = new Thickness(3, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        CommitOnEnterOrLeave(text, value =>
        {
            if (Rgba.TryParse(value.Trim(), out var parsed))
                changed(parsed);
        });
        // While dragging in the picker only the preview follows; the inspector is rebuilt when it closes.
        var swatch = Controls.ColorPicker.Swatch(color, (picked, live) =>
        {
            text.Text = picked.ToHex()[..7];
            if (live)
                _liveColor?.Invoke(picked, changed);
            else
                changed(picked);
        });
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        panel.Children.Add(swatch);
        panel.Children.Add(text);
        return panel;
    }

    /// <summary>Applies a colour while a picker is open, without rebuilding the inspector under it.</summary>
    private static Action<Rgba, Action<Rgba>>? _liveColor;

    private static Border Swatch(Rgba color) => new()
    {
        Width = 14,
        Height = 14,
        Margin = new Thickness(3, 0),
        VerticalAlignment = VerticalAlignment.Center,
        BorderBrush = new SolidColorBrush(Color.Parse("#8D97A3")),
        BorderThickness = new Thickness(1),
        Background = Brush(color),
    };

    // The text box being typed in, and how to commit what it holds. Typing is committed on Enter
    // or on leaving the box; what saves, replaces or reads the whole character commits it first.
    private Action? _pendingTyping;

    private void CommitOnEnterOrLeave(TextBox box, Action<string> commit)
    {
        var original = box.Text ?? "";
        void Commit()
        {
            if ((box.Text ?? "") != original)
            {
                original = box.Text ?? "";
                commit(original);
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
        };
        box.GotFocus += (_, _) => _pendingTyping = Commit;
        box.LostFocus += (_, _) =>
        {
            if (_pendingTyping == Commit)
                _pendingTyping = null;
            Commit();
        };
    }

    /// <summary>Commits what is being typed, before the character is saved, replaced or read whole.</summary>
    private void CommitTyping()
    {
        var commit = _pendingTyping;
        _pendingTyping = null;
        commit?.Invoke();
    }

    // A slider in steps of 0.01 with its value beside it.
    private static (DockPanel Panel, TextBlock Value, Slider Slider) SizeSlider(float minimum, float maximum, float current)
    {
        var value = new TextBlock { Text = current.ToString("0.00"), Classes = { "mono" }, Width = 34, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = current,
            SmallChange = 0.01,
            LargeChange = 0.05,
            TickFrequency = 0.01,
            IsSnapToTickEnabled = true,
            MinHeight = 18,
        };
        var panel = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        panel.Children.Add(value);
        panel.Children.Add(slider);
        return (panel, value, slider);
    }

    // Goob's height and width: two sliders that pull each other within the species' size ratio,
    // with centimetres and weight as its lobby shows them.
    private void AddHeightAndWidth(Core.Editing.EditorSession session, SpeciesInfo species, Core.Profiles.CharacterFile file)
    {
        var height = Core.Profiles.CharacterSize.Height(file, session.Fork!, species);
        var width = Core.Profiles.CharacterSize.Width(file, session.Fork!, species);
        var (heightPanel, heightValue, heightSlider) = SizeSlider(species.MinHeight, species.MaxHeight, height);
        var (widthPanel, widthValue, widthSlider) = SizeSlider(species.MinWidth, species.MaxWidth, width);
        var heightNote = new TextBlock { Classes = { "hint" }, Margin = new Thickness(4, 0, 4, 2), TextWrapping = TextWrapping.Wrap };
        var widthNote = new TextBlock { Classes = { "hint" }, Margin = new Thickness(4, 0, 4, 2), TextWrapping = TextWrapping.Wrap };
        void Describe(float h, float w)
        {
            var (cm, shoulders) = Core.Profiles.CharacterSize.Centimetres(species, h, w);
            heightNote.Text = $"{cm} cm, {Core.Profiles.CharacterSize.Kilograms(species, h, w)} kg; {species.MinHeight:0.00} to {species.MaxHeight:0.00}";
            widthNote.Text = $"{shoulders} cm across the shoulders; {species.MinWidth:0.00} to {species.MaxWidth:0.00}";
        }
        Describe(height, width);

        // Moving one slider can pull the other, as in the lobby.
        var updating = false;
        void Moved(bool heightMoved)
        {
            if (updating)
                return;
            var (h, w) = Core.Profiles.CharacterSize.KeepRatio((float)Math.Round(heightSlider.Value, 2), (float)Math.Round(widthSlider.Value, 2), species, heightMoved);
            updating = true;
            heightSlider.Value = h;
            widthSlider.Value = w;
            updating = false;
            heightValue.Text = h.ToString("0.00");
            widthValue.Text = w.ToString("0.00");
            Describe(h, w);
            Apply(s => heightMoved ? s.SetHeight(h) : s.SetWidth(w), keepInspector: true);
        }
        heightSlider.ValueChanged += (_, _) => Moved(heightMoved: true);
        widthSlider.ValueChanged += (_, _) => Moved(heightMoved: false);

        var heightStack = new StackPanel();
        heightStack.Children.Add(heightPanel);
        heightStack.Children.Add(heightNote);
        AddRow("Height", heightStack);
        var widthStack = new StackPanel();
        widthStack.Children.Add(widthPanel);
        widthStack.Children.Add(widthNote);
        AddRow("Width", widthStack);
        var reset = new Button { Classes = { "small" }, Content = "Default size", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(reset, $"{species.DefaultHeight:0.00} high, {species.DefaultWidth:0.00} wide, as the lobby's reset buttons give");
        reset.Click += (_, _) => Apply(s =>
        {
            s.SetHeight(species.DefaultHeight);
            return s.SetWidth(species.DefaultWidth);
        });
        AddRow("", reset);
    }
}
