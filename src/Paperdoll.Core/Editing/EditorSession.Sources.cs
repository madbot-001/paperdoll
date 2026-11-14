using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Editing;

/// <summary>Where each fork's files come from: downloaded, a game folder on this computer, or a copy of an SS14 launcher build.</summary>
public sealed partial class EditorSession
{
    private SourcedForkStore? Sourced => Store as SourcedForkStore;

    private SourcedForkStore RequireSourced() =>
        Sourced ?? throw new InvalidOperationException("This session's store reads every fork from its downloads.");

    public ForkSource SourceOf(string forkId) => Sourced?.SourceOf(forkId) ?? ForkSource.Download;

    /// <summary>Every fork that is not downloaded, with where it comes from, to keep in the settings.</summary>
    public IReadOnlyDictionary<string, ForkSource> Sources() => Sourced?.Sources ?? new Dictionary<string, ForkSource>();

    /// <summary>Why a fork's game folder or launcher copy could not be read when last tried, or null.</summary>
    public string? FolderProblem(string forkId) => Sourced?.FolderOf(forkId)?.Problem ?? Sourced?.LauncherOf(forkId)?.Problem;

    /// <summary>What was left out of a fork's game folder or launcher copy when it was last read: links leading out of it, folders that cannot be listed.</summary>
    public IReadOnlyList<string> FolderSkipped(string forkId) => (Sourced?.FolderOf(forkId) ?? Sourced?.LauncherOf(forkId)?.Files)?.Skipped ?? [];

    /// <summary>Takes the sources kept from an earlier run, before any fork is loaded.</summary>
    public async Task RestoreSourcesAsync(IReadOnlyDictionary<string, ForkSource> sources, CancellationToken ct = default)
    {
        if (Sourced is not { } sourced)
            return;
        await TakeStoreTurnAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                foreach (var (forkId, source) in sources)
                    sourced.Restore(forkId, source);
            }, ct);
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    /// <summary>
    /// Reads a fork from a game folder from now on. Throws <see cref="GameFolderException"/> when
    /// the folder cannot be used, or another fork already uses it; one folder serves one fork.
    /// </summary>
    public async Task UseFolderAsync(ForkInfo fork, string folder, CancellationToken ct = default)
    {
        var sourced = RequireSourced();
        await TakeStoreTurnAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                CheckFolderFree(sourced, fork, folder);
                var before = sourced.FolderOf(fork.Id) == null ? sourced.LauncherOf(fork.Id) : null;
                sourced.UseFolder(fork.Id, folder);
                if (before != null)
                    DeleteCopy(sourced, fork);
            }, ct);
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    // Deletes Paperdoll's copy of a launcher build that the fork no longer reads. One that cannot
    // be deleted now is left: it is replaced by the next copy, or deleted with the fork.
    private static void DeleteCopy(SourcedForkStore sourced, ForkInfo fork)
    {
        try
        {
            sourced.LauncherCopies?.Delete(fork.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Throws when the folder is not a game folder, or is already another fork's.
    private static void CheckFolderFree(SourcedForkStore sourced, ForkInfo fork, string folder)
    {
        var chosen = GameFolder.Open(folder);
        foreach (var (otherId, other) in sourced.Sources)
        {
            if (otherId == fork.Id || other.Folder == null)
                continue;
            GameFolder? used;
            try
            {
                used = GameFolder.Open(other.Folder);
            }
            catch (GameFolderException)
            {
                continue;
            }
            if (used.Resources == chosen.Resources)
                throw new GameFolderException($"{folder} is already the game folder of {KnownForks.Find(otherId)?.Name ?? otherId}; one folder serves one fork.");
        }
    }

    /// <summary>
    /// Gives a fork a new source and reads it from there, without changing the session, as
    /// <see cref="PrepareForkAsync"/> does. If it cannot be read, the fork goes back to the
    /// source and version it had, so the store and what the session shows still agree. For the
    /// SS14 launcher, the newest build of the fork it holds is copied into Paperdoll's store
    /// first, replacing any copy from before only once the fork loads from the new one.
    /// </summary>
    public async Task<ForkLoad> PrepareFromSourceAsync(ForkInfo fork, ForkSource source, string? characterSource, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sourced = RequireSourced();
        await TakeStoreTurnAsync(ct);
        IForkStore? before = null;
        var switched = false;
        string? downloaded = null;
        var synced = false;
        LauncherImports? copies = null;
        var copied = false;
        string? setAside = null;
        try
        {
            if (source.Kind == ForkSourceKind.Folder)
            {
                progress?.Report($"Reading {fork.Name}'s game folder");
                var folder = new FolderForkStore(fork.Id, source.Folder ?? "", sourced.OwnFolders);
                await Task.Run(() =>
                {
                    CheckFolderFree(sourced, fork, folder.Directory);
                    folder.Read();
                }, ct);
                before = sourced.SetOwn(fork.Id, folder);
                switched = true;
            }
            else if (source.Kind == ForkSourceKind.Launcher)
            {
                copies = sourced.LauncherCopies ?? throw new InvalidOperationException("This session keeps no copies of launcher builds.");
                var launcher = source.Launcher ?? throw new LauncherException("No SS14 launcher folder was given.");
                progress?.Report($"Looking for {fork.Name} in the SS14 launcher");
                var staging = await Task.Run(() =>
                {
                    Recover(copies, fork);
                    var build = LauncherCache.Newest(launcher, fork)
                        ?? throw new LauncherException($"The SS14 launcher in {launcher} holds no build of {fork.Name} now. Join one of its servers with the launcher once, then try again.");
                    return LauncherImport.Copy(build, copies, progress, ct);
                }, ct);
                try
                {
                    setAside = copies.SwapIn(fork.Id, staging);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    DeleteStaging(copies, staging);
                    throw new LauncherException($"Could not put the copy of {fork.Name} in place in {copies.Directory}: {e.Message}", e);
                }
                copied = true;
                var copy = new LauncherForkStore(fork.Id, copies, launcher);
                await Task.Run(copy.Read, ct);
                before = sourced.SetOwn(fork.Id, copy);
                switched = true;
            }
            else
            {
                before = sourced.SetOwn(fork.Id, null);
                switched = true;
                downloaded = await Store.CommitOfAsync(fork.Id, ct);
                if (downloaded == null)
                {
                    progress?.Report($"Checking {fork.Name} for its newest version");
                    synced = true;
                    await Store.SyncAsync(fork, ct);
                }
            }
            var load = await PrepareAsync(fork, characterSource, progress, ct);
            // The fork loads from its new source: a copy it no longer reads goes.
            if (copied)
                Discard(copies!, setAside);
            else if (before is LauncherForkStore)
                DeleteCopy(sourced, fork);
            return load;
        }
        catch (Exception failure)
        {
            // The source first, which cannot fail, so the fork is back where it was whatever follows.
            if (switched)
                sourced.SetOwn(fork.Id, before);
            if (copied)
            {
                try
                {
                    copies!.Undo(fork.Id, setAside);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException($"{failure.Message} The copy it had before could not be put back either: {undo.Message}", failure);
                }
            }
            if (synced)
            {
                try
                {
                    await sourced.Downloads.RevertSyncAsync(fork.Id, downloaded, CancellationToken.None);
                }
                catch (Exception undo) when (!ct.IsCancellationRequested)
                {
                    // The load's own failure is what is reported, with this one beside it.
                    throw new InvalidOperationException($"{failure.Message} Its download could not be undone either: {undo.Message}", failure);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    // Stopped by the player, which is what is reported.
                }
            }
            throw;
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    private static void DeleteStaging(LauncherImports copies, string staging)
    {
        try
        {
            copies.DeleteStaging(staging);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Deleted at the next copy or start, by LauncherImports.Recover.
        }
    }

    // Tidies up after a copy of the fork that stopped part way when Paperdoll did.
    private static void Recover(LauncherImports copies, ForkInfo fork)
    {
        try
        {
            copies.Recover(fork.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again at the next copy or start.
        }
    }

    private static void Discard(LauncherImports copies, string? setAside)
    {
        try
        {
            copies.Discard(setAside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Deleted at the next copy or start, by LauncherImports.Recover.
        }
    }

    /// <summary>
    /// Downloads a fork again rather than reading it from a game folder or launcher copy; a game
    /// folder is left as it is, a launcher copy deleted.
    /// </summary>
    public async Task UseDownloadAsync(ForkInfo fork, CancellationToken ct = default)
    {
        var sourced = RequireSourced();
        await TakeStoreTurnAsync(ct);
        try
        {
            if (sourced.UseDownload(fork.Id) is LauncherForkStore)
                await Task.Run(() => DeleteCopy(sourced, fork), CancellationToken.None);
        }
        finally
        {
            _storeTurn.Release();
        }
    }
}
