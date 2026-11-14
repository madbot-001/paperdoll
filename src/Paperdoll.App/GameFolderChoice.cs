using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.App;

/// <summary>Choosing a game folder for a fork: the folder, then the fork it holds.</summary>
internal static class GameFolderChoice
{
    /// <summary>Asks for a folder with the system's folder picker; null when none was chosen.</summary>
    public static async Task<string?> PickAsync(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the game folder (the one holding Resources)",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Asks for a folder, checks it and finds the fork it holds from its git remote, asking when
    /// it does not say. Returns the fork and folder, for the caller to make its source; null when
    /// cancelled or refused, with the reason given to <paramref name="report"/>.
    /// </summary>
    /// <param name="suggested">The fork to offer first when the folder does not say.</param>
    public static async Task<(ForkInfo Fork, string Folder)?> ChooseAsync(Window owner, Func<Window, Task<string?>> pick, ForkInfo? suggested, Action<string> report)
    {
        var path = await pick(owner);
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var editable = KnownForks.All.Where(f => new ForkStatus(f, null).Editable).ToList();
        var check = await Task.Run(() => GameFolder.Inspect(path, KnownForks.All));
        if (check.Problem != null)
        {
            report(check.Problem);
            return null;
        }
        var fork = check.Fork;
        if (fork == null)
        {
            var first = check.Likely.FirstOrDefault(f => editable.Contains(f)) ?? (suggested != null && editable.Contains(suggested) ? suggested : null);
            fork = await new ChooseForkWindow(path, editable, first).ShowDialog<ForkInfo?>(owner);
            if (fork == null)
                return null;
        }
        else if (!editable.Contains(fork))
        {
            report($"{path} holds {fork.Name}, which Paperdoll cannot edit yet.");
            return null;
        }
        return (fork, path);
    }
}
