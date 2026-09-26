using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;

namespace Paperdoll.App;

/// <summary>
/// The editor window: an explorer of the character on the left, the preview in the middle, an
/// inspector for the selected part on the right, and tables below. Everything shown comes from an
/// <see cref="EditorSession"/>; every change goes back through it (so the game's rules apply),
/// then the window is refreshed.
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
            SetStatus("Opening the fork store");
            _session = await Task.Run(() => EditorSession.OpenAsync(Environment.GetEnvironmentVariable("PAPERDOLL_STORE")));
            var statuses = await _session.ForkStatusesAsync();
            await RefreshForkBoxAsync();

            // PAPERDOLL_START_FORK names a fork to open at start, downloading it if needed.
            var start = Environment.GetEnvironmentVariable("PAPERDOLL_START_FORK") is { } id ? KnownForks.Find(id) : null;
            start ??= statuses.FirstOrDefault(s => s.Downloaded && s.Editable && s.Fork.Id == "deltav")?.Fork
                ?? statuses.FirstOrDefault(s => s.Downloaded && s.Editable)?.Fork;

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
        IsEnabled = false;
        try
        {
            var progress = new Progress<string>(SetStatus);
            await Task.Run(() => _session.LoadForkAsync(fork, update, progress));
            await RefreshForkBoxAsync();
            OnForkLoaded();
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

    /// <summary>A fork's data changed: rebuild what depends on the fork, then the rest.</summary>
    private void OnForkLoaded()
    {
        BuildPortraits();
        _selected = new Node(NodeKind.Character);
        RefreshAll();
        SetStatus($"{_session!.Fork!.Name} loaded. Pick a species in the Species table below, then work through the character on the left.");
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

    /// <summary>Redraws every pane from the session. The inspector is kept while a control in it is being dragged.</summary>
    private void RefreshAll(bool keepInspector = false)
    {
        if (_session?.Content == null || _session.Look == null)
            return;
        _refreshing = true;
        try
        {
            BuildExplorer();
            if (!keepInspector)
                BuildInspector();
            BuildBreadcrumbs();
            RefreshPreview();
            RefreshTables();

            StatusFork.Text = $"{_session.Fork!.Name} {_session.Content.Commit[..8]} via {_session.StoreKind}";
            StatusCounts.Text = $"{_session.Selectable().Count} species, {_session.Content.Characters.Markings.Count} markings";
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Runs an edit through the session and shows the result, or the error.</summary>
    private void Apply(Func<EditorSession, IReadOnlyList<RuleFix>> edit, bool keepInspector = false)
    {
        if (_session?.File == null || _refreshing)
            return;
        try
        {
            var fixes = edit(_session);
            RefreshAll(keepInspector);
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
        var choice = await new ForksWindow(_session).ShowDialog<ForkChoice?>(this);
        await RefreshForkBoxAsync();
        if (choice != null)
            await LoadForkAsync(choice.Fork, choice.Update);
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        if (_session?.Look == null)
            return;
        _selected = new Node(NodeKind.Character);
        Apply(s =>
        {
            s.NewCharacter(s.Look!.Species);
            return s.LastFixes;
        });
        SetStatus("New character. Give it a name in the inspector.");
    }

    private void OnShowTab(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var index))
            BottomTabs.SelectedIndex = index;
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnAbout(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);
}

/// <summary>A credits table row.</summary>
public sealed record CreditRow(string Rsi, string LicenseText, string Copyright)
{
    public CreditRow(CreditLine line)
        : this(line.Rsi, (line.License ?? "unknown") + (line.NonCommercial ? " (non-commercial)" : ""), line.Copyright ?? "")
    {
    }
}

/// <summary>A species table row.</summary>
public sealed record SpeciesRow(string Id, string Name, Bitmap? Portrait, string Sexes, string Ages, string SkinRule, int Layers, string Size, string Source);

/// <summary>A markings table row, with where the marking would go.</summary>
public sealed record MarkingRow(string Id, string Name, string Layer, int Sprites, string Coloring, string Restriction,
    string License, string Source, string OrganCategory, string LayerKey);
