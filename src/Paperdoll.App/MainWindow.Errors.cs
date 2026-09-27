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
        var kept = WriteAutosave() switch
        {
            Autosaved.Written => " Your working copy is saved.",
            Autosaved.Failed => " Your working copy could not be saved: use Save or Export.",
            _ => "",
        };
        SetStatus($"Something went wrong: {error.Message}{kept}" + (log != null ? $" The details are in {log}." : ""));
    }
}
