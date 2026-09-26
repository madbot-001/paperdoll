using Paperdoll.Core.Store;

namespace Paperdoll.Core.Tests;

public sealed class ForkStoresTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-stores-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("git version 2.55.0\n", 2, 55)]
    [InlineData("git version 2.45.1.windows.1", 2, 45)]
    [InlineData("git version 2.39.5 (Apple Git-154)", 2, 39)]
    public void Reads_git_versions(string output, int major, int minor)
    {
        Assert.Equal(new Version(major, minor), ForkStores.ParseGitVersion(output));
    }

    [Fact]
    public async Task Uses_git_when_it_is_installed()
    {
        var ct = TestContext.Current.CancellationToken;
        var version = await ForkStores.GitVersionAsync(ct: ct);
        Assert.SkipWhen(version == null || version < ForkStores.MinimumGit, "Needs git 2.45 or newer.");

        var store = await ForkStores.OpenAsync(_root, ct: ct);

        Assert.IsType<GitForkStore>(store);
    }

    [Fact]
    public async Task Falls_back_to_the_github_api_without_git()
    {
        var ct = TestContext.Current.CancellationToken;

        var store = await ForkStores.OpenAsync(_root, gitPath: Path.Combine(_root, "no-such-git"), ct: ct);

        Assert.IsType<GitHubForkStore>(store);
    }
}
