using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <param name="Source">A new source to read the fork from, given for the fork that is open and for a copy from the SS14 launcher, which is loaded from it.</param>
public sealed record ForkChoice(ForkInfo Fork, bool Update, ForkSource? Source = null);

/// <param name="Source">Where its files come from, as shown: "Download", "Folder" or "Launcher".</param>
/// <param name="Folder">Its game folder, when it is read from one.</param>
public sealed record ForkRow(ForkInfo Fork, string Name, string Model, string Editable, string Commit, string Repository, string LastPlayed,
    string Source = "Download", string? Folder = null)
{
    /// <summary>Whether it is read from Paperdoll's copy of an SS14 launcher build.</summary>
    public bool FromLauncher => Source == "Launcher";
}

/// <summary>Lists the known forks; opens, updates or removes them, and sets where each one's files come from.</summary>
/// <remarks>The launcher's content.db is read each time the list is filled, for Last played and the builds From launcher... offers.</remarks>
public partial class ForksWindow : Window
{
    private readonly EditorSession? _session;

    public ForksWindow()
    {
        InitializeComponent();
    }

    private readonly Func<Window, Task>? _showFiles;

    // Where the SS14 launcher may keep the forks it has run; none unless given.
    private readonly IReadOnlyList<string> _launcherFolders = [];

    // Called with the new choice when the 18+ box is ticked or cleared, to keep it.
    private readonly Action<bool>? _adultChanged;

    // Asks for a game folder; the system's folder picker unless given.
    private readonly Func<Window, Task<string?>> _pickFolder = GameFolderChoice.PickAsync;

    // Called when a fork's source has changed, to keep the sources.
    private readonly Action? _sourcesChanged;

    public ForksWindow(EditorSession session, Func<Window, Task>? showFiles = null, IReadOnlyList<string>? launcherFolders = null,
        bool showAdult = false, Action<bool>? adultChanged = null, Func<Window, Task<string?>>? pickFolder = null, Action? sourcesChanged = null) : this()
    {
        _session = session;
        _pickFolder = pickFolder ?? GameFolderChoice.PickAsync;
        _sourcesChanged = sourcesChanged;
        _showFiles = showFiles;
        _launcherFolders = launcherFolders ?? [];
        AdultBox.IsChecked = showAdult;
        _adultChanged = adultChanged;
        Closing += (_, e) => e.Cancel |= _removing;
        Opened += async (_, _) =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not list the forks: {ex.Message}";
            }
        };
    }

    private async Task RefreshAsync()
    {
        if (_session == null)
            return;
        var statuses = await _session.ForkStatusesAsync();
        // Read each time, away from the window's thread; it is only ever read, never kept.
        var (launcher, builds) = await Task.Run(() => (LauncherForks.Read(_launcherFolders), LauncherCache.Scan(_launcherFolders)));
        _builds = builds;
        var played = new Dictionary<string, DateTime>();
        var unknown = new List<string>();
        foreach (var used in launcher)
        {
            if (KnownForks.FindByServerForkId(used.ServerForkId) is not { } fork)
                unknown.Add(used.ServerForkId);
            else if (!played.TryGetValue(fork.Id, out var seen) || used.LastUsed > seen)
                played[fork.Id] = used.LastUsed;
        }
        // 18+ forks are listed only when asked for; one already downloaded, read from a folder or
        // open stays, so it can be removed.
        var showAdult = AdultBox.IsChecked == true;
        var rows = statuses
            .Select(s => (Status: s, _session.SourceOf(s.Fork.Id).Folder, _session.SourceOf(s.Fork.Id).Kind))
            .Where(s => showAdult || !s.Status.Fork.Adult || s.Status.Commit != null || s.Kind != ForkSourceKind.Download || s.Status.Fork.Id == _session.Fork?.Id)
            .OrderByDescending(s => played.GetValueOrDefault(s.Status.Fork.Id)).ThenByDescending(s => s.Status.Editable).ThenBy(s => s.Status.Fork.Name)
            .Select(s => new ForkRow(s.Status.Fork, s.Status.Fork.Adult ? $"{s.Status.Fork.Name} (18+)" : s.Status.Fork.Name, s.Status.Fork.Model == AppearanceModel.New ? "New" : "Old",
                s.Status.Editable ? "Yes" : "Not yet", s.Status.Commit is { } commit ? ForkVersions.Short(commit) : "", s.Status.Fork.Repository,
                played.TryGetValue(s.Status.Fork.Id, out var last) ? last.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                s.Kind switch { ForkSourceKind.Folder => "Folder", ForkSourceKind.Launcher => "Launcher", _ => "Download" }, s.Folder))
            .ToList();
        var selected = Selected?.Fork.Id;
        ForkGrid.ItemsSource = rows;
        LauncherText.IsVisible = unknown.Count > 0;
        LauncherText.Text = $"Also in your SS14 launcher, not known to Paperdoll: {string.Join(", ", unknown)}.";
        LauncherButton.IsEnabled = builds.Builds.Any(b => new ForkStatus(b.Fork, null).Editable);
        ForkGrid.SelectedItem = rows.FirstOrDefault(r => r.Fork.Id == selected);
        UpdateButtons();
    }

    private async void OnAdultChanged(object? sender, RoutedEventArgs e)
    {
        // Not while the window is being set up: it lists the forks itself once it opens.
        if (!IsVisible)
            return;
        _adultChanged?.Invoke(AdultBox.IsChecked == true);
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not list the forks: {ex.Message}";
        }
    }

    private ForkRow? Selected => ForkGrid.SelectedItem as ForkRow;

    private void OnSelection(object? sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var row = Selected;
        var editable = row != null && new ForkStatus(row.Fork, null).Editable;
        OpenButton.IsEnabled = editable;
        OpenButton.Content = row != null && row.Commit.Length == 0 && row.Folder == null && !row.FromLauncher ? "Download" : "Open";
        // A launcher copy is updated by copying the launcher's newest build, even one that cannot be read now.
        UpdateButton.IsEnabled = editable && (row!.Commit.Length > 0 || row.FromLauncher);
        RemoveButton.IsEnabled = row != null && (row.Commit.Length > 0 || row.Folder != null || row.FromLauncher);
        DownloadButton.IsEnabled = row != null && (row.Folder != null || row.FromLauncher);
        if (_note?.ForkId != row?.Fork.Id)
            _note = null;
        var problem = row == null ? null : _session?.FolderProblem(row.Fork.Id) is { } why ? $". {why}" : "";
        StatusText.Text = _note?.Text ?? (row == null ? "Pick a fork."
            : row.Folder != null ? $"{row.Fork.Name}: read from {row.Folder}" + problem
            : row.FromLauncher ? $"{row.Fork.Name}: copied from the SS14 launcher in {_session?.SourceOf(row.Fork.Id).Launcher}" + problem
            : $"{row.Fork.Name}: github.com/{row.Fork.Repository}, branch {row.Fork.Branch}");
    }

    // What became of the last change to a fork, shown while that fork stays chosen (the list,
    // filled again after a change, chooses it again a moment later).
    private (string? ForkId, string Text)? _note;

    private void Note(string? forkId, string text)
    {
        _note = (forkId, text);
        UpdateButtons();
    }

    private void SelectFork(string forkId)
    {
        ForkGrid.SelectedItem = ForkGrid.ItemsSource?.Cast<ForkRow>().FirstOrDefault(r => r.Fork.Id == forkId);
        UpdateButtons();
    }

    // Reads a fork from a game folder: the fork the folder holds, or the one picked when it does not say.
    private async void OnFromFolder(object? sender, RoutedEventArgs e)
    {
        if (_session == null)
            return;
        string? report = null;
        IsEnabled = false;
        _removing = true;
        ForkInfo? fork;
        ForkChoice? load = null;
        try
        {
            var chosen = await GameFolderChoice.ChooseAsync(this, _pickFolder, Selected?.Fork, text => report = text);
            fork = chosen?.Fork;
            // The open fork is loaded from the folder before the folder is kept as its source.
            if (chosen is { } open && open.Fork.Id == _session.Fork?.Id)
                load = new ForkChoice(open.Fork, Update: false, ForkSource.FromFolder(open.Folder));
            else if (chosen is { } other)
            {
                try
                {
                    await Task.Run(() => _session.UseFolderAsync(other.Fork, other.Folder));
                    _sourcesChanged?.Invoke();
                    report = $"{other.Fork.Name} is now read from {other.Folder}, as it is on disk.";
                }
                catch (GameFolderException ex)
                {
                    fork = null;
                    report = ex.Message;
                }
            }
            if (load == null)
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            fork = null;
            report = $"Could not use that folder: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
            _removing = false;
        }
        if (load != null)
        {
            Close(load);
            return;
        }
        if (fork != null)
            SelectFork(fork.Id);
        if (report != null)
            Note(fork?.Id ?? Selected?.Fork.Id, fork != null ? report + " Open loads it." : report);
    }

    // The builds the launcher held when the list was last filled.
    private LauncherScan _builds = new([], []);

    // Copies a fork from the launcher: the fork is picked from the builds it holds, then loaded,
    // with progress and Cancel in the main window, and kept as the fork's source if it loads.
    private async void OnFromLauncher(object? sender, RoutedEventArgs e)
    {
        if (_session == null)
            return;
        if (await new LauncherBuildsWindow(_builds, Selected?.Fork).ShowDialog<LauncherBuild?>(this) is { } build)
            Close(new ForkChoice(build.Fork, Update: false, ForkSource.FromLauncher(build.Launcher)));
    }

    private async void OnUseDownload(object? sender, RoutedEventArgs e)
    {
        if (_session == null || Selected is not { } row || (row.Folder == null && !row.FromLauncher))
            return;
        // The open fork is loaded from its download before the download is kept as its source.
        if (row.Fork.Id == _session.Fork?.Id)
        {
            Close(new ForkChoice(row.Fork, Update: false, ForkSource.Download));
            return;
        }
        try
        {
            await Task.Run(() => _session.UseDownloadAsync(row.Fork));
            _sourcesChanged?.Invoke();
            await RefreshAsync();
            SelectFork(row.Fork.Id);
        }
        catch (Exception ex)
        {
            Note(row.Fork.Id, $"Could not switch {row.Fork.Name} back to Download: {ex.Message}");
            return;
        }
        Note(row.Fork.Id, row.FromLauncher
            ? $"{row.Fork.Name} is downloaded again from now on; Paperdoll's copy from the SS14 launcher was deleted, and the launcher left as it is."
            : $"{row.Fork.Name} is downloaded again from now on; {row.Folder} was left as it is.");
    }

    private void OnOpenFork(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            Close(new ForkChoice(row.Fork, Update: false));
    }

    private void OnUpdateFork(object? sender, RoutedEventArgs e)
    {
        // A launcher copy is updated by copying the launcher's newest build of it again.
        if (Selected is { } row)
            Close(new ForkChoice(row.Fork, Update: true, row.FromLauncher ? _session?.SourceOf(row.Fork.Id) : null));
    }

    private async void OnRemoveFork(object? sender, RoutedEventArgs e)
    {
        if (_session == null || Selected is not { } row)
            return;
        if (row.Fork.Id == _session.Fork?.Id)
        {
            StatusText.Text = "That fork is open; open another one before removing it.";
            return;
        }
        StatusText.Text = $"Removing {row.Fork.Name}...";
        // Nothing else in the window until it is done, and it stays open: a second removal or a
        // download at the same time would trip over the first.
        IsEnabled = false;
        _removing = true;
        try
        {
            await Task.Run(() => _session.RemoveForkAsync(row.Fork));
            if (row.Folder != null || row.FromLauncher)
                _sourcesChanged?.Invoke();
            await RefreshAsync();
            Note(row.Fork.Id, row.Folder != null ? $"Removed {row.Fork.Name}; {row.Folder} was left as it is."
                : row.FromLauncher ? $"Removed {row.Fork.Name}: Paperdoll's copy was deleted, and the SS14 launcher left as it is."
                : $"Removed {row.Fork.Name}.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not remove {row.Fork.Name}: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
            _removing = false;
        }
    }

    private bool _removing;

    private async void OnFiles(object? sender, RoutedEventArgs e)
    {
        if (_showFiles != null)
            await _showFiles(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(null);
}
