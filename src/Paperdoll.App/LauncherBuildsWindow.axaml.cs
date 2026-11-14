using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <param name="Version">The build's version, as shown.</param>
/// <param name="LastPlayed">When the launcher last ran it, as a local date.</param>
public sealed record LauncherRow(LauncherBuild Build, string Name, string Version, string LastPlayed);

/// <summary>
/// Lists the newest build of each fork the SS14 launcher holds, for the player to pick the fork
/// to copy. Closes with the build, or null when cancelled.
/// </summary>
public partial class LauncherBuildsWindow : Window
{
    public LauncherBuildsWindow() => InitializeComponent();

    /// <param name="first">The fork to have chosen at first, if the launcher holds it.</param>
    public LauncherBuildsWindow(LauncherScan scan, ForkInfo? first) : this()
    {
        var editable = scan.Builds.Where(b => new ForkStatus(b.Fork, null).Editable).ToList();
        var rows = editable
            .Select(b => new LauncherRow(b, b.Fork.Adult ? $"{b.Fork.Name} (18+)" : b.Fork.Name, b.ShortVersion,
                b.LastUsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();
        BuildGrid.ItemsSource = rows;
        BuildGrid.SelectedItem = rows.FirstOrDefault(r => r.Build.Fork.Id == first?.Id) ?? (rows.Count == 1 ? rows[0] : null);
        MessageText.Text = rows.Count > 0
            ? "Choose the fork to copy. Paperdoll copies the game files it reads from the launcher's newest build of it into its own folder, checking each one; the launcher is only read, never changed, and is not needed afterwards."
            : "The SS14 launcher on this computer holds no build of a fork Paperdoll can edit. Join one of the fork's servers with the launcher once, then try again.";
        var notes = new List<string>();
        var others = scan.Builds.Except(editable).Select(b => b.Fork.Name).ToList();
        if (others.Count > 0)
            notes.Add($"Also in the launcher, not editable yet: {string.Join(", ", others)}.");
        notes.AddRange(scan.Problems);
        NoteText.Text = string.Join(" ", notes);
        NoteText.IsVisible = notes.Count > 0;
        CopyButton.IsEnabled = BuildGrid.SelectedItem != null;
    }

    private void OnSelection(object? sender, SelectionChangedEventArgs e) => CopyButton.IsEnabled = BuildGrid.SelectedItem != null;

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (BuildGrid.SelectedItem is LauncherRow row)
            Close(row.Build);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
