using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

public class ErrorTests
{
    [Fact]
    public async Task An_unexpected_error_is_logged_and_shown_and_the_window_carries_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var data = Directory.CreateTempSubdirectory("paperdoll-errors-").FullName;
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        try
        {
            await using var editor = TestForks.Session();
            await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);

            var session = HeadlessApp.Session;
            await session.Dispatch(() =>
            {
                var window = new MainWindow(editor) { Width = 1100, Height = 720 };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                window.OnUnexpectedError(new InvalidOperationException("Made-up failure."));

                Assert.Contains("Made-up failure.", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").First(t => t.StartsWith("Something went wrong", StringComparison.Ordinal)));
                Assert.Contains("Made-up failure.", File.ReadAllText(Path.Combine(data, "errors.log")));
                window.Close();
            }, ct);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
            Directory.Delete(data, recursive: true);
        }
    }
}
