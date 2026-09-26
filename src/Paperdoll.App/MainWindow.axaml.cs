using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.App;

/// <summary>
/// The editor window. Everything it shows comes from an <see cref="EditorSession"/>; every change
/// goes back through it, so the game's rules apply, then the window is refreshed.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] Directions = ["South", "North", "East", "West"];

    private EditorSession? _session;
    private bool _refreshing;
    private int _zoom = 6;
    private Direction _direction = Direction.South;

    /// <summary>Finishes when the window has opened its store and loaded a fork (if any).</summary>
    public Task Startup { get; private set; } = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        DirectionBox.ItemsSource = Directions;
        DirectionBox.SelectedIndex = 0;
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
        }
        RefreshAll();
        SetStatus($"{session.Fork?.Name} loaded. Pick a species on the left.");
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
            RefreshAll();
            SetStatus($"{fork.Name} loaded. Pick a species on the left.");
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

    /// <summary>Redraws every pane from the session; the property grid too unless asked not to.</summary>
    private void RefreshAll(bool keepProperties = false)
    {
        if (_session?.Content == null || _session.Look == null)
            return;
        _refreshing = true;
        try
        {
            var species = _session.Selectable();
            SpeciesList.ItemsSource = species.Select(_session.DisplayName).ToList();
            SpeciesList.SelectedIndex = species.ToList().FindIndex(s => s.Id == _session.Look.Species);

            if (!keepProperties)
                BuildProperties();
            BuildMarkings();
            RefreshPreview();

            CreditsGrid.ItemsSource = _session.Credits().Select(c => new CreditRow(c)).ToList();
            MessagesList.ItemsSource = _session.LastFixes.Count == 0
                ? ["The character passes the game's checks."]
                : _session.LastFixes.Select(f => f.Message).ToList();

            StatusFork.Text = $"{_session.Fork!.Name} @ {_session.Content.Commit[..8]} ({_session.StoreKind})";
            StatusCounts.Text = $"{species.Count} species, {_session.Content.Characters.Markings.Count} markings";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshPreview()
    {
        if (_session?.Look == null)
            return;
        using var image = _session.Render(_direction);
        using var scaled = image.Resize(new SKImageInfo(image.Width * _zoom, image.Height * _zoom), SKSamplingOptions.Default);
        using var data = scaled.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var old = PreviewImage.Source as IDisposable;
        PreviewImage.Source = new Bitmap(stream);
        old?.Dispose();
    }

    /// <summary>
    /// Runs an edit through the session and shows the result, or the error. While a control in
    /// the property grid is being dragged, pass <paramref name="keepProperties"/> so it is not
    /// rebuilt under the pointer.
    /// </summary>
    private void Apply(Func<EditorSession, IReadOnlyList<RuleFix>> edit, bool keepProperties = false)
    {
        if (_session?.File == null || _refreshing)
            return;
        try
        {
            edit(_session);
            RefreshAll(keepProperties);
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void OnSpeciesChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _session == null || SpeciesList.SelectedIndex < 0)
            return;
        var species = _session.Selectable()[SpeciesList.SelectedIndex];
        Apply(s => s.ChangeSpecies(species.Id));
    }

    private async void OnForkChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ForkBox.SelectedItem is not ForkInfo fork || fork.Id == _session?.Fork?.Id)
            return;
        await LoadForkAsync(fork, update: false);
    }

    private void OnDirectionChosen(object? sender, SelectionChangedEventArgs e)
    {
        _direction = (Direction)Math.Max(0, DirectionBox.SelectedIndex);
        if (!_refreshing)
            RefreshPreview();
    }

    private void OnZoom(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var zoom))
        {
            _zoom = zoom;
            RefreshPreview();
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
        var dialog = new ForksWindow(_session);
        var choice = await dialog.ShowDialog<ForkChoice?>(this);
        await RefreshForkBoxAsync();
        if (choice != null)
            await LoadForkAsync(choice.Fork, choice.Update);
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        if (_session?.Look == null)
            return;
        Apply(s =>
        {
            s.NewCharacter(s.Look!.Species);
            return s.LastFixes;
        });
        SetStatus("New character.");
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
