using System.Text;

namespace Paperdoll.Core.Tests;

/// <summary>Made-up game folders on disk, laid out as a copy or git clone of a fork's repository.</summary>
public static class TestGameFolders
{
    /// <summary>A commit id as long as a real one, made up.</summary>
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    /// Writes a made-up fork's files (from <see cref="TestForks.Store"/>, fetched or not) into
    /// <paramref name="folder"/> as they would be in a clone. Returns the folder.
    /// </summary>
    public static string Write(string folder, string forkId)
    {
        foreach (var (path, data) in TestForks.Store().FilesOf(forkId))
        {
            var file = Path.Combine([folder, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        }
        // A clone always has both, whatever the made-up fork holds, beside the game's code.
        Directory.CreateDirectory(Path.Combine(folder, "Resources", "Prototypes"));
        Directory.CreateDirectory(Path.Combine(folder, "Resources", "Textures"));
        Directory.CreateDirectory(Path.Combine(folder, "Content.Client"));
        return folder;
    }

    /// <summary>How a made-up checkout keeps the commit its branch is on.</summary>
    public enum Ref
    {
        Loose,
        Packed,
        Detached,
    }

    /// <summary>Writes a made-up .git folder: HEAD on master at <see cref="Commit"/>, and an "origin" remote when given.</summary>
    public static void Git(string folder, string? origin, Ref kind = Ref.Loose, string? other = null)
    {
        var git = Directory.CreateDirectory(Path.Combine(folder, ".git")).FullName;
        File.WriteAllText(Path.Combine(git, "HEAD"), kind == Ref.Detached ? Commit + "\n" : "ref: refs/heads/master\n");
        if (kind == Ref.Loose)
        {
            Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
            File.WriteAllText(Path.Combine(git, "refs", "heads", "master"), Commit + "\n");
        }
        else if (kind == Ref.Packed)
        {
            File.WriteAllText(Path.Combine(git, "packed-refs"),
                $"# pack-refs with: peeled fully-peeled sorted \nfedcba9876543210fedcba9876543210fedcba98 refs/heads/other\n{Commit} refs/heads/master\n^aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        }
        var config = new StringBuilder("[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
        if (other != null)
            config.Append($"[remote \"upstream\"]\n\turl = {other}\n\tfetch = +refs/heads/*:refs/remotes/upstream/*\n");
        if (origin != null)
            config.Append($"[remote \"origin\"]\n\turl = {origin}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
        config.Append("[branch \"master\"]\n\tremote = origin\n\tmerge = refs/heads/master\n");
        File.WriteAllText(Path.Combine(git, "config"), config.ToString());
    }

    /// <summary>
    /// Every file, folder and link under a folder, with its size, when it last changed and where
    /// a link leads, links not followed: to show nothing in it was written.
    /// </summary>
    public static IReadOnlyList<string> Snapshot(string folder)
    {
        var lines = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        while (pending.TryPop(out var at))
        {
            lines.Add($"{Path.GetRelativePath(folder, at.FullName)}/ {at.LastWriteTimeUtc.Ticks}");
            foreach (var item in at.EnumerateFileSystemInfos())
            {
                if (item.LinkTarget is { } target)
                    lines.Add($"{Path.GetRelativePath(folder, item.FullName)} -> {target}");
                else if (item is DirectoryInfo directory)
                    pending.Push(directory);
                else
                    lines.Add($"{Path.GetRelativePath(folder, item.FullName)} {((FileInfo)item).Length} {item.LastWriteTimeUtc.Ticks}");
            }
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
}
