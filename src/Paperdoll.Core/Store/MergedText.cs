// The merged-file format read here is the one RobustToolbox writes
// (Robust.Packaging/AssetProcessing/Passes/AssetPassMergeTextDirectories.cs and RobustClientAssetGraph.cs),
// MIT licensed: Copyright (c) 2019 Space Station 14 Contributors. See THIRD-PARTY-NOTICES.md.

using System.Text;

namespace Paperdoll.Core.Store;

/// <summary>
/// Splits a <c>__merged.yml</c> or <c>__merged.ftl</c> the SS14 launcher holds back into the files
/// it was made of. The game's packager merges the files of each folder, not those below it, in
/// ordinal order, each between a line <c>--- # BEGIN path</c> (<c># BEGIN path</c> for .ftl) and a
/// line <c># END path</c>, its own lines each ended by a newline. Every path must name a file in
/// the merged file's own folder, so nothing is written anywhere else.
/// </summary>
public static class MergedText
{
    /// <summary>The name merged files have, by extension ("yml" or "ftl").</summary>
    public static string MergedName(string extension) => $"__merged.{extension}";

    /// <summary>
    /// The files a merged file holds, as paths below Resources and their bytes. A merged file
    /// with no BEGIN line at all is given back whole, as merged.yml or merged.ftl in its folder.
    /// Throws <see cref="FormatException"/> for a file not laid out as the packager lays it out.
    /// </summary>
    /// <param name="folder">The merged file's folder below Resources, such as "Prototypes/Entities".</param>
    /// <param name="extension">"yml" or "ftl".</param>
    public static IReadOnlyList<(string Path, byte[] Data)> Split(string folder, string extension, byte[] merged)
    {
        var head = extension == "yml" ? "--- # BEGIN " : "# BEGIN ";
        const string tail = "# END ";
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(merged);
        }
        catch (DecoderFallbackException)
        {
            throw new FormatException("it is not UTF-8 text");
        }
        var lines = text.Split('\n');
        // The packager ends every line with a newline, so the last part is empty.
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        if (!lines.Take(count).Any(l => l.StartsWith(head, StringComparison.Ordinal)))
            return [($"{folder}/merged.{extension}", merged)];

        var files = new List<(string, byte[])>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var at = 0;
        while (at < count)
        {
            if (!lines[at].StartsWith(head, StringComparison.Ordinal))
                throw new FormatException($"line {at + 1} is outside every file it holds");
            var path = lines[at][head.Length..];
            CheckInFolder(path, folder, extension);
            if (!names.Add(path))
                throw new FormatException($"it holds {LauncherCache.Shown(path)} twice");
            var end = tail + path;
            var body = new StringBuilder();
            at++;
            // A file's own line that reads as its END is taken as its end only where the next
            // file begins, or the merged file ends.
            while (true)
            {
                if (at >= count)
                    throw new FormatException($"it ends inside {LauncherCache.Shown(path)}, with no END line");
                if (lines[at] == end && (at + 1 == count || lines[at + 1].StartsWith(head, StringComparison.Ordinal)))
                    break;
                body.Append(lines[at]).Append('\n');
                at++;
            }
            at++;
            files.Add((path, Encoding.UTF8.GetBytes(body.ToString())));
        }
        return files;
    }

    // A path in a BEGIN line must name a file of this extension in the merged file's own folder.
    private static void CheckInFolder(string path, string folder, string extension)
    {
        var name = path.StartsWith(folder + "/", StringComparison.Ordinal) ? path[(folder.Length + 1)..] : null;
        if (name == null || name.Contains('/') || !name.EndsWith("." + extension, StringComparison.Ordinal) || !LauncherPaths.IsPlainPart(name))
            throw new FormatException($"it names {LauncherCache.Shown(path)}, which is not a .{extension} file in {folder}");
    }
}
