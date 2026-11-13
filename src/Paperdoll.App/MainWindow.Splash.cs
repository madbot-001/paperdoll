using System.Diagnostics;
using Avalonia.Interactivity;
using Paperdoll.Core.Editing;

namespace Paperdoll.App;

public partial class MainWindow
{
    // Shown instead of this window during the first load; null once it has closed.
    private SplashWindow? _splash;
    private Stopwatch _splashClock = new();
    private TimeSpan _splashLeast;
    // Where the first load gets its session; null for the store PAPERDOLL_STORE names, or the usual one.
    private Func<Task<EditorSession>>? _openSession;

    /// <summary>
    /// Shows <paramref name="splash"/> and starts the first load without showing this window,
    /// which takes the splash's place once the load ends or needs the user, the splash having been
    /// up for at least <paramref name="least"/>. Closed by the user, the splash takes this window
    /// with it: it is never shown, as Paperdoll quits.
    /// </summary>
    /// <param name="openSession">Where the session comes from; null for the store PAPERDOLL_STORE names, or the usual one.</param>
    public void StartBehindSplash(SplashWindow splash, TimeSpan least, Func<Task<EditorSession>>? openSession = null)
    {
        splash.CancelClicked += (_, _) => OnCancelLoad(splash, new RoutedEventArgs());
        splash.Closed += (_, _) =>
        {
            if (_splash == splash)
                _splash = null;
        };
        splash.Show();
        _splash = splash;
        _splashLeast = least;
        _splashClock = Stopwatch.StartNew();
        _openSession = openSession;
        Startup = StartAsync();
    }

    // Once the splash has been up long enough, closes it and shows this window instead.
    private async Task LeaveSplashAsync()
    {
        if (_splash == null)
            return;
        var left = _splashLeast - _splashClock.Elapsed;
        if (left > TimeSpan.Zero)
            await Task.Delay(left);
        LeaveSplash();
    }

    // Closes the splash, if it is up, and shows this window instead.
    private void LeaveSplash()
    {
        if (_splash is not { } splash)
            return;
        _splash = null;
        Show();
        splash.Close();
    }
}
