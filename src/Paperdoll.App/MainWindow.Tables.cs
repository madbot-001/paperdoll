using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperdoll.Core.Characters;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.App;

public partial class MainWindow
{
    private List<SpeciesRow> _speciesRows = [];
    private List<MarkingRow> _markingRows = [];
    private List<JobRow> _jobRows = [];

    private void SetUpTables()
    {
        SpeciesFilter.TextChanged += (_, _) => FilterSpecies();
        MarkingFilter.TextChanged += (_, _) => FilterMarkings();
        JobFilter.TextChanged += (_, _) => FilterJobs();
    }

    private void BuildJobTable()
    {
        var session = _session!;
        var outfits = session.Content!.Outfits;
        _jobRows = outfits.SelectableJobs().Select(job =>
        {
            var department = outfits.Departments.FirstOrDefault(d => d.Roles.Contains(job.Id));
            var groups = outfits.RoleLoadouts.TryGetValue(Core.Outfits.OutfitCatalog.RoleFor(job.Id), out var g) ? g.Count : 0;
            return new JobRow(job.Id, session.Content.Strings.Get(job.NameKey),
                department != null ? session.Content.Strings.Get(department.NameKey) : "", session.JobPriority(job.Id), groups);
        })
        .OrderBy(r => r.Priority switch { "High" => 0, "Medium" => 1, "Low" => 2, _ => 3 })
        .ThenBy(r => r.Department, StringComparer.CurrentCulture)
        .ThenBy(r => r.Name, StringComparer.CurrentCulture)
        .ToList();
        FilterJobs();
    }

    private void FilterJobs()
    {
        var filter = JobFilter.Text?.Trim() ?? "";
        var selected = (JobGrid.SelectedItem as JobRow)?.Id;
        var rows = _jobRows.Where(r => filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Department.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        JobGrid.ItemsSource = rows;
        JobGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
    }

    private void OnSetPriority(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string priority } && JobGrid.SelectedItem is JobRow row)
        {
            Apply(s => s.SetJobPriority(row.Id, priority));
            SetStatus($"{row.Name}: {priority}.");
        }
    }

    private void OnPreviewJob(object? sender, RoutedEventArgs e) => PreviewSelectedJob();

    private void OnJobDoubleTapped(object? sender, TappedEventArgs e) => PreviewSelectedJob();

    private void PreviewSelectedJob()
    {
        if (JobGrid.SelectedItem is not JobRow row || _session == null)
            return;
        _session.PreviewJob = row.Id;
        _refreshing = true;
        SelectDressedJob();
        _refreshing = false;
        _selected = new Node(NodeKind.Outfit);
        RefreshAll();
    }

    private void RefreshTables()
    {
        var session = _session!;
        BuildSpeciesTable();
        RefreshMarkingTable();
        BuildJobTable();
        CreditsGrid.ItemsSource = session.Credits().Select(c => new CreditRow(c)).ToList();

        var fixes = session.LastFixes;
        MessagesTab.Header = fixes.Count == 0 ? "Messages" : $"Messages ({fixes.Count})";
        MessagesList.ItemsSource = fixes.Count == 0
            ? ["The character passes the game's checks as it is."]
            : fixes.Select(f => $"{f.Field}: {f.Message}").ToList();

        SourceText.Text = session.File!.ToYaml();
    }

    private void BuildSpeciesTable()
    {
        var session = _session!;
        _speciesRows = session.Selectable().Select(s => new SpeciesRow(
            s.Id,
            session.DisplayName(s),
            _portraits.GetValueOrDefault(s.Id),
            string.Join(", ", s.Sexes),
            $"{s.MinAge} to {s.MaxAge}",
            s.SkinColoration ?? "",
            s.Organs.Sum(o => o.MarkingLayers.Count),
            Core.Profiles.CharacterSize.HasHeight(session.Fork!) ? $"{s.BaseScale.Y:0.##}x" : "1x",
            session.SourceOf("species", s.Id) ?? "")).ToList();
        FilterSpecies();
    }

    private void FilterSpecies()
    {
        var filter = SpeciesFilter.Text?.Trim() ?? "";
        var rows = _speciesRows.Where(r => filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var wasRefreshing = _refreshing;
        _refreshing = true;
        SpeciesGrid.ItemsSource = rows;
        SpeciesGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == _session?.Look?.Species);
        _refreshing = wasRefreshing;
    }

    private void OnUseSpecies(object? sender, RoutedEventArgs e) => UseSelectedSpecies();

    private void OnSpeciesDoubleTapped(object? sender, TappedEventArgs e) => UseSelectedSpecies();

    private void UseSelectedSpecies()
    {
        if (SpeciesGrid.SelectedItem is not SpeciesRow row || row.Id == _session?.Look?.Species)
            return;
        _selected = new Node(NodeKind.Character);
        Apply(s => s.ChangeSpecies(row.Id));
        SetStatus($"Species changed to {row.Name}. Markings it cannot have were removed; see Messages.");
    }

    /// <summary>
    /// The markings that can be added: to the selected layer, or to every layer of the species when
    /// the character or an organ is selected.
    /// </summary>
    private void RefreshMarkingTable()
    {
        var session = _session!;
        var species = session.Content!.Characters.Species[session.Look!.Species];
        var scope = species.Organs.Where(o => o.MarkingGroup != null)
            .SelectMany(o => o.MarkingLayers.Select(layer => (Organ: o, Layer: layer)))
            .Where(p => _selected.Layer == null
                ? _selected.Organ == null || p.Organ.Category == _selected.Organ
                : p.Organ.Category == _selected.Organ && p.Layer == _selected.Layer)
            .ToList();

        _markingRows = scope
            .SelectMany(p => session.AvailableMarkings(p.Organ, p.Layer).Select(m => Row(m, p.Organ, p.Layer)))
            .ToList();

        MarkingScope.Text = _selected.Layer != null
            ? $"For {Words(_selected.Organ!)} › {Words(_selected.Layer)}"
            : _selected.Organ != null
                ? $"For every layer of {Words(_selected.Organ)}"
                : $"For every layer of {session.DisplayName(species)}; pick a layer on the left to narrow";
        FilterMarkings();
    }

    private MarkingRow Row(MarkingInfo marking, OrganInfo organ, string layer)
    {
        var session = _session!;
        var license = marking.Sprites.Count > 0 ? session.Renderer!.Meta(marking.Sprites[0].Rsi)?.License ?? "" : "";
        return new MarkingRow(
            marking.Id,
            session.MarkingName(marking.Id),
            Words(layer),
            marking.Sprites.Count,
            DescribeColoring(marking),
            marking.SexRestriction ?? "",
            license,
            session.SourceOf("marking", marking.Id) ?? "",
            organ.Category,
            layer);
    }

    private void FilterMarkings()
    {
        var filter = MarkingFilter.Text?.Trim() ?? "";
        MarkingGrid.ItemsSource = _markingRows.Where(r => filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || r.Layer.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void OnAddMarking(object? sender, RoutedEventArgs e) => AddSelectedMarking();

    private void OnMarkingDoubleTapped(object? sender, TappedEventArgs e) => AddSelectedMarking();

    private void AddSelectedMarking()
    {
        if (MarkingGrid.SelectedItem is not MarkingRow row)
            return;
        var session = _session!;
        var organ = session.Content!.Characters.Species[session.Look!.Species].Organs.First(o => o.Category == row.OrganCategory);
        var count = Applied(session.Look, row.OrganCategory, row.LayerKey).Count;
        if (session.LayerLimit(organ, row.LayerKey) is { } limit && count >= limit)
        {
            SetStatus($"{Words(row.OrganCategory)} › {row.Layer} already holds {limit}; remove one first.");
            return;
        }
        _selected = new Node(NodeKind.Marking, row.OrganCategory, row.LayerKey, count);
        Apply(s => s.AddMarking(row.OrganCategory, row.LayerKey, row.Id));
        SetStatus($"Added {row.Name} to {Words(row.OrganCategory)} › {row.Layer}.");
    }

    // How a marking is coloured by default, from its coloring block.
    private static string DescribeColoring(MarkingInfo marking)
    {
        var type = marking.Node.Children.TryGetValue(new YamlScalarNode("coloring"), out var coloring)
            && coloring is YamlMappingNode map
            && map.Children.TryGetValue(new YamlScalarNode("default"), out var def) && def is YamlMappingNode defMap
            && defMap.Children.TryGetValue(new YamlScalarNode("type"), out var typeNode) && !typeNode.Tag.IsEmpty
                ? typeNode.Tag.Value
                : "";
        var text = type switch
        {
            "!type:SkinColoring" or "" => "Skin",
            "!type:EyeColoring" => "Eyes",
            "!type:TattooColoring" => "Darker skin",
            "!type:SimpleColoring" => "Fixed",
            "!type:CategoryColoring" => "Like hair",
            _ => type.Replace("!type:", ""),
        };
        return marking.ForcedColoring ? text + ", forced" : text;
    }
}
