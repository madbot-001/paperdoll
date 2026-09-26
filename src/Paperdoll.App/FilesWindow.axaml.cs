using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <summary>Where Paperdoll keeps its files, how much room they take, and ways to tidy them.</summary>
public partial class FilesWindow : Window
{
    private readonly EditorSession? _session;
    private readonly string _autosave = "";
    private readonly string _settings = "";
    private readonly Action? _autosaveDeleted;

    public FilesWindow() => InitializeComponent();

    public FilesWindow(EditorSession session, string autosave, string settings, Action autosaveDeleted) : this()
    {
        _session = session;
        _autosave = autosave;
        _settings = settings;
        _autosaveDeleted = autosaveDeleted;
        Build();
    }

    private string StoreFolder => Path.GetDirectoryName(_session!.Store.Directory) ?? _session.Store.Directory;

    private void Build()
    {
        Rows.Children.Clear();

        var clean = new Button { Content = "Clean up", Classes = { "small" } };
        clean.Click += async (_, _) => await CleanUpAsync(clean);
        Rows.Children.Add(Section("Fork downloads", StoreFolder, $"{Size(StoreSize.Of(StoreFolder))}, through {_session!.StoreKind}.",
            "Game data for the forks you downloaded. Clean up removes what older versions left behind after updates; your forks stay ready to use.",
            clean));

        var delete = new Button { Content = "Delete", Classes = { "small" }, IsEnabled = File.Exists(_autosave) };
        delete.Click += (_, _) =>
        {
            File.Delete(_autosave);
            _autosaveDeleted?.Invoke();
            StatusText.Text = "Deleted the working copy. With autosave on, the next change saves a new one.";
            Build();
        };
        Rows.Children.Add(Section("Working copy", _autosave,
            File.Exists(_autosave) ? $"Saved {File.GetLastWriteTime(_autosave):g}." : "None saved.",
            "The character as you last left it, reopened when Paperdoll starts (File > Autosave).",
            delete));

        Rows.Children.Add(Section("Settings", _settings, File.Exists(_settings) ? Size(new FileInfo(_settings).Length) + "." : "Not saved yet.",
            "Autosave and Random choices, and the file Save writes to.", null));
    }

    private Control Section(string title, string path, string detail, string about, Button? action)
    {
        var open = new Button { Content = "Open folder", Classes = { "small" } };
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        open.IsEnabled = Directory.Exists(folder);
        open.Click += async (_, _) =>
        {
            if (GetTopLevel(this)?.Launcher is { } launcher && !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder)))
                StatusText.Text = $"Could not open {folder}.";
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 3, 0, 0) };
        buttons.Children.Add(open);
        if (action != null)
            buttons.Children.Add(action);

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new SelectableTextBlock { Text = path, Classes = { "mono" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = detail });
        panel.Children.Add(new TextBlock { Text = about, Classes = { "hint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(buttons);
        return panel;
    }

    private async Task CleanUpAsync(Button button)
    {
        button.IsEnabled = false;
        StatusText.Text = "Cleaning up...";
        try
        {
            var freed = await Task.Run(() => _session!.CleanUpStoreAsync());
            StatusText.Text = freed > 0 ? $"Freed {Size(freed)}." : "Nothing to clean up.";
        }
        catch (Exception e)
        {
            StatusText.Text = $"Could not clean up: {e.Message}";
        }
        Build();
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
