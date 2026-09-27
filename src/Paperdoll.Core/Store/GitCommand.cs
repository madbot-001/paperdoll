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
        foreach (var setting in (string[])["credential.helper=", "core.askPass=", "http.lowSpeedLimit=1000", "http.lowSpeedTime=60"])
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
        // A setting that sends GitHub through ssh must not ask either.
        info.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
        return info;
    }

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
        try
        {
            return await RunOnceAsync(argList, input, ct);
        }
        // git killed part way (a crash, the computer switched off) leaves its lock files behind,
        // and every later command that needs them refuses. Old ones are removed and git asked again.
        catch (GitException e) when (LockedOut().IsMatch(e.Message) && RemoveStaleLocks() > 0)
        {
            return await RunOnceAsync(argList, input, ct);
        }
    }

    // Lock files older than this cannot belong to a git still running for Paperdoll.
    private static readonly TimeSpan StaleLock = TimeSpan.FromMinutes(2);

    private int RemoveStaleLocks()
    {
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(Repository, "*.lock", SearchOption.AllDirectories))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < StaleLock)
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

    [System.Text.RegularExpressions.GeneratedRegex(@"\.lock'?: File exists|Unable to create '[^']*\.lock'|could not lock config file", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
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
