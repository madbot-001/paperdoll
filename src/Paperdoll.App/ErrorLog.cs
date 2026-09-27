namespace Paperdoll.App;

/// <summary>
/// Errors nothing else caught, written to errors.log in Paperdoll's own folder for the user to
/// look at or pass on themselves. Nothing sends it anywhere.
/// </summary>
public static class ErrorLog
{
    private const long MaxSize = 256 * 1024;
    private static string? _last;
    private static DateTime _lastAt;

    public static string Path => System.IO.Path.Combine(MainWindow.DataDirectory, "errors.log");

    /// <summary>Adds an error to the log, unless it just did the same one. Returns the log's path, or null if it could not write.</summary>
    public static string? Write(Exception? error, bool fatal = false)
    {
        try
        {
            var text = error?.ToString() ?? "An unknown error.";
            var now = DateTime.Now;
            if (text == _last && now - _lastAt < TimeSpan.FromSeconds(10))
                return Path;
            (_last, _lastAt) = (text, now);

            Directory.CreateDirectory(MainWindow.DataDirectory);
            // Kept small: the newest half is kept when it grows too big.
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxSize)
            {
                var old = File.ReadAllText(Path);
                File.WriteAllText(Path, old[(old.Length / 2)..]);
            }
            var version = typeof(ErrorLog).Assembly.GetName().Version;
            File.AppendAllText(Path, $"{now:yyyy-MM-dd HH:mm:ss} Paperdoll {version}{(fatal ? ", closing" : "")}{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}");
            return Path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
