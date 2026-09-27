namespace Paperdoll.App;

public partial class MainWindow
{
    /// <summary>
    /// An error nothing else caught: logged, the working copy saved, and the user told, and
    /// Paperdoll keeps running. The preview's animation stops, so an error there does not repeat.
    /// </summary>
    public void OnUnexpectedError(Exception error)
    {
        var log = ErrorLog.Write(error);
        _animation?.Stop();
        _refreshing = false;
        _keepInspector = false;
        WriteAutosave();
        SetStatus($"Something went wrong: {error.Message} Your working copy is saved"
            + (log != null ? $"; the details are in {log}." : "."));
    }
}
