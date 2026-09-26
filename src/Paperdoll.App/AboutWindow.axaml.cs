using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Paperdoll.App;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
