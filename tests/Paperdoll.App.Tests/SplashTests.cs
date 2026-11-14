using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;
using Paperdoll.Core.Tests;

namespace Paperdoll.App.Tests;

/// <summary>The splash shown instead of the main window while Paperdoll first loads.</summary>
[Collection(nameof(OwnFolder))]
public class SplashTests
{
    private static readonly ForkInfo Upstream = KnownForks.Find("upstream")!;

    // A store with Wizard's Den downloaded, made of the test forks' body.
    private static MemoryForkStore Store()
    {
        var store = new MemoryForkStore().Add(Upstream.Id, "Resources/Prototypes/body.yml", TestForks.Body);
        store.SyncAsync(Upstream).GetAwaiter().GetResult();
        return store;
    }

    private static T Find<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    private static string? Status(Window window) => Find<TextBlock>(window, "StatusText").Text;

    /// <summary>
    /// Starts a main window behind a splash, in a data folder of its own so no working copy or
    /// settings from other tests are read, with its session from <paramref name="open"/>.
    /// </summary>
    /// <param name="settings">The settings file to start with, if any.</param>
    private static async Task OnStart(Func<Task<EditorSession>> open, Func<MainWindow, SplashWindow, Task> test, TimeSpan least = default, string? settings = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var data = Path.Combine(TestData.Directory, "splash-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        if (settings != null)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), settings);
        }
        try
        {
            await HeadlessApp.Session.Dispatch(async () =>
            {
                var window = new MainWindow { Width = 1100, Height = 720 };
                var splash = new SplashWindow();
                window.StartBehindSplash(splash, least, open);
                try
                {
                    await test(window, splash);
                }
                finally
                {
                    splash.Close();
                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, ct);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
            if (Directory.Exists(data))
                Directory.Delete(data, true);
        }
    }

    [Fact]
    public async Task The_splash_shows_the_step_under_way_instead_of_the_main_window_until_the_load_ends()
    {
        var release = new TaskCompletionSource();
        await using var editor = new EditorSession(Store());
        await OnStart(async () =>
        {
            await release.Task;
            return editor;
        }, async (window, splash) =>
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(splash.IsVisible);
            Assert.False(window.IsVisible);
            Assert.Equal("Opening the fork store", Status(splash));
            Assert.Equal(Status(window), Status(splash));
            // Opening the store cannot be stopped.
            Assert.False(Find<Button>(splash, "CancelButton").IsVisible);

            release.SetResult();
            await window.Startup;

            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
            Assert.StartsWith("Wizard's Den (upstream) loaded.", Status(window));
            Assert.True(Find<Menu>(window, "MainMenu").IsEnabled);
        });
    }

    [Fact]
    public async Task The_splash_stays_up_for_its_least_time()
    {
        await using var editor = new EditorSession(Store());
        await OnStart(() => Task.FromResult(editor), async (window, splash) =>
        {
            var clock = Stopwatch.StartNew();
            await window.Startup;
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(140));
            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
        }, least: TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public Task A_start_that_fails_closes_the_splash_and_shows_why_in_the_main_window() =>
        OnStart(() => Task.FromException<EditorSession>(new IOException("Made-up store failure.")), async (window, splash) =>
        {
            await window.Startup;

            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
            Assert.Equal("Could not start: Made-up store failure.", Status(window));
        });

    [Fact]
    public async Task Cancel_shows_on_the_splash_while_the_load_can_be_stopped_and_stops_it()
    {
        var store = Store();
        store.CommitHold = new TaskCompletionSource();
        await using var editor = new EditorSession(store);
        await OnStart(() => Task.FromResult(editor), async (window, splash) =>
        {
            var cancel = Find<Button>(splash, "CancelButton");
            DemoRoomTests.WaitFor(() => cancel.IsVisible);
            Assert.True(cancel.IsEnabled);
            Assert.False(window.IsVisible);

            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(cancel.IsEnabled);
            Assert.Equal("Stopping...", Status(splash));
            await window.Startup;

            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
            Assert.Equal("Stopped. Wizard's Den (upstream) stays as it was.", Status(window));
            Assert.False(Find<Button>(window, "CancelButton").IsVisible);
        });
    }

    [Fact]
    public async Task With_no_fork_from_anywhere_the_main_window_replaces_the_splash_and_asks_where_forks_come_from()
    {
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, splash) =>
        {
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());

            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
            Assert.StartsWith("No fork downloaded yet.", Status(window));
            Assert.Empty(window.OwnedWindows.OfType<ForksWindow>());

            // Download goes on to the Forks window, as before.
            var ask = window.OwnedWindows.OfType<SourceWindow>().Single();
            Assert.True(Find<RadioButton>(ask, "DownloadChoice").IsChecked);
            Click(Find<Button>(ask, "ContinueButton"));
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<ForksWindow>().Any());
            window.OwnedWindows.OfType<ForksWindow>().Single().Close();
            await window.Startup;
        });
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task Cancelling_the_question_leaves_Paperdoll_open_with_no_fork()
    {
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, _) =>
        {
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());
            window.OwnedWindows.OfType<SourceWindow>().Single().Close();
            await window.Startup;

            Assert.True(window.IsVisible);
            Assert.Empty(window.OwnedWindows);
            Assert.Null(editor.Fork);
            Assert.StartsWith("No fork downloaded yet.", Status(window));
            Assert.True(Find<Menu>(window, "MainMenu").IsEnabled);
        });
    }

    // A made-up clone of Wizard's Den's repository, holding the test forks' body.
    private static string UpstreamClone(string? origin = "https://github.com/space-wizards/space-station-14.git")
    {
        var folder = TestGameFolders.Write(Directory.CreateDirectory(Path.Combine(TestData.Directory, "clone-" + Guid.NewGuid().ToString("N"))).FullName, "b");
        TestGameFolders.Git(folder, origin);
        return folder;
    }

    [Fact]
    public async Task Game_folder_picks_a_folder_reads_its_fork_from_it_and_keeps_the_choice_for_the_next_start()
    {
        var folder = UpstreamClone();
        var before = TestGameFolders.Snapshot(folder);
        string? settings = null;
        await using (var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore())))
        {
            await OnStart(() => Task.FromResult(editor), async (window, _) =>
            {
                window.PickFolder = _ => Task.FromResult<string?>(folder);
                DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());
                var ask = window.OwnedWindows.OfType<SourceWindow>().Single();
                Find<RadioButton>(ask, "FolderChoice").IsChecked = true;
                Click(Find<Button>(ask, "ContinueButton"));
                await window.Startup;

                Assert.StartsWith("Wizard's Den (upstream) loaded.", Status(window));
                Assert.Equal("Wizard's Den (upstream) 01234567 (folder)", Find<TextBlock>(window, "StatusFork").Text);
                Assert.Equal(Upstream, editor.Fork);
                Assert.Empty(window.OwnedWindows);
                settings = File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("PAPERDOLL_DATA")!, "settings.json"));
            });
        }
        var saved = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(settings!)!;
        Assert.Equal(new Dictionary<string, ForkSource> { ["upstream"] = ForkSource.FromFolder(folder) }, saved.Sources);

        // The next start reads the fork from the folder again, without asking.
        await using (var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore())))
        {
            await OnStart(() => Task.FromResult(editor), async (window, _) =>
            {
                await window.Startup;
                Assert.StartsWith("Wizard's Den (upstream) loaded.", Status(window));
                Assert.Equal(ForkSource.FromFolder(folder), editor.SourceOf("upstream"));
                Assert.Empty(window.OwnedWindows);
            }, settings: settings);
        }
        Assert.Equal(before, TestGameFolders.Snapshot(folder));
    }

    [Fact]
    public async Task A_folder_the_fork_cannot_be_read_from_is_not_kept()
    {
        var folder = UpstreamClone();
        await using (var huge = File.Create(Path.Combine(folder, "Resources", "Prototypes", "huge.yml")))
            huge.SetLength(GameFolder.MaxFileSize + 1);
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, _) =>
        {
            window.PickFolder = _ => Task.FromResult<string?>(folder);
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());
            var ask = window.OwnedWindows.OfType<SourceWindow>().Single();
            Find<RadioButton>(ask, "FolderChoice").IsChecked = true;
            Click(Find<Button>(ask, "ContinueButton"));
            await window.Startup;

            Assert.StartsWith("Could not load Wizard's Den (upstream): Could not read Resources/Prototypes/huge.yml", Status(window));
            Assert.Null(editor.Fork);
            Assert.Empty(editor.Sources());
            var settings = Path.Combine(Environment.GetEnvironmentVariable("PAPERDOLL_DATA")!, "settings.json");
            Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settings))!.Sources);
        });
    }

    [Fact]
    public async Task A_folder_whose_remote_does_not_say_asks_for_the_fork()
    {
        var folder = UpstreamClone(origin: null);
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, _) =>
        {
            window.PickFolder = _ => Task.FromResult<string?>(folder);
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());
            var ask = window.OwnedWindows.OfType<SourceWindow>().Single();
            Find<RadioButton>(ask, "FolderChoice").IsChecked = true;
            Click(Find<Button>(ask, "ContinueButton"));
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<ChooseForkWindow>().Any());

            var choose = window.OwnedWindows.OfType<ChooseForkWindow>().Single();
            var list = Find<ListBox>(choose, "ForkList");
            Assert.False(Find<Button>(choose, "OkButton").IsEnabled);
            list.SelectedItem = Upstream.Name;
            Click(Find<Button>(choose, "OkButton"));
            await window.Startup;

            Assert.StartsWith("Wizard's Den (upstream) loaded.", Status(window));
            Assert.Equal(ForkSource.FromFolder(folder), editor.SourceOf("upstream"));
        });
    }

    [Fact]
    public async Task A_folder_that_is_not_a_game_folder_is_explained_and_no_fork_loads()
    {
        var empty = Directory.CreateDirectory(Path.Combine(TestData.Directory, "empty-" + Guid.NewGuid().ToString("N"))).FullName;
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, _) =>
        {
            window.PickFolder = _ => Task.FromResult<string?>(empty);
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<SourceWindow>().Any());
            var ask = window.OwnedWindows.OfType<SourceWindow>().Single();
            Find<RadioButton>(ask, "FolderChoice").IsChecked = true;
            Click(Find<Button>(ask, "ContinueButton"));
            await window.Startup;

            Assert.Contains("has no Resources/Prototypes or Resources/Textures", Status(window));
            Assert.Null(editor.Fork);
            Assert.Empty(editor.Sources());
        });
    }

    [Fact]
    public async Task With_a_source_kept_but_its_folder_gone_the_Forks_window_opens_rather_than_the_question()
    {
        var gone = Path.Combine(TestData.Directory, "gone-" + Guid.NewGuid().ToString("N"));
        var settings = System.Text.Json.JsonSerializer.Serialize(new AppSettings { Sources = new() { ["upstream"] = ForkSource.FromFolder(gone) } });
        await using var editor = new EditorSession(new SourcedForkStore(new MemoryForkStore()));
        await OnStart(() => Task.FromResult(editor), async (window, _) =>
        {
            DemoRoomTests.WaitFor(() => window.OwnedWindows.OfType<ForksWindow>().Any());
            Assert.Empty(window.OwnedWindows.OfType<SourceWindow>());
            window.OwnedWindows.OfType<ForksWindow>().Single().Close();
            await window.Startup;
        }, settings: settings);
    }

    [Fact]
    public void Settings_from_before_sources_have_every_fork_downloaded()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"Autosave":true,"Floor":2,"MatchedServers":{}}""")!;
        Assert.Empty(old.Sources);
    }

    [Fact]
    public async Task An_unexpected_error_during_the_load_closes_the_splash_and_is_shown_in_the_main_window()
    {
        var release = new TaskCompletionSource();
        await using var editor = new EditorSession(Store());
        await OnStart(async () =>
        {
            await release.Task;
            return editor;
        }, async (window, splash) =>
        {
            window.OnUnexpectedError(new InvalidOperationException("Made-up failure."));

            Assert.False(splash.IsVisible);
            Assert.True(window.IsVisible);
            Assert.StartsWith("Something went wrong: Made-up failure.", Status(window));
            release.SetResult();
            await window.Startup;
            Assert.True(window.IsVisible);
        });
    }

    [Fact]
    public async Task A_splash_closed_by_the_user_takes_the_main_window_with_it()
    {
        var release = new TaskCompletionSource();
        await using var editor = new EditorSession(Store());
        await OnStart(async () =>
        {
            await release.Task;
            return editor;
        }, async (window, splash) =>
        {
            splash.Close();
            release.SetResult();
            await window.Startup;

            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public async Task A_window_on_a_session_shows_no_splash()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var editor = TestForks.Session();
        await editor.LoadForkAsync(TestForks.A, update: false, ct: ct);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new MainWindow(editor) { Width = 1100, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(typeof(MainWindow).GetField("_splash", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window));
            Assert.True(window.Startup.IsCompleted);
            Assert.StartsWith("Fork A loaded.", Status(window));
            window.Close();
        }, ct);
    }

    [Fact]
    public Task The_splash_shows_the_version_without_the_commit() => HeadlessApp.Session.Dispatch(() =>
    {
        var text = Find<TextBlock>(new SplashWindow(), "VersionText").Text;
        Assert.Matches(@"^Version \d+\.\d+\.\d+$", text);
    }, TestContext.Current.CancellationToken);
}
