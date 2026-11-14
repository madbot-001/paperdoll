using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperdoll.Core.Forks;

namespace Paperdoll.App;

/// <summary>Asks which fork a game folder holds, when its git checkout does not say. Closing it cancels.</summary>
public partial class ChooseForkWindow : Window
{
    private readonly IReadOnlyList<ForkInfo> _forks = [];

    public ChooseForkWindow() => InitializeComponent();

    /// <param name="first">The fork to have chosen at first, if any.</param>
    public ChooseForkWindow(string folder, IReadOnlyList<ForkInfo> forks, ForkInfo? first) : this()
    {
        _forks = forks;
        MessageText.Text = $"Paperdoll cannot tell from its git remote which fork {folder} holds. Choose it:";
        ForkList.ItemsSource = forks.Select(f => f.Adult ? $"{f.Name} (18+)" : f.Name).ToList();
        ForkList.SelectedIndex = first == null ? -1 : forks.ToList().FindIndex(f => f.Id == first.Id);
    }

    private void OnSelection(object? sender, SelectionChangedEventArgs e) => OkButton.IsEnabled = ForkList.SelectedIndex >= 0;

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (ForkList.SelectedIndex >= 0)
            Close(_forks[ForkList.SelectedIndex]);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
