using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;

namespace Paperdoll.App;

/// <summary>
/// Explorer on the left, preview in the middle, inspector for the selected part on the right,
/// tables below. Changes go through <see cref="EditorSession"/> so the game's rules apply, then
/// the window refreshes.
/// </summary>
public partial class MainWindow : Window
{
    private EditorSession? _session;
    private bool _refreshing;

    /// <summary>Finishes when the window has opened its store and loaded a fork (if any).</summary>
    public Task Startup { get; private set; } = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        SetUpPreview();
        SetUpTables();
        SetUpFiles();
        SetUpExplorerDrag();
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            RefreshAll(keepInspector: true);
        };
        _liveColor = (color, apply) =>
        {
            _keepInspector = true;
            try
            {
                apply(color);
            }
            finally
            {
                _keepInspector = false;
            }
        };
        Opened += (_, _) =>
        {
            if (_session == null)
                Startup = StartAsync();
        };
    }

    /// <summary>A window on a session that already has a fork loaded (used for screenshots).</summary>
    public MainWindow(EditorSession session) : this()
    {
        _session = session;
        if (session.Fork != null)
        {
            _refreshing = true;
            ForkBox.ItemsSource = new[] { session.Fork };
            ForkBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ForkInfo.Name));
            ForkBox.SelectedIndex = 0;
            _refreshing = false;
            OnForkLoaded();
        }
    }

    private async Task StartAsync()
    {
        try
        {
            LoadSettings();
            _restorePending = _settings.Autosave && File.Exists(AutosavePath);
            SetStatus("Opening the fork store");
            _session = await Task.Run(() => EditorSession.OpenAsync(Environment.GetEnvironmentVariable("PAPERDOLL_STORE")));
            var statuses = await _session.ForkStatusesAsync();
            await RefreshForkBoxAsync();

            // PAPERDOLL_START_FORK names a fork to open at start, downloading it if needed.
            var start = Environment.GetEnvironmentVariable("PAPERDOLL_START_FORK") is { } id ? KnownForks.Find(id) : null;
            // Otherwise the fork the autosaved character was made for, if it is downloaded.
            if (start == null && _settings.Autosave && AutosaveForkId() is { } savedFork && KnownForks.FindByServerForkId(savedFork) is { } forFile
                && statuses.Any(s => s.Downloaded && s.Editable && s.Fork.Id == forFile.Id))
                start = forFile;
            start ??= statuses.FirstOrDefault(s => s.Downloaded && s.Editable && s.Fork.Id == "deltav")?.Fork
                ?? statuses.FirstOrDefault(s => s.Downloaded && s.Editable)?.Fork;

            // The working copy is reopened on the first fork that loads.
            if (start != null)
                await LoadForkAsync(start, update: false);
            else
            {
                SetStatus("No fork downloaded yet. Choose Fork > Forks... to download one.");
                await ShowForksAsync();
            }
        }
        catch (Exception e)
        {
            SetStatus($"Could not start: {e.Message}");
        }
    }

    private async Task LoadForkAsync(ForkInfo fork, bool update)
    {
        if (_session == null)
            return;
        CommitTyping();
        IsEnabled = false;
        try
        {
            var progress = new Progress<string>(SetStatus);
            await Task.Run(() => _session.LoadForkAsync(fork, update, progress));
            await RefreshForkBoxAsync();
            OnForkLoaded();
            await RestoreAutosaveAsync();
        }
        catch (Exception e)
        {
            SetStatus($"Could not load {fork.Name}: {e.Message}");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    /// <summary>Rebuilds what depends on the fork, then the rest.</summary>
    private void OnForkLoaded()
    {
        var session = _session!;
        if (session.PreviewJob != null && !session.Content!.Outfits.Jobs.ContainsKey(session.PreviewJob))
            session.PreviewJob = null;
        BuildJobList();
        BuildPortraits();
        ClearLoadoutPictures();
        _selected = new Node(NodeKind.Character);
        RefreshAll();
        var changes = session.LastFixes.Count;
        SetStatus(changes == 0
            ? $"{session.Fork!.Name} loaded. Work through {session.File?.Name ?? "the character"} on the left, or pick a species in the Species table."
            : session.ChangedBySwitch
                ? $"{session.Fork!.Name} loaded. {session.File?.Name} was fitted to it ({changes} change{(changes == 1 ? "" : "s")} in Messages); switching back restores it, editing or saving here keeps the changes."
                : $"{session.Fork!.Name} loaded. {session.File?.Name} was checked against it; {changes} change{(changes == 1 ? "" : "s")} listed in Messages.");
    }

    private async Task RefreshForkBoxAsync()
    {
        if (_session == null)
            return;
        var downloaded = (await _session.ForkStatusesAsync()).Where(s => s.Downloaded && s.Editable).Select(s => s.Fork).ToList();
        _refreshing = true;
        ForkBox.ItemsSource = downloaded;
        ForkBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ForkInfo.Name));
        ForkBox.SelectedItem = downloaded.FirstOrDefault(f => f.Id == _session.Fork?.Id);
        _refreshing = false;
    }

    /// <summary>
    /// Redraws every pane from the session. Skips the inspector while a control in it is being
    /// dragged, so only the preview follows until dragging stops.
    /// </summary>
    private void RefreshAll(bool keepInspector = false, bool previewOnly = false)
    {
        if (_session?.Content == null || _session.Look == null)
            return;
        _refreshing = true;
        try
        {
            if (previewOnly)
            {
                RefreshPreview();
                _settle.Stop();
                _settle.Start();
                return;
            }
            _settle.Stop();
            BuildExplorer();
            if (!keepInspector)
                BuildInspector();
            BuildBreadcrumbs();
            RefreshPreview();
            RefreshTables();

            UpdateTitle();
            var matched = _settings.MatchedServers.TryGetValue(_session.Fork!.Id, out var server) && server.Commit == _session.Content.Commit
                ? $", as on {server.Name}" : "";
            StatusFork.Text = $"{_session.Fork!.Name} {_session.Content.Commit[..8]}{matched} via {_session.StoreKind}";
            StatusCounts.Text = $"{_session.Selectable().Count} species, {_session.Content.Characters.Markings.Count} markings";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private bool _keepInspector;

    // Fires once dragging has paused, to refresh what a live change skipped.
    private readonly Avalonia.Threading.DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>Runs an edit through the session and shows the result, or the error.</summary>
    private void Apply(Func<EditorSession, IReadOnlyList<RuleFix>> edit, bool keepInspector = false)
    {
        if (_session?.File == null || _refreshing)
            return;
        try
        {
            var fixes = edit(_session);
            MarkChanged();
            var live = keepInspector || _keepInspector;
            RefreshAll(live, previewOnly: live);
            if (fixes.Count > 0)
                SetStatus(fixes[^1].Message);
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async void OnForkChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ForkBox.SelectedItem is not ForkInfo fork || fork.Id == _session?.Fork?.Id)
            return;
        await LoadForkAsync(fork, update: false);
    }

    private async void OnMatchServer(object? sender, RoutedEventArgs e)
    {
        CommitTyping();
        if (_session == null || await new ServersWindow().ShowDialog<ServerChoice?>(this) is not { } choice)
            return;
        IsEnabled = false;
        try
        {
            var progress = new Progress<string>(SetStatus);
            var (fork, commit) = await Task.Run(() => _session.MatchServerAsync(choice.Address, progress));
            _settings = _settings with { MatchedServers = new(_settings.MatchedServers) { [fork.Id] = new MatchedServer(choice.Name, commit) } };
            SaveSettings();
            await RefreshForkBoxAsync();
            OnForkLoaded();
            await RestoreAutosaveAsync();
            SetStatus($"{fork.Name} now matches {choice.Name} ({commit[..8]}). Fork > Update goes back to the newest version.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not match {choice.Name}: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void OnUpdateFork(object? sender, RoutedEventArgs e)
    {
        if (_session?.Fork is { } fork)
            await LoadForkAsync(fork, update: true);
    }

    private async void OnForks(object? sender, RoutedEventArgs e) => await ShowForksAsync();

    private async Task ShowForksAsync()
    {
        if (_session == null)
            return;
        var choice = await new ForksWindow(_session, ShowFilesAsync).ShowDialog<ForkChoice?>(this);
        await RefreshForkBoxAsync();
        if (choice != null)
            await LoadForkAsync(choice.Fork, choice.Update);
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (_session?.Look == null || !await MayReplaceCharacterAsync("starting a new one"))
            return;
        _selected = new Node(NodeKind.Character);
        ForgetWorkingFile();
        KeepPreviousWorkingCopy();
        Apply(s =>
        {
            s.NewCharacter(s.Look!.Species);
            return s.LastFixes;
        });
        // A new character has nothing to lose until it is edited.
        _dirty = false;
        UpdateTitle();
        SetStatus("New character. Give it a name in the inspector.");
    }

    private void OnRandom(object? sender, RoutedEventArgs e)
    {
        CommitTyping();
        if (_session?.File == null)
            return;
        var parts = _settings.RandomParts;
        if (parts == Core.Characters.RandomParts.None)
        {
            SetStatus("Nothing to randomise: choose what Random changes with the arrow next to it.");
            return;
        }
        _selected = new Node(NodeKind.Character);
        Apply(s => s.Randomize(parts, strength: Math.Clamp(_settings.RandomStrength, 0, 100) / 100f));
        SetStatus($"Randomised {_session.File.Name}. Jobs, loadouts, traits and records were kept.");
    }

    // The check boxes under Random's arrow: what it changes, kept between runs.
    private void BuildRandomParts()
    {
        RandomPartsPanel.Children.Clear();
        RandomPartsPanel.Children.Add(new TextBlock { Text = "Random changes:", Classes = { "hint" }, Margin = new Avalonia.Thickness(2, 0, 2, 2) });
        (Core.Characters.RandomParts Part, string Label)[] parts =
        [
            (Core.Characters.RandomParts.Species, "Species"),
            (Core.Characters.RandomParts.Sex, "Sex"),
            (Core.Characters.RandomParts.Pronouns, "Pronouns"),
            (Core.Characters.RandomParts.Name, "Name"),
            (Core.Characters.RandomParts.Age, "Age"),
            (Core.Characters.RandomParts.Skin, "Skin colour"),
            (Core.Characters.RandomParts.Eyes, "Eye colour"),
            (Core.Characters.RandomParts.Markings, "Markings, hair included"),
            (Core.Characters.RandomParts.Size, "Height and width (Goob)"),
        ];
        foreach (var (part, label) in parts)
        {
            var box = new CheckBox { Content = label, IsChecked = _settings.RandomParts.HasFlag(part) };
            box.IsCheckedChanged += (_, _) =>
            {
                _settings = _settings with { RandomParts = box.IsChecked == true ? _settings.RandomParts | part : _settings.RandomParts & ~part };
                SaveSettings();
            };
            RandomPartsPanel.Children.Add(box);
        }

        // How many markings Random adds, as a share of what upstream's lobby randomiser would.
        var value = new TextBlock { Classes = { "mono" }, Width = 34, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(4, 0, 0, 0) };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_settings.RandomStrength, 0, 100),
            SmallChange = 5,
            LargeChange = 10,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            MinHeight = 18,
            Width = 140,
        };
        value.Text = $"{slider.Value:0}%";
        slider.ValueChanged += (_, e) =>
        {
            value.Text = $"{e.NewValue:0}%";
            _settings = _settings with { RandomStrength = (int)Math.Round(e.NewValue) };
            SaveSettings();
        };
        var row = new DockPanel { Margin = new Avalonia.Thickness(2, 6, 2, 0) };
        var caption = new TextBlock { Text = "Markings:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(caption);
        row.Children.Add(value);
        row.Children.Add(slider);
        ToolTip.SetTip(row, "How busy random markings get. At 100% every place a layer takes is rolled, as upstream's lobby does; on Euphoria that is dozens of markings. Delta-V's and Euphoria's own lobbies add none. Hair is rolled either way.");
        RandomPartsPanel.Children.Add(row);
        RandomPartsPanel.Children.Add(new TextBlock { Text = "100% is upstream's lobby randomiser.", Classes = { "hint" }, Margin = new Avalonia.Thickness(2, 0, 2, 0) });
    }

    private void OnShowTab(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var index))
            BottomTabs.SelectedIndex = index;
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnAbout(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);
}

public sealed record CreditRow(string Rsi, string LicenseText, string Copyright)
{
    public CreditRow(CreditLine line)
        : this(line.Rsi, (line.License ?? "unknown") + (line.NonCommercial ? " (non-commercial)" : ""), line.Copyright ?? "")
    {
    }
}

public sealed record SpeciesRow(string Id, string Name, Bitmap? Portrait, string Sexes, string Ages, string SkinRule, int Layers, string Size, string Source);

public sealed record TraitRow(string Id, string Name, string Category, int Cost, string Picked, string Status, string Source);

public sealed record GearRow(string Where, string Item, string From, string Id, string? Group);

public sealed record JobRow(string Id, string Name, string Department, string Priority, int Groups);

public sealed record MarkingRow(string Id, string Name, string Layer, int Sprites, string Coloring, string Restriction,
    string License, string Source, string OrganCategory, string LayerKey)
{
    /// <summary>Draws the row's picture when the table first shows the row.</summary>
    public Lazy<Avalonia.Media.Imaging.Bitmap?>? PictureSource { get; init; }

    public Avalonia.Media.Imaging.Bitmap? Picture => PictureSource?.Value;
}
