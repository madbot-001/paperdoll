using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Paperdoll.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!InstanceLock.TryAcquire(MainWindow.DataDirectory))
            {
                desktop.MainWindow = new MessageWindow(
                    "Paperdoll is already open. Switch to that window: two copies would overwrite each other's working copy and settings.");
                base.OnFrameworkInitializationCompleted();
                return;
            }
            // The splash stands in for the main window until the first load ends. The lifetime
            // shows its main window when it starts, so it is given the main window once that opens;
            // until then the splash is the only window, and closing it quits.
            var main = new MainWindow();
            main.Opened += (_, _) => desktop.MainWindow = main;
            // An error nothing else caught is shown and logged, and the working copy saved,
            // rather than closing Paperdoll.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                // The main window, even while the splash stands in for it.
                if ((desktop.MainWindow ?? main) is MainWindow shown)
                    shown.OnUnexpectedError(e.Exception);
                else
                    ErrorLog.Write(e.Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write(e.Exception);
                e.SetObserved();
            };
            // Errors on other threads still close the program; the log at least says why.
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception, fatal: true);
            try
            {
                main.StartBehindSplash(new SplashWindow(), TimeSpan.FromMilliseconds(500));
            }
            catch (Exception e)
            {
                // No splash, then: the main window opens at once and loads in view.
                ErrorLog.Write(e);
                desktop.MainWindow = main;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
