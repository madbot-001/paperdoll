using System.Diagnostics;
using System.Text;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

/// <summary>
/// Tests against the real repositories on GitHub. Skipped unless PAPERDOLL_NETWORK_TESTS=1.
/// </summary>
public sealed class NetworkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-net-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Delta_v_prototypes_and_locale_download_and_read()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        var store = new GitForkStore(Path.Combine(_root, "store.git"));
        await store.InitializeAsync(ct);
        var timer = Stopwatch.StartNew();

        var commit = await store.SyncAsync(KnownForks.Find("deltav")!, ct: ct);
        var entries = await store.ListAsync("deltav", ["Resources/Prototypes", "Resources/Locale/en-US"], ct);
        var fetched = await store.FetchAsync("deltav", entries, ct);

        await using var reader = store.OpenReader();
        var harpy = entries.Single(e => e.Path == "Resources/Prototypes/_DV/Species/harpy.yml");
        var text = Encoding.UTF8.GetString((await reader.ReadAsync(harpy.ObjectId, ct))!);

        TestContext.Current.SendDiagnosticMessage(
            $"Delta-V {commit[..8]}: {entries.Count} files, {fetched} fetched in {timer.Elapsed.TotalSeconds:F1}s");
        Assert.Contains("id: Harpy", text);
        Assert.Empty(await store.MissingAsync(entries.Select(e => e.ObjectId), ct));
    }

    [Fact]
    public async Task Delta_v_species_download_through_the_github_api()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        // Unauthenticated, GitHub allows 60 API requests an hour; this test uses about six.
        var store = new GitHubForkStore(Path.Combine(_root, "files"), token: Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
        await store.InitializeAsync(ct);

        await store.SyncAsync(KnownForks.Find("deltav")!, ct);
        var entries = await store.ListAsync("deltav", ["Resources/Prototypes/_DV/Species"], ct);
        await store.FetchAsync("deltav", entries, ct);

        await using var reader = store.OpenReader();
        var harpy = entries.Single(e => e.Path == "Resources/Prototypes/_DV/Species/harpy.yml");
        Assert.Contains("id: Harpy", Encoding.UTF8.GetString((await reader.ReadAsync(harpy.ObjectId, ct))!));
    }
}
