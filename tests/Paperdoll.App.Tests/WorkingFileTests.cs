namespace Paperdoll.App.Tests;

public class WorkingFileTests
{
    private const string Saved = "profile:\n  name: Test Person\n";
    private const string Edited = "profile:\n  name: Test Person Two\n";
    private const string Other = "profile:\n  name: Someone Else\n";

    [Fact]
    public void The_working_copy_goes_back_to_its_file_only_while_the_file_is_as_left()
    {
        var hash = MainWindow.WorkingFileMatches(Saved, null, Saved) ? MainWindow.Fingerprint(Saved) : null;

        // Edited since the last save: still the same file.
        Assert.True(MainWindow.WorkingFileMatches(Saved, hash, Edited));
        // Another character saved there since, or the game exporting again: not ours to overwrite.
        Assert.False(MainWindow.WorkingFileMatches(Other, hash, Edited));
        // Settings from before fingerprints: only when the file and the working copy agree.
        Assert.False(MainWindow.WorkingFileMatches(Saved, null, Edited));
        Assert.True(MainWindow.WorkingFileMatches(Edited, null, Edited));
    }
}
