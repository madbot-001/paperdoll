using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.App;

public sealed record ForkChoice(ForkInfo Fork, bool Update);

public sealed record ForkRow(ForkInfo Fork, string Name, string Model, string Editable, string Commit, string Repository, string LastPlayed);

/// <summary>Lists the known forks; opens, updates or removes them.</summary>
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

    public ForksWindow(EditorSession session, Func<Window, Task>? showFiles = null, IReadOnlyList<string>? launcherFolders = null,
        bool showAdult = false, Action<bool>? adultChanged = null) : this()
    {
        _session = session;
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
        // 18+ forks are listed only when asked for; one already downloaded or open stays, so it can be removed.
        var showAdult = AdultBox.IsChecked == true;
        var rows = statuses
            .Where(s => showAdult || !s.Fork.Adult || s.Commit != null || s.Fork.Id == _session.Fork?.Id)
            .OrderByDescending(s => played.GetValueOrDefault(s.Fork.Id)).ThenByDescending(s => s.Editable).ThenBy(s => s.Fork.Name)
            .Select(s => new ForkRow(s.Fork, s.Fork.Adult ? $"{s.Fork.Name} (18+)" : s.Fork.Name, s.Fork.Model == AppearanceModel.New ? "New" : "Old",
                s.Editable ? "Yes" : "Not yet", s.Commit?[..8] ?? "", s.Fork.Repository,
                played.TryGetValue(s.Fork.Id, out var last) ? last.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""))
            .ToList();
        ForkGrid.ItemsSource = rows;
        LauncherText.IsVisible = unknown.Count > 0;
        LauncherText.Text = $"Also in your SS14 launcher, not known to Paperdoll: {string.Join(", ", unknown)}.";
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
        OpenButton.Content = row != null && row.Commit.Length == 0 ? "Download" : "Open";
        UpdateButton.IsEnabled = editable && row!.Commit.Length > 0;
        RemoveButton.IsEnabled = row != null && row.Commit.Length > 0;
        StatusText.Text = row == null ? "Pick a fork." : $"{row.Fork.Name}: github.com/{row.Fork.Repository}, branch {row.Fork.Branch}";
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
            await RefreshAsync();
            StatusText.Text = $"Removed {row.Fork.Name}.";
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
