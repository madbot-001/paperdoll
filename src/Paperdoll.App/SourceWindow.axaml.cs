using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <summary>Asks, when no fork has a source yet, where the game's files are to come from. Closing it cancels.</summary>
public partial class SourceWindow : Window
{
    public SourceWindow() => InitializeComponent();

    /// <param name="noLauncher">Why the SS14 launcher cannot be chosen, or null when it can.</param>
    public SourceWindow(string? noLauncher) : this()
    {
        LauncherChoice.IsEnabled = noLauncher == null;
        if (noLauncher != null)
            LauncherHint.Text = noLauncher;
    }

    private void OnContinue(object? sender, RoutedEventArgs e) =>
        Close(FolderChoice.IsChecked == true ? ForkSourceKind.Folder
            : LauncherChoice.IsChecked == true && LauncherChoice.IsEnabled ? ForkSourceKind.Launcher
            : ForkSourceKind.Download);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
