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
            desktop.MainWindow = new MainWindow();
            // An error nothing else caught is shown and logged, and the working copy saved,
            // rather than closing Paperdoll.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                if (desktop.MainWindow is MainWindow main)
                    main.OnUnexpectedError(e.Exception);
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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
