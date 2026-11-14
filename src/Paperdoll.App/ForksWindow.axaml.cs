using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <param name="Source">A new source to read the fork from, given only for the fork that is open, which is loaded from it.</param>
public sealed record ForkChoice(ForkInfo Fork, bool Update, ForkSource? Source = null);

/// <param name="Source">Where its files come from, as shown: "Download" or "Folder".</param>
/// <param name="Folder">Its game folder, when it is read from one.</param>
public sealed record ForkRow(ForkInfo Fork, string Name, string Model, string Editable, string Commit, string Repository, string LastPlayed,
    string Source = "Download", string? Folder = null);

/// <summary>Lists the known forks; opens, updates or removes them, and sets where each one's files come from.</summary>
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
        var launcher = await Task.Run(() => LauncherForks.Read(_launcherFolders));
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
            .Select(s => (Status: s, _session.SourceOf(s.Fork.Id).Folder))
            .Where(s => showAdult || !s.Status.Fork.Adult || s.Status.Commit != null || s.Folder != null || s.Status.Fork.Id == _session.Fork?.Id)
            .OrderByDescending(s => played.GetValueOrDefault(s.Status.Fork.Id)).ThenByDescending(s => s.Status.Editable).ThenBy(s => s.Status.Fork.Name)
            .Select(s => new ForkRow(s.Status.Fork, s.Status.Fork.Adult ? $"{s.Status.Fork.Name} (18+)" : s.Status.Fork.Name, s.Status.Fork.Model == AppearanceModel.New ? "New" : "Old",
                s.Status.Editable ? "Yes" : "Not yet", s.Status.Commit is { } commit ? ForkVersions.Short(commit) : "", s.Status.Fork.Repository,
                played.TryGetValue(s.Status.Fork.Id, out var last) ? last.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                s.Folder != null ? "Folder" : "Download", s.Folder))
            .ToList();
        var selected = Selected?.Fork.Id;
        ForkGrid.ItemsSource = rows;
        LauncherText.IsVisible = unknown.Count > 0;
        LauncherText.Text = $"Also in your SS14 launcher, not known to Paperdoll: {string.Join(", ", unknown)}.";
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
        OpenButton.Content = row != null && row.Commit.Length == 0 && row.Folder == null ? "Download" : "Open";
        UpdateButton.IsEnabled = editable && row!.Commit.Length > 0;
        RemoveButton.IsEnabled = row != null && (row.Commit.Length > 0 || row.Folder != null);
        DownloadButton.IsEnabled = row?.Folder != null;
        if (_note?.ForkId != row?.Fork.Id)
            _note = null;
        StatusText.Text = _note?.Text ?? (row == null ? "Pick a fork."
            : row.Folder != null ? $"{row.Fork.Name}: read from {row.Folder}" + (_session?.FolderProblem(row.Fork.Id) is { } problem ? $". {problem}" : "")
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

    private async void OnUseDownload(object? sender, RoutedEventArgs e)
    {
        if (_session == null || Selected is not { Folder: not null } row)
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
        Note(row.Fork.Id, $"{row.Fork.Name} is downloaded again from now on; {row.Folder} was left as it is.");
    }

    private void OnOpenFork(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            Close(new ForkChoice(row.Fork, Update: false));
    }

    private void OnUpdateFork(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            Close(new ForkChoice(row.Fork, Update: true));
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
            if (row.Folder != null)
                _sourcesChanged?.Invoke();
            await RefreshAsync();
            Note(row.Fork.Id, row.Folder != null ? $"Removed {row.Fork.Name}; {row.Folder} was left as it is." : $"Removed {row.Fork.Name}.");
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
