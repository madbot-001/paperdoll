using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <summary>Asks, when no fork has a source yet, where the game's files are to come from. Closing it cancels.</summary>
public partial class SourceWindow : Window
{
    public SourceWindow() => InitializeComponent();

    private void OnContinue(object? sender, RoutedEventArgs e) =>
        Close(FolderChoice.IsChecked == true ? ForkSourceKind.Folder : ForkSourceKind.Download);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
