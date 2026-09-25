using Avalonia;

namespace Paperdoll.App;

internal static class Program
{
    // Nothing that needs Avalonia may run before BuildAvaloniaApp.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
