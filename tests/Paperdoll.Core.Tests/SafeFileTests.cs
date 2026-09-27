namespace Paperdoll.Core.Tests;

public sealed class SafeFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-safefile-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root))
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
}
