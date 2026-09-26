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

        if (Core.Profiles.CharacterSize.HasHeight(session.Fork!))
        {
            var current = Core.Profiles.CharacterSize.CheckHeight(Core.Profiles.CharacterSize.ReadHeight(file) ?? 1f, species);
            var value = new TextBlock { Text = current.ToString("0.00"), Classes = { "mono" }, Width = 34, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
            var slider = new Slider
            {
                Minimum = species.MinHeight,
                Maximum = species.MaxHeight,
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

        var overflow = session.Content!.Outfits.Jobs.TryGetValue(Core.Outfits.OutfitCatalog.FallbackJob, out var overflowJob)
            ? session.Content.Strings[overflowJob.NameKey] ?? overflowJob.Id
            : Core.Outfits.OutfitCatalog.FallbackJob;
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
        description.LostFocus += (_, _) =>
        {
            if (description.Text != (session.File?.FlavorText ?? ""))
                Apply(s => s.Edit(f => f.FlavorText = description.Text ?? ""));
        };
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

        AddCategory("Marking layers");
        foreach (var layer in organ.MarkingLayers)
        {
            var applied = Applied(session.Look!, organ.Category, layer).Count;
            var limit = session.LayerLimit(organ, layer);
            var available = session.AvailableMarkings(organ, layer).Count;
            var open = new Button { Classes = { "crumb" }, Content = Words(layer), HorizontalAlignment = HorizontalAlignment.Left };
            var target = new Node(NodeKind.Layer, organ.Category, layer);
            open.Click += (_, _) => Select(target);
            AddRow("", open, $"{applied} of {(limit?.ToString() ?? "any")}, {available} to choose from");
        }
    }

    private void InspectLayer(OrganInfo organ, string layer)
    {
        var session = _session!;
        var catalog = session.Content!.Characters;
        InspectorTitle.Text = $"Layer: {Words(organ.Category)} › {Words(layer)}";
        var group = organ.MarkingGroup != null && catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var g) ? g : null;
        var limit = group != null && group.Limits.TryGetValue(layer, out var l) ? l : null;
        var applied = Applied(session.Look!, organ.Category, layer);

        AddCategory("Layer");
        AddRow("Layer", Mono(layer));
        AddRow("Limit", Text(limit == null ? "No limit" : $"{limit.Limit}" + (limit.Required ? ", required" : "")));
        if (limit is { Default.Count: > 0 })
            AddRow("Default", Mono(string.Join("\n", limit.Default)));
        AddRow("To choose from", Text($"{session.AvailableMarkings(organ, layer).Count} markings"));

        AddCategory(applied.Count == 0 ? "Markings" : "Markings (first is drawn on top)");
        for (var i = 0; i < applied.Count; i++)
            AddWide(MarkingLine(organ.Category, layer, i, applied[i], applied.Count));

        var add = new Button { Classes = { "small" }, Content = "Add a marking...", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 4) };
        // A one-marking layer stays open: picking another swaps it.
        add.IsEnabled = limit == null || limit.Limit == 1 || applied.Count < limit.Limit;
        if (limit is { Limit: 1 } && applied.Count == 1)
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
        AddRow("Name", Text(session.MarkingName(entry.Id)));
        AddRow("Id", Mono(entry.Id));
        AddRow("Layer", Mono(marking.Layer));
        if (marking.SexRestriction != null)
            AddRow("Only for", Text(marking.SexRestriction));

        AddCategory("Colours");
        for (var c = 0; c < entry.Colors.Count; c++)
        {
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
        AddRow("Shown as", jobs, "The lobby shows the High priority job; pick another to try its clothes.");
        if (job == null)
            return;

        var priority = new ComboBox { ItemsSource = Priorities, SelectedItem = session.JobPriority(job), HorizontalAlignment = HorizontalAlignment.Stretch };
        priority.SelectionChanged += (_, _) =>
        {
            if (priority.SelectedItem is string value && value != session.JobPriority(job))
                Apply(s => s.SetJobPriority(job, value));
        };
        AddRow("Priority", priority, "Setting High moves another High job to Medium.");

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

        AddCategory(group.MaxLimit == 1 ? "Choose one" : $"Choose up to {group.MaxLimit}");
        foreach (var loadoutId in group.Loadouts)
        {
            if (!outfits.Loadouts.TryGetValue(loadoutId, out var loadout))
                continue;
            var check = outfits.Check(loadout, session.Look!.Species);
            var name = session.LoadoutName(loadoutId);
            Control choice = group.MaxLimit == 1
                ? new RadioButton { Content = name, IsChecked = chosen.Contains(loadoutId), GroupName = "loadout-" + groupId }
                : new CheckBox { Content = name, IsChecked = chosen.Contains(loadoutId) };
            choice.IsEnabled = check != Core.Outfits.LoadoutCheck.WrongSpecies;
            choice.Margin = new Thickness(6, 1);
            ToolTip.SetTip(choice, loadoutId);
            ((Avalonia.Controls.Primitives.ToggleButton)choice).IsCheckedChanged += (_, _) =>
            {
                var isChecked = ((Avalonia.Controls.Primitives.ToggleButton)choice).IsChecked == true;
                if (!_refreshing && isChecked != chosen.Contains(loadoutId))
                    Apply(s => s.ToggleLoadout(job, groupId, loadoutId));
            };
            var note = check switch
            {
                Core.Outfits.LoadoutCheck.WrongSpecies => $"Not for {session.DisplayName(session.Content.Characters.Species[session.Look!.Species])}",
                Core.Outfits.LoadoutCheck.ServerChecks => "Needs playtime; the server checks",
                _ => null,
            };
            if (note == null)
                AddWide(choice);
            else
            {
                var stack = new StackPanel();
                stack.Children.Add(choice);
                stack.Children.Add(new TextBlock { Text = note, Classes = { "hint", check == Core.Outfits.LoadoutCheck.ServerChecks ? "warning" : "muted" }, Margin = new Thickness(28, 0, 4, 2) });
                AddWide(stack);
            }
        }
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

    /// <summary>Antagonists the character is willing to be, as the lobby's Antags tab lists them.</summary>
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

    /// <summary>One applied marking in a layer's list: order buttons, name, colours, remove.</summary>
    private Control MarkingLine(string organ, string layer, int index, MarkingEntry entry, int count)
    {
        var row = new DockPanel { Margin = new Thickness(6, 2, 4, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        buttons.Children.Add(ActionButton("↑", index > 0, () => Apply(s => s.MoveMarking(organ, layer, index, -1)), "Move up (drawn above the next)"));
        buttons.Children.Add(ActionButton("↓", index < count - 1, () => Apply(s => s.MoveMarking(organ, layer, index, 1)), "Move down"));
        buttons.Children.Add(ActionButton("×", true, () => Apply(s => s.RemoveMarking(organ, layer, index)), "Remove"));
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(4, 0) };
        for (var c = 0; c < entry.Colors.Count; c++)
        {
            var colorIndex = c;
            colors.Children.Add(ColorField(entry.Colors[c], color => Apply(s => s.SetMarkingColor(organ, layer, index, colorIndex, color)), compact: true));
        }
        DockPanel.SetDock(colors, Dock.Right);
        row.Children.Add(colors);

        var name = new Button { Classes = { "crumb" }, Content = _session!.MarkingName(entry.Id), HorizontalAlignment = HorizontalAlignment.Left };
        var target = new Node(NodeKind.Marking, organ, layer, index);
        name.Click += (_, _) => Select(target);
        row.Children.Add(name);
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
    private static Control ColorField(Rgba color, Action<Rgba> changed, bool compact = false)
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

    private static void CommitOnEnterOrLeave(TextBox box, Action<string> commit)
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
        box.LostFocus += (_, _) => Commit();
    }
}
