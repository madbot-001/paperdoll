using System.Diagnostics;
using System.Text;

namespace Paperdoll.Core.Store;

/// <summary>A failed git command, with what git printed to stderr.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>Runs the git program against one repository.</summary>
internal sealed partial class GitCommand(string gitPath, string repository)
{
    public string GitPath { get; } = gitPath;
    public string Repository { get; } = repository;

    public ProcessStartInfo StartInfo(IEnumerable<string> args)
    {
        var info = NewStartInfo(GitPath);
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(Repository);
        // Forks are public: nothing needs a password, and a repository that is gone or private
        // must fail at once rather than wait on a sign-in window nobody sees. So no credential
        // helper and no password programs; a transfer that stalls (under 1 KB/s for a minute)
        // gives up.
        // Nor does git start maintenance of its own in the background, which would hold locks
        // after the command ends: Paperdoll cleans up itself.
        foreach (var setting in (string[])["credential.helper=", "core.askPass=", "http.lowSpeedLimit=1000", "http.lowSpeedTime=60",
                     "gc.auto=0", "maintenance.auto=false"])
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(setting);
        }
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        // Never fetch a missing file behind our back, one request per file: reads report
        // "missing" instead and FetchAsync gets them all in one request.
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_ASKPASS"] = "";
        info.Environment["SSH_ASKPASS"] = "";
        info.Environment["GCM_INTERACTIVE"] = "never";
        // A setting that sends GitHub through ssh must not ask or stall either, unless the user
        // runs git with an ssh of their own choosing (which this would replace).
        if (Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") == null && Environment.GetEnvironmentVariable("GIT_SSH") == null
            && !HasOwnSshCommand(GitPath))
            info.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=15";
        return info;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> OwnSshCommand = new();

    // Whether the user's git settings name an ssh command (core.sshCommand), asked once.
    private static bool HasOwnSshCommand(string gitPath) => OwnSshCommand.GetOrAdd(gitPath, path =>
    {
        try
        {
            var info = NewStartInfo(path);
            info.ArgumentList.Add("config");
            info.ArgumentList.Add("--get");
            info.ArgumentList.Add("core.sshCommand");
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(info);
            if (process == null)
                return false;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return false;
            }
            return output.Result.Trim().Length > 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    });

    /// <summary>A git process with its output captured and, on Windows, no console window of its own.</summary>
    public static ProcessStartInfo NewStartInfo(string gitPath) => new(gitPath)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    public async Task<byte[]> RunAsync(IEnumerable<string> args, string? input = null, CancellationToken ct = default)
    {
        var argList = args.ToList();
        var started = DateTime.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RunOnceAsync(argList, input, ct);
            }
            // Stopped part way, git leaves its lock files behind, and the next command refuses. The
            // ones it made are removed.
            catch (OperationCanceledException)
            {
                RemoveLocks(file => File.GetLastWriteTimeUtc(file) >= started - TimeSpan.FromSeconds(2));
                throw;
            }
            // git killed part way (a crash, the computer switched off) leaves its lock files
            // behind, and every later command that needs them refuses. The one git names is
            // removed if old, and git asked once more.
            catch (GitException e) when (attempt == 1 && LockedOut().Match(e.Message) is { Success: true } locked
                && (locked.Groups["path"].Success
                    ? RemoveStaleLock(LockOf(locked.Groups["path"].Value))
                    : RemoveLocks(file => file.EndsWith("config.lock", StringComparison.Ordinal) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= StaleLock) > 0))
            {
            }
        }
    }

    // Lock files older than this cannot belong to a git still running for Paperdoll.
    private static readonly TimeSpan StaleLock = TimeSpan.FromMinutes(2);

    private int RemoveLocks(Func<string, bool> which)
    {
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(Repository, "*.lock", SearchOption.AllDirectories))
        {
            try
            {
                if (!which(file))
                    continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use after all; left alone.
            }
        }
        return removed;
    }

    // git names the lock ("shallow.lock") or, for the config, the file it guards ("config").
    private static string LockOf(string named) => named.EndsWith(".lock", StringComparison.Ordinal) ? named : named + ".lock";

    // Removes the lock git named if it is old. git names the path as it resolved it, which differs
    // from Paperdoll's when a folder on the way is a link, so the lock is found by its path inside
    // the store: the longest that git's path ends with.
    private bool RemoveStaleLock(string named)
    {
        named = "/" + named.Replace('\\', '/').TrimStart('/');
        var casing = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var file = Directory.EnumerateFiles(Repository, "*.lock", SearchOption.AllDirectories)
            .Where(f => named.EndsWith("/" + Path.GetRelativePath(Repository, f).Replace('\\', '/'), casing))
            .MaxBy(f => f.Length);
        if (file == null || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < StaleLock)
            return false;
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // "Unable to create '<path>.lock': File exists", or "could not lock config file <path>: File exists".
    [System.Text.RegularExpressions.GeneratedRegex(@"Unable to create '(?<path>[^']*\.lock)'|could not lock config file (?<path>\S+?)(?=: File exists)|\.lock'?: File exists", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LockedOut();

    private async Task<byte[]> RunOnceAsync(List<string> argList, string? input, CancellationToken ct)
    {
        using var process = Process.Start(StartInfo(argList))
            ?? throw new GitException($"Could not start {GitPath}.");
        // Cancelling stops git itself, not only the wait for it.
        using var stop = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }
        });

        var stdout = new MemoryStream();
        var copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout, ct);
        var readErr = process.StandardError.ReadToEndAsync(ct);

        if (input != null)
            await process.StandardInput.WriteAsync(input.AsMemory(), ct);
        process.StandardInput.Close();

        await copyOut;
        var stderr = await readErr;
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new GitException($"git {string.Join(' ', argList)} failed ({process.ExitCode}): {stderr.Trim()}");

        return stdout.ToArray();
    }

    public async Task<string> RunTextAsync(IEnumerable<string> args, string? input = null, CancellationToken ct = default) =>
        Encoding.UTF8.GetString(await RunAsync(args, input, ct));
}
