using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Paperdoll.App;

/// <summary>A message with an OK button.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow() => InitializeComponent();

    public MessageWindow(string message) : this() => MessageText.Text = message;

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
