using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.App.Tests;

/// <summary>Starts the real window without a screen, for pictures of the interface.</summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    // One for all tests: Avalonia can be started only once in a process. Never disposed:
    // disposing it never returns once a window has run (seen with Avalonia 12.1).
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    public static HeadlessUnitTestSession Session => Shared.Value;
}

public class ScreenshotTests
{
    /// <summary>
    /// Loads Delta-V (or the fork PAPERDOLL_SCREENSHOT_FORK names), opens the main window on it
    /// (optionally on a character file) and saves a picture. Set PAPERDOLL_SCREENSHOT_OUT to a
    /// folder, PAPERDOLL_STORE to reuse a download, and PAPERDOLL_SCREENSHOT_FILE to show a
    /// character file. Needs the network the first time.
    /// </summary>
    [Fact]
    public async Task Main_window_screenshot()
    {
        var output = Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_OUT");
        Assert.SkipWhen(output == null, "Set PAPERDOLL_SCREENSHOT_OUT to a folder to take screenshots.");
        var ct = TestContext.Current.CancellationToken;

        await using var editor = await EditorSession.OpenAsync(Environment.GetEnvironmentVariable("PAPERDOLL_STORE"), ct);
        var fork = KnownForks.Find(Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_FORK") ?? "deltav")
            ?? throw new InvalidOperationException("PAPERDOLL_SCREENSHOT_FORK names no known fork.");
        await editor.LoadForkAsync(fork, update: false, ct: ct);
        if (Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_FILE") is { } path)
            editor.Open(await File.ReadAllTextAsync(path, ct));
        if (Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_SPECIES") is { } species)
            editor.ChangeSpecies(species);
        // PAPERDOLL_SCREENSHOT_JOB dresses the preview for a job.
        if (Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_JOB") is { } job)
            editor.PreviewJob = job;

        // Not disposed: disposing the headless session never returns once the window has run
        // (seen with Avalonia 12.1), and its thread ends with the test process anyway.
        var session = HeadlessApp.Session;
        await session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            // PAPERDOLL_SCREENSHOT_PART selects a part ("Head/Hair/0"); PAPERDOLL_SCREENSHOT_TAB a table tab.
            if (Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_PART") is { } part)
                window.ShowPart(part, int.TryParse(Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_TAB"), out var tab) ? tab : 0);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Directory.CreateDirectory(output!);
            var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
            frame.Save(Path.Combine(output!, (Environment.GetEnvironmentVariable("PAPERDOLL_SCREENSHOT_NAME") ?? "main-window") + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
        }, ct);
    }
}
