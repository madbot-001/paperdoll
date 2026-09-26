using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Paperdoll.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        if (typeof(AboutWindow).Assembly.GetName().Version is { } version)
            TitleText.Text = $"Paperdoll {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
