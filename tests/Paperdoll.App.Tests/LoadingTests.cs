using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

public class LoadingTests
{
    [Fact]
    public async Task Shortcut_keys_change_nothing_while_a_fork_loads()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);
        editor.Edit(f => f.Name = "Kept Person");

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var file = editor.File;
            // As during a load: set by the window itself while one runs.
            typeof(MainWindow).GetField("_loading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);

            window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
            window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(file, editor.File);
            Assert.Equal("Kept Person", editor.File!.Name);
            window.Close();
        }, ct);
    }
}
