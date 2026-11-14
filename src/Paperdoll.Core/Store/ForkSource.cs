namespace Paperdoll.Core.Store;

/// <summary>Where a fork's files come from.</summary>
public enum ForkSourceKind
{
    /// <summary>Downloaded from the fork's repository into Paperdoll's store.</summary>
    Download = 0,

    /// <summary>Read, as they are on disk, from a copy or git clone of the game on this computer.</summary>
    Folder = 1,
}

/// <summary>Where one fork's files come from, as kept in the settings.</summary>
public sealed record ForkSource
{
    public static readonly ForkSource Download = new();

    public ForkSourceKind Kind { get; init; }

    /// <summary>The game folder, for <see cref="ForkSourceKind.Folder"/>.</summary>
    public string? Folder { get; init; }

    public static ForkSource FromFolder(string folder) => new() { Kind = ForkSourceKind.Folder, Folder = folder };
}

/// <summary>
/// The versions a store gives a fork. A downloaded fork's is its commit id. A game folder's is
/// "folder:", the commit the folder's git checkout is on (empty without one), ":" and when the
/// folder was last read, so what is kept for one reading is not taken for the next.
/// </summary>
public static class ForkVersions
{
    private const string FolderPrefix = "folder:";

    public static string OfFolder(string? commit, long readAt) => $"{FolderPrefix}{commit}:{readAt}";

    public static bool IsFolder(string version) => version.StartsWith(FolderPrefix, StringComparison.Ordinal);

    /// <summary>The commit id, or null for a game folder that is not a git checkout.</summary>
    public static string? Commit(string version)
    {
        if (!IsFolder(version))
            return version;
        var rest = version[FolderPrefix.Length..];
        var colon = rest.IndexOf(':');
        var commit = colon < 0 ? rest : rest[..colon];
        return commit.Length > 0 ? commit : null;
    }

    /// <summary>
    /// The version as shown: the commit's first 8 characters; for a game folder, marked as such,
    /// since its files may differ from that commit, or just "folder" without one.
    /// </summary>
    public static string Short(string version)
    {
        var commit = Commit(version);
        var shortCommit = commit?[..Math.Min(8, commit.Length)];
        return IsFolder(version) ? shortCommit == null ? "folder" : $"{shortCommit} (folder)" : shortCommit!;
    }
}
