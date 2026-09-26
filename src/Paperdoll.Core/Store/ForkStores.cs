using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Paperdoll.Core.Store;

/// <summary>Picks where to keep fork files: git when it is installed, the GitHub API otherwise.</summary>
public static partial class ForkStores
{
    /// <summary>
    /// Git 2.45 added switching off lazy fetching (<c>GIT_NO_LAZY_FETCH</c>), which the git store
    /// needs so a missing file is reported instead of fetched one request at a time.
    /// </summary>
    public static readonly Version MinimumGit = new(2, 45);

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperdoll", "store");

    /// <summary>
    /// The git store in <c>&lt;directory&gt;/git</c> when a new enough git is found, otherwise the
    /// GitHub API store in <c>&lt;directory&gt;/files</c>.
    /// </summary>
    public static async Task<IForkStore> OpenAsync(string directory, string gitPath = "git", string? githubToken = null, CancellationToken ct = default)
    {
        IForkStore store = await GitVersionAsync(gitPath, ct) >= MinimumGit
            ? new GitForkStore(Path.Combine(directory, "git"), gitPath)
            : new GitHubForkStore(Path.Combine(directory, "files"), token: githubToken);
        await store.InitializeAsync(ct);
        return store;
    }

    /// <summary>The installed git's version, or null if git cannot be run.</summary>
    public static async Task<Version?> GitVersionAsync(string gitPath = "git", CancellationToken ct = default)
    {
        try
        {
            var info = new ProcessStartInfo(gitPath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(info);
            if (process == null)
                return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? ParseGitVersion(output) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // not installed
        }
    }

    /// <summary>Reads "git version 2.55.0" or "git version 2.45.1.windows.1".</summary>
    public static Version? ParseGitVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;
    }

    [GeneratedRegex(@"git version (\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
