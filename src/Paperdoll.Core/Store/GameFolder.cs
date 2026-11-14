using System.Text;
using System.Text.RegularExpressions;
using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Store;

/// <summary>A game folder that cannot be used, or a file in it that cannot be read.</summary>
public sealed class GameFolderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a game folder's git checkout says about it, read from its files without running git.</summary>
/// <param name="Commit">The commit it is on, or null when there is no checkout or it cannot be read.</param>
/// <param name="Remotes">Its remotes' names and addresses, "origin" first.</param>
/// <param name="Problem">Why some of it could not be read, other than a file not being there.</param>
public sealed record GitCheckout(string? Commit, IReadOnlyList<(string Name, string Url)> Remotes, string? Problem = null)
{
    public static readonly GitCheckout None = new(null, []);
}

/// <summary>
/// Opens of one game folder's files that were given up on and still wait, such as on a pipe
/// swapped in for a file. Past <see cref="MaxWaiting"/>, no more of the folder's files are opened
/// until it is read again, which starts a new count.
/// </summary>
public sealed class OpenGate
{
    public const int MaxWaiting = 4;

    private int _waiting;

    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>The first file given up on.</summary>
    public string? FirstStuck { get; private set; }

    internal void Stuck(string path)
    {
        FirstStuck ??= path;
        Interlocked.Increment(ref _waiting);
    }

    internal void Done() => Interlocked.Decrement(ref _waiting);
}

/// <summary>What a folder chosen as a game folder was found to be.</summary>
/// <param name="Folder">The folder, when it can be used.</param>
/// <param name="Problem">Why it cannot be used, otherwise.</param>
/// <param name="Fork">The fork its remote names, when it is sure.</param>
/// <param name="Likely">Forks its other remotes name, to offer first when it is not.</param>
public sealed record GameFolderCheck(GameFolder? Folder, string? Problem, GitCheckout Git, ForkInfo? Fork, IReadOnlyList<ForkInfo> Likely);

/// <summary>
/// A copy or git clone of a fork's repository on this computer. Only read: nothing in it is
/// written, created or deleted, and git is never run in it. No link inside it is followed, so
/// nothing outside it is listed or opened, and each file is read under its own path.
/// </summary>
/// <param name="Top">The folder holding Resources, where a git checkout keeps .git; null when
/// the folder chosen is a Resources folder not known to be the game's own.</param>
/// <param name="Resources">The Resources folder, with every link in the path to it followed.</param>
/// <param name="Root">The folder as chosen, with every link in the path to it followed: no part
/// below it is followed as a link, Resources included, whenever a file is listed or read.</param>
public sealed partial record GameFolder(string? Top, string Resources, string Root)
{
    /// <summary>The parts from <see cref="Root"/> to a path given by its parts below Resources.</summary>
    public IReadOnlyList<string> FromRoot(IEnumerable<string> belowResources) =>
        [.. Resources == Root ? [] : new[] { Path.GetFileName(Resources) }, .. belowResources];

    /// <summary>The largest file read from a game folder; no sprite, prototype or string file comes near it.</summary>
    public const long MaxFileSize = 32L * 1024 * 1024;

    // .git's own small files: HEAD, a ref, config, the .git file and commondir.
    private const int MaxSmallFile = 1024 * 1024;

    /// <summary>
    /// Checks a folder: it holds Resources/Prototypes and Resources/Textures, or is itself that
    /// Resources folder. Throws <see cref="GameFolderException"/> saying what is wrong.
    /// </summary>
    public static GameFolder Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new GameFolderException("No folder was chosen.");
        string full;
        try
        {
            full = RealPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            throw new GameFolderException($"{path} is not a folder that can be read: {e.Message}", e);
        }
        if (!Directory.Exists(full))
            throw new GameFolderException($"There is no folder at {full}.");
        if (IsLauncherFolder(full))
            throw new GameFolderException($"{full} holds the SS14 launcher's own files (content.db or __merged.yml), not a game folder. Choose a copy or git clone of the game's repository.");

        var resources = Path.Combine(full, "Resources");
        if (HoldsGameParts(resources))
            return new GameFolder(full, resources, full);
        if (HoldsGameParts(full))
        {
            // Its git checkout is used only when this is the game's own Resources folder.
            var parent = Path.GetDirectoryName(full);
            var isGames = string.Equals(Path.GetFileName(full), "Resources", StringComparison.OrdinalIgnoreCase) && parent != null && LooksLikeGame(parent);
            return new GameFolder(isGames ? parent : null, full, full);
        }

        string[] parts = ["Prototypes", "Textures"];
        if (Directory.Exists(resources))
        {
            var missing = parts.Where(p => !Directory.Exists(Path.Combine(resources, p))).Select(p => "Resources/" + p);
            throw new GameFolderException($"{full} has a Resources folder but no {string.Join(" or ", missing)}, so it is not a whole game folder.");
        }
        if (parts.Any(p => Directory.Exists(Path.Combine(full, p))))
        {
            var missing = parts.Where(p => !Directory.Exists(Path.Combine(full, p)));
            throw new GameFolderException($"{full} looks like a Resources folder but has no {string.Join(" or ", missing)}.");
        }
        throw new GameFolderException($"{full} has no Resources/Prototypes or Resources/Textures. Choose the game's top folder (the one holding Resources), or its Resources folder.");
    }

    // Whether a folder holds Prototypes and Textures. One that is reached through a link is
    // refused, as links in a game folder are not followed.
    private static bool HoldsGameParts(string folder)
    {
        if (!Directory.Exists(Path.Combine(folder, "Prototypes")) || !Directory.Exists(Path.Combine(folder, "Textures")))
            return false;
        foreach (var part in new[] { folder, Path.Combine(folder, "Prototypes"), Path.Combine(folder, "Textures") })
        {
            if (IsLink(part))
                throw new GameFolderException($"{part} is a link, and Paperdoll follows no links in a game folder. Choose the folder it leads to.");
        }
        return true;
    }

    // A git checkout of the game: .git beside the game's code (Content.Client) or solution.
    private static bool LooksLikeGame(string folder)
    {
        try
        {
            return (Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")))
                && (Directory.Exists(Path.Combine(folder, "Content.Client")) || Directory.EnumerateFiles(folder, "*.sln*").Any());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The launcher keeps a game's files merged and zipped, in content.db.
    private static bool IsLauncherFolder(string folder) =>
        File.Exists(Path.Combine(folder, "content.db"))
        || new[] { "", "Resources", "Prototypes", Path.Combine("Resources", "Prototypes") }.Any(p => File.Exists(Path.Combine(folder, p, "__merged.yml")));

    /// <summary>Checks a folder, reads its git checkout and finds the fork it holds among <paramref name="forks"/>.</summary>
    public static GameFolderCheck Inspect(string path, IEnumerable<ForkInfo> forks)
    {
        GameFolder folder;
        try
        {
            folder = Open(path);
        }
        catch (GameFolderException e)
        {
            return new GameFolderCheck(null, e.Message, GitCheckout.None, null, []);
        }
        var git = folder.Top is { } top ? ReadGit(top) : GitCheckout.None;
        var (sure, likely) = ForkOf(git, forks.ToList());
        return new GameFolderCheck(folder, null, git, sure, likely);
    }

    /// <summary>
    /// The fork a checkout's remotes name: sure when it is the "origin" remote's (or the only
    /// remote's) repository; otherwise those the other remotes name, as likely.
    /// </summary>
    public static (ForkInfo? Sure, IReadOnlyList<ForkInfo> Likely) ForkOf(GitCheckout git, IReadOnlyList<ForkInfo> forks)
    {
        ForkInfo? Named(string url) => GitHubRepository(url) is { } repository
            ? forks.FirstOrDefault(f => string.Equals(f.Repository, repository, StringComparison.OrdinalIgnoreCase))
            : null;
        var main = git.Remotes.FirstOrDefault(r => r.Name == "origin");
        if (main.Url == null && git.Remotes.Count == 1)
            main = git.Remotes[0];
        if (main.Url != null && Named(main.Url) is { } sure)
            return (sure, []);
        return (null, git.Remotes.Select(r => Named(r.Url)).OfType<ForkInfo>().Distinct().ToList());
    }

    /// <summary>
    /// "owner/repo" from a GitHub address in any of git's forms (https://github.com/owner/repo.git,
    /// git@github.com:owner/repo, ssh://git@github.com/owner/repo), or null for another host.
    /// </summary>
    public static string? GitHubRepository(string url)
    {
        var match = GitHubUrl().Match(url.Trim());
        return match.Success ? $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}" : null;
    }

    [GeneratedRegex(@"^(?:(?:https?|ssh|git)://(?:[^@/]+@)?(?:www\.)?github\.com(?::\d+)?/|(?:[^@/:]+@)?github\.com:/?)(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrl();

    /// <summary>
    /// Reads a checkout's commit and remotes from the files in <c>.git</c>, without running git.
    /// A <c>.git</c> file pointing elsewhere (a worktree) is followed, as git does, even out of
    /// the folder; but no link inside <paramref name="top"/> is, on the way there or within the
    /// git folder. Anything that cannot be read gives no commit or no remotes rather than an error.
    /// </summary>
    /// <param name="gate">The game folder's count of opens given up on; a new one when not given.</param>
    public static GitCheckout ReadGit(string top, OpenGate? gate = null)
    {
        var files = new GitFiles(gate ?? new OpenGate());
        try
        {
            var dotGit = Path.Combine(top, ".git");
            string gitDir;
            if (IsLink(dotGit))
                return new GitCheckout(null, [], ".git is a link, and links in a game folder are not followed");
            if (Directory.Exists(dotGit))
                gitDir = dotGit;
            else if (files.Text(top, [".git"]) is { } pointer && pointer.Trim() is var line && line.StartsWith("gitdir:", StringComparison.Ordinal))
                gitDir = RealPath(Path.Combine(top, line["gitdir:".Length..].Trim()), noLinksIn: top);
            else
                return files.Checkout(null, []);
            if (!Directory.Exists(gitDir))
                return files.Checkout(null, []);
            // A worktree keeps its own HEAD; refs and config are in the repository it belongs to.
            var common = gitDir;
            if (files.Text(gitDir, ["commondir"]) is { } commonDir && RealPath(Path.Combine(gitDir, commonDir.Trim()), noLinksIn: top) is var shared && Directory.Exists(shared))
                common = shared;
            return files.Checkout(ReadHead(files, gitDir, common), ReadRemotes(files, common));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new GitCheckout(null, [], e.Message);
        }
    }

    // Reads .git's files for one checkout, noting why any could not be read.
    private sealed class GitFiles(OpenGate gate)
    {
        private readonly List<string> _problems = [];

        public byte[]? Bytes(string under, IReadOnlyList<string> parts, long max)
        {
            var data = ReadPlainFile(under, parts, max, out var why, gate);
            if (data == null && why != NotThere)
                _problems.Add($"{string.Join('/', parts)}: {why}");
            return data;
        }

        // One of .git's small text files, or null when it cannot be read.
        public string? Text(string under, IReadOnlyList<string> parts) =>
            Bytes(under, parts, MaxSmallFile) is { } data ? Encoding.UTF8.GetString(data) : null;

        public GitCheckout Checkout(string? commit, IReadOnlyList<(string Name, string Url)> remotes) =>
            commit == null && remotes.Count == 0 && _problems.Count == 0
                ? GitCheckout.None
                : new(commit, remotes, _problems.Count == 0 ? null : string.Join("; ", _problems));
    }

    private static string? ReadHead(GitFiles files, string gitDir, string common)
    {
        var head = files.Text(gitDir, ["HEAD"])?.Trim();
        if (head == null)
            return null;
        if (IsCommitId(head))
            return head.ToLowerInvariant();
        if (!head.StartsWith("ref:", StringComparison.Ordinal))
            return null;
        var name = head[4..].Trim();
        if (!IsSafeRef(name))
            return null;
        foreach (var folder in new[] { gitDir, common }.Distinct())
        {
            if (files.Text(folder, name.Split('/'))?.Trim() is { } loose && IsCommitId(loose))
                return loose.ToLowerInvariant();
        }
        // packed-refs can be long in a repository with many tags, but never near the cap.
        if (files.Bytes(common, ["packed-refs"], MaxFileSize) is not { } packed)
            return null;
        foreach (var entry in Encoding.UTF8.GetString(packed).Split('\n'))
        {
            if (entry.StartsWith('#') || entry.StartsWith('^'))
                continue;
            var space = entry.IndexOf(' ');
            if (space > 0 && entry[(space + 1)..].Trim() == name && IsCommitId(entry[..space]))
                return entry[..space].ToLowerInvariant();
        }
        return null;
    }

    // "refs/heads/master", and nothing that could lead out of the git folder.
    private static bool IsSafeRef(string name) =>
        name.StartsWith("refs/", StringComparison.Ordinal) && !name.EndsWith('/')
        && name.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && part.All(c => c > ' ' && c != '\\' && c != ':' && c != 0x7F));

    private static bool IsCommitId(string text) => text.Length is 40 or 64 && text.All(char.IsAsciiHexDigit);

    // The remotes in a git folder's config file, "origin" first.
    private static IReadOnlyList<(string Name, string Url)> ReadRemotes(GitFiles files, string gitDir)
    {
        if (files.Text(gitDir, ["config"]) is not { } text)
            return [];
        var remotes = new List<(string Name, string Url)>();
        string? remote = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var section = RemoteSection().Match(line);
                remote = section.Success ? section.Groups[1].Value : null;
                continue;
            }
            var equals = line.IndexOf('=');
            if (remote == null || equals < 0 || !line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line[(equals + 1)..].Trim();
            if (value.StartsWith('"'))
                value = value[1..(value.IndexOf('"', 1) is var end and > 0 ? end : value.Length)];
            else if (value.IndexOfAny(['#', ';']) is var comment and >= 0)
                value = value[..comment].Trim();
            if (value.Length > 0)
                remotes.Add((remote, value));
        }
        return remotes.OrderBy(r => r.Name == "origin" ? 0 : 1).ToList();
    }

    [GeneratedRegex(@"^\[\s*remote\s+""([^""]*)""\s*\]")]
    private static partial Regex RemoteSection();

    /// <summary>What <see cref="ReadPlainFile"/> gives as the reason for a file that is not there.</summary>
    public const string NotThere = "it is not there";

    /// <summary>Whether a path is a link (or, on Windows, a junction).</summary>
    public static bool IsLink(string path) => new FileInfo(path).LinkTarget != null;

    /// <summary>
    /// The bytes of a plain file at <paramref name="parts"/> below <paramref name="under"/>.
    /// Refused, as null with <paramref name="why"/>, when any part of the path below
    /// <paramref name="under"/> is a link, when it is not a plain file, when it is larger than
    /// <paramref name="max"/>, or when it does not open within <see cref="OpenWait"/>. A file
    /// that reads as empty is never opened, so a pipe or device cannot keep the read waiting or
    /// running without end.
    /// </summary>
    /// <remarks>
    /// The checks come before the open, as .NET has no open that refuses links. A part swapped for
    /// a link in between is followed by that one open; what it reads is still a plain file of the
    /// size checked, and an open that waits, as on a pipe swapped in, is given up on.
    /// </remarks>
    /// <param name="gate">The game folder's count of opens given up on.</param>
    public static byte[]? ReadPlainFile(string under, IReadOnlyList<string> parts, long max, out string why, OpenGate gate, CancellationToken ct = default)
    {
        try
        {
            var path = under;
            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
                if (IsLink(path))
                {
                    why = "it is reached through a link, and links in a game folder are not followed";
                    return null;
                }
            }
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                why = NotThere;
                return null;
            }
            if (info.Length == 0)
            {
                why = "";
                return [];
            }
            if (info.Length > max)
            {
                why = $"it is larger than {max / (1024 * 1024)} MB";
                return null;
            }
            using var stream = OpenPlain(path, info.Length, OpenWait, ct, gate, out why);
            if (stream == null)
                return null;
            var buffer = new byte[info.Length];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read == buffer.Length ? buffer : buffer[..read];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>
    /// How long opening a file may take before it is given up on: long enough for a slow or
    /// network drive, where a plain file opens at once.
    /// </summary>
    public static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Opens a file for reading, giving up after <paramref name="wait"/> or when
    /// <paramref name="ct"/> is cancelled, and refuses what opened unless it is a plain file of
    /// <paramref name="length"/> bytes. Null with <paramref name="why"/> when refused. An open
    /// given up on keeps waiting on a thread of its own, counted in <paramref name="gate"/>, and
    /// the file is closed should it ever open; while the gate is full, nothing more is opened.
    /// </summary>
    public static FileStream? OpenPlain(string path, long length, TimeSpan wait, CancellationToken ct, OpenGate gate, out string why)
    {
        ct.ThrowIfCancellationRequested();
        if (gate.Waiting >= OpenGate.MaxWaiting)
        {
            why = $"{gate.FirstStuck} and other files in the game folder did not open, as a pipe would not. Fix or remove them, then Update the fork; if this goes on, restart Paperdoll";
            return null;
        }
        var opening = Task.Run(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan), CancellationToken.None);
        bool opened;
        try
        {
            opened = opening.Wait(wait, ct);
        }
        catch (OperationCanceledException)
        {
            GiveUp(opening, path, gate);
            throw;
        }
        catch (AggregateException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
        if (!opened)
        {
            GiveUp(opening, path, gate);
            why = $"it did not open within {wait.TotalSeconds:0.#} seconds; on a slow or network drive, Update the fork to try again";
            return null;
        }
        var stream = opening.Result;
        if (!stream.CanSeek || stream.Length != length)
        {
            why = stream.CanSeek ? "it changed while it was being read; read the folder again" : "it is not a plain file";
            stream.Dispose();
            return null;
        }
        why = "";
        return stream;
    }

    // Leaves an open that is still waiting, closing the file should it ever open; a failure
    // when it ends is seen here, not left unobserved.
    private static void GiveUp(Task<FileStream> opening, string path, OpenGate gate)
    {
        gate.Stuck(path);
        opening.ContinueWith(done =>
        {
            if (done.IsCompletedSuccessfully)
                done.Result.Dispose();
            else
                _ = done.Exception;
            gate.Done();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Whether a path is <paramref name="folder"/> or inside it, both with their links already
    /// followed, compared as the platform's file system compares names: ignoring case on Windows
    /// and macOS.
    /// </summary>
    public static bool IsWithin(string path, string folder, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        path = Path.TrimEndingDirectorySeparator(path);
        folder = Path.TrimEndingDirectorySeparator(folder);
        return path.Equals(folder, comparison) || path.StartsWith(folder + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IgnoresCase => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// The path with every link in it followed, as the system would to open it: each link is
    /// read where it is met and its target, absolute or relative, takes the place of the part,
    /// so a ".." after a link goes up from where the link leads. Throws
    /// <see cref="IOException"/> for links that go round in a loop, and for a link met inside
    /// <paramref name="noLinksIn"/>, when given.
    /// </summary>
    public static string RealPath(string path, string? noLinksIn = null)
    {
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
        var current = Path.GetPathRoot(path)!;
        // The parts still to walk, the next on top.
        var pending = new Stack<string>(Parts(path[current.Length..]).Reverse());
        var hops = 0;
        while (pending.TryPop(out var part))
        {
            if (part is "" or ".")
                continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }
            var next = Path.Combine(current, part);
            if (new FileInfo(next).LinkTarget is not { } target)
            {
                current = next;
                continue;
            }
            // The folder holding the link, its own links followed, decides where the link is.
            if (noLinksIn != null && IsWithin(current, noLinksIn, IgnoresCase))
                throw new IOException($"{path} passes through a link inside the game folder.");
            if (++hops > 40)
                throw new IOException($"{path} goes round a loop of links.");
            if (Path.IsPathRooted(target))
            {
                current = Path.GetPathRoot(target)!;
                target = target[current.Length..];
            }
            foreach (var targetPart in Parts(target).Reverse())
                pending.Push(targetPart);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static IEnumerable<string> Parts(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
}
