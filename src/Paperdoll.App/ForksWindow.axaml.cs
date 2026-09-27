using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.App;

public sealed record ForkChoice(ForkInfo Fork, bool Update);

public sealed record ForkRow(ForkInfo Fork, string Name, string Model, string Editable, string Commit, string Repository);

/// <summary>Lists the known forks; opens, updates or removes them.</summary>
public partial class ForksWindow : Window
{
    private readonly EditorSession? _session;

    public ForksWindow()
    {
        InitializeComponent();
    }

    private readonly Func<Window, Task>? _showFiles;

    public ForksWindow(EditorSession session, Func<Window, Task>? showFiles = null) : this()
    {
        _session = session;
        _showFiles = showFiles;
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
        var rows = (await _session.ForkStatusesAsync())
            .OrderByDescending(s => s.Editable).ThenBy(s => s.Fork.Name)
            .Select(s => new ForkRow(s.Fork, s.Fork.Name, s.Fork.Model == AppearanceModel.New ? "New" : "Old",
                s.Editable ? "Yes" : "Not yet", s.Commit?[..8] ?? "", s.Fork.Repository))
            .ToList();
        ForkGrid.ItemsSource = rows;
        UpdateButtons();
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
