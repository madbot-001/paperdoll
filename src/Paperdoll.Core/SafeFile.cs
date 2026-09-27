using System.Text;

namespace Paperdoll.Core;

/// <summary>
/// Writes files so that a crash, a full disk or a power cut never leaves one half written or
/// empty: the text goes to a temporary file beside it, which then takes its place in one step.
/// </summary>
public static class SafeFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string text) => WriteAllBytes(path, Utf8.GetBytes(text));

    public static void WriteAllBytes(string path, byte[] data)
    {
        path = Path.GetFullPath(path);
        // A link is written through to the file it points at, and stays a link.
        var info = new FileInfo(path);
        if (info.LinkTarget != null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            path = target.FullName;
        var exists = File.Exists(path);
        // A file the user may not write stays refused, as writing into it would be; replacing it
        // would otherwise get around that on Linux and macOS.
        if (exists)
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
            }
        }

        // Short, so that a file with a long name still has room for it.
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".paperdoll-{Guid.NewGuid():N}"[..20] + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // No room beside it (a folder the user may write files in but not add to, or one the
            // system guards): write it in place, as before, rather than not at all.
            Delete(temp);
            WriteInPlace(path, data);
            return;
        }

        try
        {
            if (exists && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            Swap(temp, path, exists);
        }
        catch
        {
            Delete(temp);
            throw;
        }
    }

    // Replacing keeps the old file's permissions and attributes on Windows; elsewhere the mode
    // was copied above. Another program (a virus scanner, a sync client) holding the file for a
    // moment gets a few more tries.
    private static void Swap(string temp, string path, bool exists)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (exists && OperatingSystem.IsWindows())
                    File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    private static void WriteInPlace(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private static void Delete(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is only a temporary copy.
        }
    }
}
