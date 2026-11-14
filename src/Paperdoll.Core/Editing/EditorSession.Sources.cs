using Paperdoll.Core.Forks;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Editing;

/// <summary>Where each fork's files come from: downloaded, or a game folder on this computer.</summary>
public sealed partial class EditorSession
{
    private SourcedForkStore? Sourced => Store as SourcedForkStore;

    private SourcedForkStore RequireSourced() =>
        Sourced ?? throw new InvalidOperationException("This session's store reads every fork from its downloads.");

    public ForkSource SourceOf(string forkId) => Sourced?.SourceOf(forkId) ?? ForkSource.Download;

    /// <summary>Every fork that is not downloaded, with where it comes from, to keep in the settings.</summary>
    public IReadOnlyDictionary<string, ForkSource> Sources() => Sourced?.Sources ?? new Dictionary<string, ForkSource>();

    /// <summary>Why a fork's game folder could not be read when last tried, or null.</summary>
    public string? FolderProblem(string forkId) => Sourced?.FolderOf(forkId)?.Problem;

    /// <summary>What was left out of a fork's game folder when it was last read: links leading out of it, folders that cannot be listed.</summary>
    public IReadOnlyList<string> FolderSkipped(string forkId) => Sourced?.FolderOf(forkId)?.Skipped ?? [];

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
                sourced.UseFolder(fork.Id, folder);
            }, ct);
        }
        finally
        {
            _storeTurn.Release();
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
    /// source and version it had, so the store and what the session shows still agree.
    /// </summary>
    public async Task<ForkLoad> PrepareFromSourceAsync(ForkInfo fork, ForkSource source, string? characterSource, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sourced = RequireSourced();
        await TakeStoreTurnAsync(ct);
        FolderForkStore? before = null;
        var switched = false;
        string? downloaded = null;
        var synced = false;
        try
        {
            if (source.Kind == ForkSourceKind.Folder)
            {
                progress?.Report($"Reading {fork.Name}'s game folder");
                var folder = new FolderForkStore(fork.Id, source.Folder ?? "");
                await Task.Run(() =>
                {
                    CheckFolderFree(sourced, fork, folder.Directory);
                    folder.Read();
                }, ct);
                before = sourced.SetFolder(fork.Id, folder);
                switched = true;
            }
            else
            {
                before = sourced.SetFolder(fork.Id, null);
                switched = true;
                downloaded = await Store.CommitOfAsync(fork.Id, ct);
                if (downloaded == null)
                {
                    progress?.Report($"Checking {fork.Name} for its newest version");
                    synced = true;
                    await Store.SyncAsync(fork, ct);
                }
            }
            return await PrepareAsync(fork, characterSource, progress, ct);
        }
        catch (Exception failure)
        {
            // The source first, which cannot fail, so the fork is back where it was whatever follows.
            if (switched)
                sourced.SetFolder(fork.Id, before);
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

    /// <summary>Downloads a fork again rather than reading it from a game folder; the folder is left as it is.</summary>
    public async Task UseDownloadAsync(ForkInfo fork, CancellationToken ct = default)
    {
        var sourced = RequireSourced();
        await TakeStoreTurnAsync(ct);
        try
        {
            sourced.UseDownload(fork.Id);
        }
        finally
        {
            _storeTurn.Release();
        }
    }
}
