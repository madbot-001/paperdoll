using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Forks;

namespace Paperdoll.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ForkCount.Text = $"{KnownForks.All.Count} known forks. None downloaded yet.";
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
