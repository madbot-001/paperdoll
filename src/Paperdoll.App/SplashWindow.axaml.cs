using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Paperdoll.App;

/// <summary>
/// Shown instead of the main window while Paperdoll first loads: its name and version, the step
/// under way, and Cancel while a download can be stopped.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        // The build adds the commit after a "+"; the version is what comes before it.
        if (typeof(SplashWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } version)
            VersionText.Text = $"Version {version.Split('+')[0]}";
    }

    /// <summary>Raised when Cancel is clicked.</summary>
    public event EventHandler? CancelClicked;

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Shows Cancel as the main window's status bar shows its own.</summary>
    public void SetCancel(bool visible, bool enabled)
    {
        CancelButton.IsVisible = visible;
        CancelButton.IsEnabled = enabled;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => CancelClicked?.Invoke(this, EventArgs.Empty);
}
