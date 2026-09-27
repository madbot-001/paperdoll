using System.Text;
using Avalonia.Platform.Storage;

namespace Paperdoll.App;

/// <summary>Writing to files the user picked in a save dialog.</summary>
public static class PickedFile
{
    public static Task WriteAsync(IStorageFile file, string text) => WriteAsync(file, new UTF8Encoding(false).GetBytes(text));

    /// <summary>
    /// In one safe step when the file is on this computer, so a failure never leaves it half
    /// written; through the dialog's stream otherwise (storage the system provides, such as a
    /// phone's).
    /// </summary>
    public static async Task WriteAsync(IStorageFile file, byte[] data)
    {
        if (file.TryGetLocalPath() is { } path)
        {
            await Task.Run(() => Core.SafeFile.WriteAllBytes(path, data));
            return;
        }
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(data);
    }
}
