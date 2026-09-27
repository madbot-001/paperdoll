namespace Paperdoll.App;

/// <summary>
/// Keeps Paperdoll to one copy at a time: two would share the working copy and settings, and
/// each would overwrite what the other saved there.
/// </summary>
public static class InstanceLock
{
    // Held open, unshared, for as long as Paperdoll runs; the system lets go when it ends, even
    // after a crash.
    private static FileStream? _held;

    /// <summary>False when another Paperdoll already holds the lock.</summary>
    public static bool TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(MainWindow.DataDirectory);
            _held = new FileStream(Path.Combine(MainWindow.DataDirectory, "paperdoll.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // A folder it cannot write in: better to run than to refuse.
            return true;
        }
    }
}
