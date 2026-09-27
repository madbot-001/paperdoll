using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

/// <summary>The window over the made-up forks in <see cref="TestForks"/>.</summary>
public class TypingTests
{
    [Fact]
    public async Task What_is_being_typed_is_kept_when_the_window_closes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        editor.Edit(f => f.Name = "Test Person");
        const string name = "Test Person";

        var session = HeadlessApp.Session;
        await session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == name);
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            box.Text = "Typed Person";
            // What closing saves is taken while it runs, before the box would lose focus.
            string? atClosing = null;
            window.Closing += (_, _) => atClosing = editor.File!.Name;

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Typed Person", atClosing);
        }, ct);
    }
}
