using System.Diagnostics;
using System.Globalization;
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
    /// <remarks>
    /// Forks already downloaded one way stay in use: git installed or updated later does not make
    /// forks downloaded through the API seem to vanish.
    /// </remarks>
    public static async Task<IForkStore> OpenAsync(string directory, string gitPath = "git", string? githubToken = null, CancellationToken ct = default)
    {
        var gitDirectory = Path.Combine(directory, "git");
        var filesDirectory = Path.Combine(directory, "files");
        var useGit = await GitVersionAsync(gitPath, ct) >= MinimumGit
            && (HasGitForks(gitDirectory) || !HasApiForks(filesDirectory));
        IForkStore store = useGit
            ? new GitForkStore(gitDirectory, gitPath)
            : new GitHubForkStore(filesDirectory, token: githubToken);
        await store.InitializeAsync(ct);
        return store;
    }

    private static bool HasGitForks(string directory) =>
        (Directory.Exists(Path.Combine(directory, "refs", "paperdoll")) && Directory.EnumerateFiles(Path.Combine(directory, "refs", "paperdoll")).Any())
        || (File.Exists(Path.Combine(directory, "packed-refs")) && File.ReadAllText(Path.Combine(directory, "packed-refs")).Contains(" refs/paperdoll/", StringComparison.Ordinal));

    private static bool HasApiForks(string directory) =>
        Directory.Exists(Path.Combine(directory, "forks")) && Directory.EnumerateFiles(Path.Combine(directory, "forks"), "*.json").Any();

    /// <summary>The installed git's version, or null if git cannot be run.</summary>
    public static async Task<Version?> GitVersionAsync(string gitPath = "git", CancellationToken ct = default)
    {
        // On a Mac without Apple's developer tools, /usr/bin/git only offers to install them, in a
        // window, every time it is run.
        if (OperatingSystem.IsMacOS() && gitPath == "git" && FirstOnPath("git") == "/usr/bin/git" && !await AppleToolsInstalledAsync(ct))
            return null;
        try
        {
            var info = GitCommand.NewStartInfo(gitPath);
            info.ArgumentList.Add("--version");
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

    private static string? FirstOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, program))
            .FirstOrDefault(File.Exists);

    // xcode-select tells without offering to install anything.
    private static async Task<bool> AppleToolsInstalledAsync(CancellationToken ct)
    {
        try
        {
            var info = new ProcessStartInfo("/usr/bin/xcode-select", "-p") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info);
            if (process == null)
                return false;
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Reads "git version 2.55.0" or "git version 2.45.1.windows.1".</summary>
    public static Version? ParseGitVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
    }

    [GeneratedRegex(@"git version (\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
