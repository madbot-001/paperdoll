using System.Diagnostics;
using System.Text;

namespace Paperdoll.Core.Store;

/// <summary>A failed git command, with what git printed to stderr.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>Runs the git program against one repository.</summary>
internal sealed class GitCommand(string gitPath, string repository)
{
    public string GitPath { get; } = gitPath;
    public string Repository { get; } = repository;

    public ProcessStartInfo StartInfo(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(GitPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(Repository);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        // Never fetch a missing file behind our back, one request per file: reads report
        // "missing" instead and FetchAsync gets them all in one request.
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        // Never stop to ask for a password.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return info;
    }

    public async Task<byte[]> RunAsync(IEnumerable<string> args, string? input = null, CancellationToken ct = default)
    {
        var argList = args.ToList();
        using var process = Process.Start(StartInfo(argList))
            ?? throw new GitException($"Could not start {GitPath}.");

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
