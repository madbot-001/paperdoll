namespace Paperdoll.Core.Tests;

public sealed class SafeFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-safefile-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Writes_a_new_file_and_replaces_an_old_one_leaving_nothing_else()
    {
        var path = Path.Combine(_root, "Test Person.yml");

        SafeFile.WriteAllText(path, "first");
        SafeFile.WriteAllText(path, "second, longer");

        Assert.Equal("second, longer", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void A_read_only_file_is_refused_and_left_as_it_was()
    {
        var path = Path.Combine(_root, "locked.yml");
        File.WriteAllText(path, "kept");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.ThrowsAny<UnauthorizedAccessException>(() => SafeFile.WriteAllText(path, "lost"));

        Assert.Equal("kept", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void A_private_file_stays_private_and_a_link_stays_a_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes and links.");
        var real = Path.Combine(_root, "real.yml");
        var link = Path.Combine(_root, "link.yml");
        File.WriteAllText(real, "old");
        File.SetUnixFileMode(real, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.CreateSymbolicLink(link, real);

        SafeFile.WriteAllText(link, "new");

        Assert.Equal("new", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(real));
    }

    [Fact]
    public void A_file_with_a_long_name_can_be_saved()
    {
        var path = Path.Combine(_root, new string('n', 240) + ".yml");

        SafeFile.WriteAllText(path, "text");

        Assert.Equal("text", File.ReadAllText(path));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void A_file_in_a_folder_that_takes_no_new_files_is_written_in_place()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix folder modes.");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "closed")).FullName;
        var path = Path.Combine(folder, "Test Person.yml");
        File.WriteAllText(path, "old");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            SafeFile.WriteAllText(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
