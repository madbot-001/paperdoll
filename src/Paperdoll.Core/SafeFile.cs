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
        // A file the user may not write stays refused, as writing into it would be; replacing it
        // would otherwise get around that on Linux and macOS.
        if (File.Exists(path))
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
            }
        }

        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Left behind; it is only a temporary copy.
            }
            throw;
        }
    }
}
