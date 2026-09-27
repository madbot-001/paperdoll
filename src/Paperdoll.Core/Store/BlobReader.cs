using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Paperdoll.Core.Store;

/// <summary>
/// Reads files from the store through one long-running <c>git cat-file --batch</c>.
/// Safe to share between threads; reads are served one at a time.
/// </summary>
internal sealed class BlobReader : IBlobReader
{
    private readonly Process _process;
    private readonly Stream _output;
    private readonly SemaphoreSlim _lock = new(1, 1);

    internal BlobReader(GitCommand git)
    {
        _process = Process.Start(git.StartInfo(["cat-file", "--batch"]))
            ?? throw new GitException($"Could not start {git.GitPath}.");
        _output = _process.StandardOutput.BaseStream;
        // Drain stderr so git never blocks on a full pipe.
        _ = _process.StandardError.ReadToEndAsync();
    }

    public async Task<byte[]?> ReadAsync(string objectId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(objectId.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);

            // "<oid> <type> <size>" or "<oid> missing"
            var header = (await ReadLineAsync(ct)).Split(' ');
            if (header.Length < 3)
                return null;

            var size = int.Parse(header[2], CultureInfo.InvariantCulture);
            var data = new byte[size];
            await _output.ReadExactlyAsync(data, ct);
            await ReadLineAsync(ct); // the newline after the contents
            return data;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await _output.ReadAsync(one, ct) == 0)
                throw new GitException("git cat-file stopped unexpectedly.");
            if (one[0] == (byte)'\n')
                return Encoding.UTF8.GetString(line.ToArray());
            line.Add(one[0]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _process.StandardInput.Close();
        await _process.WaitForExitAsync();
        _process.Dispose();
        _lock.Dispose();
    }
}
