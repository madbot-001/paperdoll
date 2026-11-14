using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Outfits;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Editing;

/// <summary>A known fork and whether it has been downloaded.</summary>
public sealed record ForkStatus(ForkInfo Fork, string? Commit)
{
    public bool Downloaded => Commit != null;

    /// <summary>Paperdoll can edit forks on the new appearance model, and old-model forks it has been checked against.</summary>
    public bool Editable => Fork.Model == AppearanceModel.New || Fork.Supported;
}

/// <summary>What became of trying an item on.</summary>
public enum WearResult
{
    Worn,

    /// <summary>Not put on: a later pick in the slot, or dress-up was cleared since the pick.</summary>
    Dropped,

    /// <summary>Not put on: other store work (a fork load, update, removal or clean-up) stopped the download.</summary>
    Stopped,
}

/// <summary>A sprite on screen and its licence, for the credits pane.</summary>
/// <summary>A fork read and a character fitted to it, ready for <see cref="EditorSession.Use"/>.</summary>
/// <param name="BeforeSwitch">The character as it was, when fitting it to this fork changed it.</param>
public sealed record ForkLoad(ForkInfo Fork, ForkContent Content, PaperdollRenderer Renderer, MemoryTextures Textures, CharacterFile File,
    IReadOnlyList<RuleFix> Fixes, CharacterLook Look, string? BeforeSwitch);

public sealed record CreditLine(string Rsi, string? License, string? Copyright)
{
    public bool NonCommercial => License?.Contains("NC", StringComparison.Ordinal) == true;
}

/// <summary>
/// Everything the editor window works on, without the window. Every edit runs through the game's
/// rules, so the character stays one the game would accept.
/// </summary>
public sealed partial class EditorSession : IAsyncDisposable
{
    private IBlobReader? _reader;

    /// <summary>A session over a given store; <see cref="OpenAsync"/> picks the usual one.</summary>
    public EditorSession(IForkStore store) => Store = store;

    public IForkStore Store { get; }
    public string StoreKind => (Store is SourcedForkStore sourced ? sourced.Downloads : Store) is GitForkStore ? "git" : "GitHub API";

    public ForkInfo? Fork { get; private set; }
    public ForkContent? Content { get; private set; }
    public PaperdollRenderer? Renderer { get; private set; }

    public CharacterFile? File { get; private set; }
    public CharacterLook? Look { get; private set; }

    /// <summary>What the rules changed on the last edit, open or new character.</summary>
    public IReadOnlyList<RuleFix> LastFixes { get; private set; } = [];

    // The character as it was before a fork switch changed it, kept until the next edit.
    private string? _beforeSwitch;

    /// <summary>Whether switching forks changed the character, and switching back would restore it.</summary>
    public bool ChangedBySwitch => _beforeSwitch != null;

    /// <summary>
    /// What to keep as the working copy: the character as last edited, not as a fork switch
    /// has since changed it.
    /// </summary>
    public string WorkingCopy() => _beforeSwitch ?? Export();

    /// <summary>A session over the usual download store, where any fork can be read from a game folder or a copy of a launcher build instead.</summary>
    public static async Task<EditorSession> OpenAsync(string? directory = null, CancellationToken ct = default)
    {
        directory ??= ForkStores.DefaultDirectory;
        return new(new SourcedForkStore(await ForkStores.OpenAsync(directory, ct: ct), Path.Combine(directory, "launcher")));
    }

    public async Task<IReadOnlyList<ForkStatus>> ForkStatusesAsync(CancellationToken ct = default)
    {
        var commits = await Store.CommitsAsync(ct);
        return KnownForks.All.Select(fork => new ForkStatus(fork, commits.GetValueOrDefault(fork.Id))).ToList();
    }

    /// <summary>
    /// Makes a fork the one being edited: downloads it if needed, reads its data and sprites. An
    /// open character is checked against the new data rather than replaced; otherwise a new one
    /// starts. The window uses <see cref="PrepareForkAsync"/> and <see cref="Use"/> instead, so
    /// the session never changes while it reads it.
    /// </summary>
    public async Task LoadForkAsync(ForkInfo fork, bool update, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Use(await PrepareForkAsync(fork, update, CharacterSource(), progress, ct));

    /// <summary>
    /// What a fork load fits to the new fork: the character as it was before any earlier switch
    /// changed it, so switching back restores what another fork's rules dropped. Null for none.
    /// </summary>
    public string? CharacterSource() => File == null ? null : _beforeSwitch ?? File.ToYaml();

    /// <summary>
    /// Reads a fork and fits the character to it without changing the session, so it can run away
    /// from the window's thread while the window keeps using the session. A failure leaves
    /// everything as it was.
    /// </summary>
    /// <param name="source">The character to fit, from <see cref="CharacterSource"/>; null starts a new one.</param>
    public async Task<ForkLoad> PrepareForkAsync(ForkInfo fork, bool update, string? source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await TakeStoreTurnAsync(ct);
        try
        {
            var previous = await Store.CommitOfAsync(fork.Id, ct);
            // A game folder is read again at every load, so what is kept for its last reading
            // (rooms, pictures) is never taken for files changed since.
            if (!update && previous != null && SourceOf(fork.Id).Kind != ForkSourceKind.Folder)
                return await PrepareAsync(fork, source, progress, ct);

            progress?.Report(SourceOf(fork.Id).Kind == ForkSourceKind.Folder
                ? $"Reading {fork.Name}'s game folder"
                : $"Checking {fork.Name} for its newest version");
            try
            {
                // Inside: a sync stopped part way may already have moved the fork.
                await Store.SyncAsync(fork, ct);
                return await PrepareAsync(fork, source, progress, ct);
            }
            catch
            {
                // A version that cannot be loaded (offline part way, out of requests) is not kept:
                // the fork stays on the version it had, which still opens without the network.
                await Store.RevertSyncAsync(fork.Id, previous, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    // One piece of store work at a time: a wardrobe download, or a fork load, update, removal or
    // clean-up. The GitHub API store rewrites a fork's record after downloading, and git runs one
    // process per command in the same repository, so these must not overlap.
    private readonly SemaphoreSlim _storeTurn = new(1, 1);

    // Stopped when other store work starts, which waits for the download to end.
    private CancellationTokenSource _downloads = new();

    // Stops wardrobe downloads under way and waits for the store; release _storeTurn when done.
    private async Task TakeStoreTurnAsync(CancellationToken ct)
    {
        var running = _downloads;
        _downloads = new CancellationTokenSource();
        await running.CancelAsync();
        await _storeTurn.WaitAsync(ct);
    }

    /// <summary>
    /// Loads a fork at the commit it was already synced to, such as one just matched to a server;
    /// if it cannot be loaded, the fork goes back to <paramref name="previous"/>.
    /// </summary>
    public async Task<ForkLoad> PrepareSyncedForkAsync(ForkInfo fork, string? previous, string? source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await TakeStoreTurnAsync(ct);
        try
        {
            return await PrepareAsync(fork, source, progress, ct);
        }
        catch
        {
            await Store.RevertSyncAsync(fork.Id, previous, CancellationToken.None);
            throw;
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    private async Task<ForkLoad> PrepareAsync(ForkInfo fork, string? source, IProgress<string>? progress, CancellationToken ct)
    {
        var content = await ForkContent.LoadAsync(Store, fork, progress, ct);
        progress?.Report("Reading sprites");
        MemoryTextures textures;
        try
        {
            textures = await MemoryTextures.LoadAsync(content, _reader ??= Store.OpenReader(), ct);
        }
        catch (Exception e) when (e is IOException or GitException && !ct.IsCancellationRequested)
        {
            // The reader, which runs for the whole session, has stopped (killed, or on Windows
            // its window closed): start another, once.
            await CloseReaderAsync();
            textures = await MemoryTextures.LoadAsync(content, _reader = Store.OpenReader(), ct);
        }
        var renderer = new PaperdollRenderer(content.Characters, content.Prototypes, textures);

        var file = source != null
            ? CharacterFile.Parse(source)
            : CreateCharacter(content, fork, content.Characters.Species.ContainsKey(CharacterRules.DefaultSpecies)
                ? CharacterRules.DefaultSpecies
                // The first in the species list, which is in name order.
                : content.Characters.Selectable(fork.HiddenSpecies).OrderBy(s => content.Strings.Get(s.NameKey), StringComparer.CurrentCulture).First().Id);
        var (fixes, look) = Checked(file, content, fork);
        // A launcher copy the fork has loaded from is kept over one it has not, after a stop.
        if (Sourced?.LauncherOf(fork.Id) != null)
            Sourced.LauncherCopies?.MarkLoaded(fork.Id);
        progress?.Report($"{fork.Name} ready");
        return new ForkLoad(fork, content, renderer, textures, file, fixes, look, source != null && fixes.Count > 0 ? source : null);
    }

    /// <summary>Takes a prepared fork into use, all at once.</summary>
    public void Use(ForkLoad load)
    {
        (Fork, Content, Renderer, _textures, File, LastFixes, Look, _beforeSwitch) =
            (load.Fork, load.Content, load.Renderer, load.Textures, load.File, load.Fixes, load.Look, load.BeforeSwitch);
        _available.Clear();
        ClearDressUp();
        ForgetOtherFolderVersions(load.Content.Commit);
    }

    // The sprite files the renderer reads, which clothing tried on adds to.
    private MemoryTextures? _textures;

    /// <summary>Forgets a fork and frees its space.</summary>
    public async Task RemoveForkAsync(ForkInfo fork, CancellationToken ct = default)
    {
        await TakeStoreTurnAsync(ct);
        try
        {
            // The reader holds the store's files open, which on Windows stops them being deleted.
            await CloseReaderAsync();
            await Store.RemoveAsync(fork.Id, ct);
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    private async Task CloseReaderAsync()
    {
        var reader = _reader;
        _reader = null;
        if (reader == null)
            return;
        try
        {
            await reader.DisposeAsync();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            // It had already stopped.
        }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Loads the fork a server runs, at the commit it was built from, so the choices match that
    /// server rather than the fork's newest code. Update goes back to the newest.
    /// </summary>
    /// <returns>The fork, the commit it now has, and the commit it had before (null if none), for <see cref="PrepareSyncedForkAsync"/>.</returns>
    public async Task<(ForkInfo Fork, string Commit, string? Previous)> SyncToServerAsync(string address, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Asking the server what it runs");
        var build = await new Servers.GameServers(Http).BuildAsync(address, ct);
        var fork = KnownForks.FindByServerForkId(build.ForkId ?? "")
            ?? throw new InvalidOperationException($"This server runs {build.ForkId ?? "a fork it does not name"}, which Paperdoll does not know.");
        if (!new ForkStatus(fork, null).Editable)
            throw new InvalidOperationException($"This server runs {fork.Name}, which Paperdoll cannot edit yet.");
        if (SourceOf(fork.Id).Kind == ForkSourceKind.Folder)
            throw new InvalidOperationException($"This server runs {fork.Name}, which is read from a game folder as it is on disk, so it cannot be moved to the server's version. To match the server, switch {fork.Name} back to Download in Fork > Forks....");
        if (!build.IsCommit)
            throw new InvalidOperationException($"This server gives its version as {build.Version ?? "nothing"}, not a git commit, so it cannot be matched.");

        progress?.Report($"Getting {fork.Name} at the server's version");
        await TakeStoreTurnAsync(ct);
        try
        {
            var previous = await Store.CommitOfAsync(fork.Id, ct);
            try
            {
                return (fork, await Store.SyncToCommitAsync(fork, build.Version!, ct), previous);
            }
            catch
            {
                // Stopped part way, the fork may already have moved.
                await Store.RevertSyncAsync(fork.Id, previous, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    /// <summary>Frees space old fork versions left behind. Returns the bytes freed.</summary>
    public async Task<long> CleanUpStoreAsync(CancellationToken ct = default)
    {
        await TakeStoreTurnAsync(ct);
        try
        {
            // Close the reader first; it is opened again when next needed.
            await CloseReaderAsync();
            return await Store.CleanUpAsync(ct);
        }
        finally
        {
            _storeTurn.Release();
        }
    }

    /// <summary>Species a player can pick on the loaded fork, by display name.</summary>
    public IReadOnlyList<SpeciesInfo> Selectable() =>
        Content == null ? [] : Content.Characters.Selectable(Fork!.HiddenSpecies).OrderBy(DisplayName, StringComparer.CurrentCulture).ToList();

    public string DisplayName(SpeciesInfo species) => Content?.Strings.Get(species.NameKey) ?? species.Id;

    public string MarkingName(string markingId) =>
        Content?.Strings[$"marking-{markingId}"] ?? markingId;

    /// <summary>A new character of the species with the game's defaults.</summary>
    public void NewCharacter(string speciesId)
    {
        var file = CreateCharacter(RequireContent(), Fork!, speciesId);
        var (fixes, look) = Checked(file);
        (File, LastFixes, Look, _beforeSwitch) = (file, fixes, look, null);
        ClearDressUp();
    }

    // A new character of a species, as the lobby starts one.
    private static CharacterFile CreateCharacter(ForkContent content, ForkInfo fork, string speciesId)
    {
        var catalog = content.Characters;
        var species = catalog.Species[speciesId];
        var file = CharacterFile.CreateNew(fork.ServerForkIds.FirstOrDefault() ?? fork.Id);
        file.Name = RandomNamer(content, fork)(species, "Epicene");
        file.Age = Math.Max(species.MinAge, Math.Min(species.YoungAge, species.MaxAge));
        file.Gender = "Epicene";
        file.WriteLook(LookDefaults.Create(catalog, speciesId, species.Sexes[0], catalog.DefaultSkin(species), Rgba.Parse("#000000")), catalog);
        // The species' default size, which the lobby's reset buttons give.
        if (CharacterSize.HasHeight(fork))
            CharacterSize.WriteHeight(file, fork, CharacterSize.CheckHeight(species.DefaultHeight, species, fork));
        if (CharacterSize.HasWidth(fork))
            CharacterSize.WriteWidth(file, CharacterSize.CheckWidth(species.DefaultWidth, species));
        // The game starts every profile on MaleHuman; its rules then give species that cannot use it their default.
        if (catalog.HasVoices)
            file.Voice = species.Voices.Contains(CharacterRules.DefaultVoice) ? CharacterRules.DefaultVoice : species.DefaultVoice(species.Sexes[0]);
        return file;
    }

    /// <summary>The job whose clothes the preview shows; null means the one the lobby would pick.</summary>
    public string? PreviewJob { get; set; }

    public bool ShowClothes { get; set; } = true;

    /// <summary>The job the preview dresses for: the chosen one, else the character's High priority job, else the fallback job.</summary>
    public string? DressedJob()
    {
        var outfits = RequireContent().Outfits;
        foreach (var candidate in new[] { PreviewJob, File?.HighPriorityJob, Fork?.FallbackJob })
        {
            if (candidate != null && outfits.Jobs.ContainsKey(candidate))
                return candidate;
        }
        return outfits.SelectableJobs().FirstOrDefault()?.Id;
    }

    /// <summary>The job's loadout as the character has it, with defaults where nothing is saved.</summary>
    public RoleLoadout LoadoutFor(string jobId) =>
        RequireContent().Outfits.LoadoutFor(jobId, Look!.Species, RequireFile().Loadouts.GetValueOrDefault(OutfitCatalog.RoleFor(jobId)));

    /// <summary>Everything the character spawns with in the job the preview shows.</summary>
    public SpawnGear? GearAtSpawn() =>
        DressedJob() is { } job ? RequireContent().Outfits.GearAtSpawn(job, LoadoutFor(job)) : null;

    /// <summary>
    /// What the preview puts on the character, slot to item: the job's outfit with dress-up's
    /// changes; null when clothes are off.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Outfit()
    {
        if (!ShowClothes || DressedJob() == null)
            return null;
        var outfit = JobOutfit();
        if (_dressUp.Count == 0 || !DressUpShown)
            return outfit;
        var dressed = new Dictionary<string, string>(outfit, StringComparer.Ordinal);
        foreach (var (slot, item) in _dressUp)
        {
            if (item == null)
                dressed.Remove(slot);
            else
                dressed[slot] = item;
        }
        return dressed;
    }

    /// <summary>The outfit the lobby preview shows for the dressed job, without dress-up; empty without a job.</summary>
    public IReadOnlyDictionary<string, string> JobOutfit() =>
        DressedJob() is { } job ? RequireContent().Outfits.OutfitFor(job, LoadoutFor(job)) : new Dictionary<string, string>();

    // Dress-up, by slot: an item, or null for nothing. Slots not here wear the job's.
    private readonly Dictionary<string, string?> _dressUp = new(StringComparer.Ordinal);

    /// <summary>
    /// What is tried on in the preview in place of the job's outfit, by slot: an item, or null
    /// for nothing. Not part of the character: it is never saved, and opening a file, a new or
    /// random character, or another fork clears it.
    /// </summary>
    public IReadOnlyDictionary<string, string?> DressUp => _dressUp;

    /// <summary>Whether any slot shows something other than the job's outfit.</summary>
    public bool HasDressUp => _dressUp.Count > 0;

    /// <summary>Whether the preview wears the dress-up; off, it wears the job's outfit and the dress-up is kept for later.</summary>
    public bool DressUpShown { get; set; }

    // The last pick in each slot, numbered, so an item that finishes downloading after a later
    // pick is not put on over it.
    private readonly Dictionary<string, long> _lastPick = new(StringComparer.Ordinal);
    private long _picks;

    /// <summary>
    /// How many times dress-up has been cleared (another fork or character, or Reset). A pick
    /// belongs to the count at the moment it is made.
    /// </summary>
    public int DressUpCleared { get; private set; }

    // Cancelled when dress-up is cleared, so picks still waiting or downloading stop with it.
    private CancellationTokenSource _dressUpRound = new();

    /// <summary>Cancelled when dress-up is next cleared.</summary>
    public CancellationToken DressUpToken => _dressUpRound.Token;

    private void ClearDressUp()
    {
        _dressUp.Clear();
        _lastPick.Clear();
        DressUpCleared++;
        var ended = _dressUpRound;
        _dressUpRound = new CancellationTokenSource();
        ended.Cancel();
    }

    public void WearNothing(string slot)
    {
        _lastPick[slot] = ++_picks;
        _dressUp[slot] = null;
    }

    /// <summary>Gives the slot back to the job's outfit.</summary>
    public void WearFromJob(string slot)
    {
        _lastPick[slot] = ++_picks;
        _dressUp.Remove(slot);
    }

    /// <summary>Gives every slot back to the job's outfit.</summary>
    public void ResetDressUp() => ClearDressUp();

    /// <summary>
    /// Tries an item on in a slot, for a pick made when <see cref="DressUpCleared"/> was
    /// <paramref name="round"/>. When the fork was loaded without the item's pictures, they are
    /// downloaded first, in one request; if that fails, the slot stays as it was and the error is
    /// thrown. Cancelled through <paramref name="ct"/>, it throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<WearResult> WearAsync(string slot, string entityId, int round, CancellationToken ct = default)
    {
        if (round != DressUpCleared)
            return WearResult.Dropped;
        var (content, renderer, textures) = (RequireContent(), Renderer!, _textures!);
        var pick = _lastPick[slot] = ++_picks;
        if (MissingSprites(entityId).Count > 0)
        {
            var otherWork = _downloads.Token;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, otherWork);
            try
            {
                await _storeTurn.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException) when (otherWork.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return WearResult.Stopped;
            }
            try
            {
                // Another download may have brought the files meanwhile, or the dress-up ended.
                var missing = round == DressUpCleared ? MissingSprites(entityId) : [];
                if (missing.Count > 0)
                {
                    await Store.FetchAsync(content.Fork.Id, missing, stop.Token);
                    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                    try
                    {
                        var reader = _reader ??= Store.OpenReader();
                        foreach (var entry in missing)
                        {
                            if (await reader.ReadAsync(entry.ObjectId, stop.Token) is { } data)
                                files[entry.Path[(ForkContent.TexturesFolder.Length + 1)..]] = data;
                        }
                    }
                    catch (Exception e) when (e is IOException or GitException && !stop.IsCancellationRequested)
                    {
                        // The reader has stopped; the next try starts another.
                        await CloseReaderAsync();
                        throw;
                    }
                    textures.Add(files);
                    renderer.Forget(missing.Select(e => FolderOf(e.Path)).ToHashSet(StringComparer.Ordinal));
                }
            }
            catch (OperationCanceledException) when (otherWork.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return WearResult.Stopped;
            }
            finally
            {
                _storeTurn.Release();
            }
            if (otherWork.IsCancellationRequested && !ct.IsCancellationRequested)
                return WearResult.Stopped;
        }
        ct.ThrowIfCancellationRequested();
        // Only the last pick in the slot, in the dress-up it was made in.
        if (_lastPick.GetValueOrDefault(slot) != pick || round != DressUpCleared)
            return WearResult.Dropped;
        _dressUp[slot] = entityId;
        return WearResult.Worn;
    }

    /// <summary>The files of an item's sprite folders that are listed but not read in yet, which trying it on downloads.</summary>
    public IReadOnlyList<StoreEntry> MissingSprites(string entityId)
    {
        var content = RequireContent();
        var folders = new ClothingResolver(content.Prototypes, _ => null).SpriteFolders(entityId).ToHashSet(StringComparer.Ordinal);
        return content.WardrobeFiles
            .Where(f => folders.Contains(FolderOf(f.Value.Path)) && _textures?.Has(f.Key) != true)
            .Select(f => f.Value)
            .ToList();
    }

    // "Resources/Textures/Clothing/Head/cap.rsi/icon.png" -> "Clothing/Head/cap.rsi"
    private static string FolderOf(string path) => path[(ForkContent.TexturesFolder.Length + 1)..path.LastIndexOf('/')];

    /// <summary>
    /// Whether an item worn in a slot draws anything on the character: the body has a layer for
    /// the slot and the item a picture for it.
    /// </summary>
    public bool Draws(string entityId, string slot)
    {
        var species = RequireContent().Characters.Species[Look!.Species];
        return Renderer!.HasSlot(species, slot) && Renderer.Clothing(entityId, slot, species) is { } visual
            && visual.Layers.Any(l => l.Sprite.State != null && Renderer.Meta(l.Sprite.Rsi)?.States.ContainsKey(l.Sprite.State) == true);
    }

    /// <summary>
    /// The slots dress-up offers, in the order the game lists them: those the character's body
    /// draws, with something from the job or the wardrobe to put there.
    /// </summary>
    public IReadOnlyList<string> WardrobeSlots()
    {
        var content = RequireContent();
        var species = content.Characters.Species[Look!.Species];
        var job = JobOutfit();
        return ClothingResolver.SlotStates.Keys
            .Where(slot => (job.ContainsKey(slot) || content.Wardrobe.Items(slot).Count > 0) && Renderer!.HasSlot(species, slot))
            .ToList();
    }

    private (ForkContent Content, Dictionary<string, IReadOnlyList<(string Id, string Name)>> BySlot)? _wearables;

    /// <summary>The items that fit a slot, by name, as loadouts are named.</summary>
    public IReadOnlyList<(string Id, string Name)> Wearables(string slot)
    {
        var content = RequireContent();
        if (_wearables is not { } cached || cached.Content != content)
            _wearables = cached = (content, new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.Ordinal));
        if (!cached.BySlot.TryGetValue(slot, out var items))
        {
            cached.BySlot[slot] = items = content.Wardrobe.Items(slot)
                .Select(id => (id, EntityName(id)))
                .OrderBy(i => i.Item2, StringComparer.CurrentCulture).ThenBy(i => i.id, StringComparer.Ordinal)
                .ToList();
        }
        return items;
    }

    public IReadOnlyList<string> SelectedTraits() => RequireFile().TraitPreferences;

    /// <summary>What trait conditions are checked against: species, the dressed job and its department.</summary>
    public Traits.TraitContext TraitContext()
    {
        var content = RequireContent();
        var job = DressedJob();
        var department = job == null ? null : content.Outfits.Departments.FirstOrDefault(d => d.Roles.Contains(job))?.Id;
        return new Traits.TraitContext(Look!.Species, job, department, SelectedTraits());
    }

    /// <summary>Adds or removes a trait, within the fork's limits. Returns why not, if it cannot be added.</summary>
    public IReadOnlyList<RuleFix> ToggleTrait(string traitId)
    {
        var content = RequireContent();
        var selected = SelectedTraits().ToList();
        if (selected.Remove(traitId))
            return Edit(f => f.SetTraitPreferences(selected));
        if (content.Traits.WhyNot(content.Traits.Traits[traitId], TraitContext(), Fork!.TraitRules) is { } reason)
            return [new RuleFix("traits", reason)];
        selected.Add(traitId);
        return Edit(f => f.SetTraitPreferences(selected));
    }

    /// <summary>The character's priority for a job: High, Medium, Low or Never.</summary>
    public string JobPriority(string jobId) => RequireFile().JobPriorities.GetValueOrDefault(jobId) ?? "Never";

    public IReadOnlyList<RuleFix> SetJobPriority(string jobId, string priority) => Edit(f => f.SetJobPriority(jobId, priority));

    /// <summary>
    /// Selects or clears a loadout within its group's limits: a group taking one item swaps it,
    /// others add up to the limit; clearing stops at the group's minimum.
    /// </summary>
    public IReadOnlyList<RuleFix> ToggleLoadout(string jobId, string groupId, string loadoutId)
    {
        var outfits = RequireContent().Outfits;
        var group = outfits.Groups[groupId];
        var current = LoadoutFor(jobId).Groups.FirstOrDefault(g => g.Group == groupId).Loadouts?.ToList() ?? [];

        if (current.Contains(loadoutId))
        {
            if (current.Count <= group.MinLimit)
                return [new RuleFix("loadout", $"This group needs at least {group.MinLimit}.")];
            current.Remove(loadoutId);
        }
        else if (group.MaxLimit == 1)
            current = [loadoutId];
        else if (current.Count >= group.MaxLimit)
            return [new RuleFix("loadout", $"This group takes at most {group.MaxLimit}; clear one first.")];
        else
            current.Add(loadoutId);

        return Edit(f => f.SetLoadoutGroup(OutfitCatalog.RoleFor(jobId), groupId, current));
    }

    /// <summary>
    /// Display name, as the lobby gives it: the dummy entity's, or the single item's, worn, held or
    /// stored. Where the lobby would say "Unknown" (several items), the loadout's id.
    /// </summary>
    public string LoadoutName(string loadoutId)
    {
        var outfits = RequireContent().Outfits;
        return outfits.Loadouts.TryGetValue(loadoutId, out var loadout) && outfits.NamedAfter(loadout) is { } entity
            ? EntityName(entity)
            : loadoutId;
    }

    /// <summary>The picture the lobby shows beside a loadout, facing south; null when it has none.</summary>
    public SKBitmap? LoadoutPicture(string loadoutId)
    {
        var outfits = RequireContent().Outfits;
        return outfits.Loadouts.TryGetValue(loadoutId, out var loadout) && outfits.PictureOf(loadout) is { } entity
            ? Renderer!.RenderEntity(entity)
            : null;
    }

    /// <summary>An entity's name: its translation if the fork has one, else the prototype's name.</summary>
    public string EntityName(string entityId)
    {
        var content = RequireContent();
        if (content.Strings[$"ent-{entityId}"] is { } translated)
            return translated;
        return content.Prototypes.Resolve("entity", entityId) is { } entity && Scalar(entity, "name") is { } name ? name : entityId;
    }

    private static string? Scalar(YamlDotNet.RepresentationModel.YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode(key), out var value)
            && value is YamlDotNet.RepresentationModel.YamlScalarNode scalar ? scalar.Value : null;

    public (float X, float Y) SpriteScale()
    {
        // A job's own body, such as a borg, is drawn at its sprite's size.
        if (PreviewEntity() != null)
            return (1, 1);
        var species = RequireContent().Characters.Species[Look!.Species];
        return CharacterSize.SpriteScale(Fork!, species, RequireFile());
    }

    /// <summary>
    /// Sets the height. Where the fork has width too, the width follows as the lobby's sliders
    /// pull it, to stay within the species' size ratio.
    /// </summary>
    public IReadOnlyList<RuleFix> SetHeight(float height) => Edit(f =>
    {
        if (!CharacterSize.HasWidth(Fork!))
        {
            CharacterSize.WriteHeight(f, Fork!, height);
            return;
        }
        var species = RequireContent().Characters.Species[Look!.Species];
        var (h, w) = CharacterSize.KeepRatio(height, CharacterSize.Width(f, Fork!, species), species, heightMoved: true);
        CharacterSize.WriteHeight(f, Fork!, h);
        CharacterSize.WriteWidth(f, w);
    });

    /// <summary>Sets the width (Goob); the height follows as the lobby's sliders pull it.</summary>
    public IReadOnlyList<RuleFix> SetWidth(float width) => Edit(f =>
    {
        var species = RequireContent().Characters.Species[Look!.Species];
        var (h, w) = CharacterSize.KeepRatio(CharacterSize.Height(f, Fork!, species), width, species, heightMoved: false);
        CharacterSize.WriteHeight(f, Fork!, h);
        CharacterSize.WriteWidth(f, w);
    });

    /// <summary>
    /// Sets the sex; pronouns follow it (Male, Female, else Epicene), and where the fork has
    /// voices, the voice becomes the species' default for that sex.
    /// </summary>
    public IReadOnlyList<RuleFix> SetSex(string sex) => Edit(f =>
    {
        f.Sex = sex;
        f.Gender = sex switch { "Male" => "Male", "Female" => "Female", _ => "Epicene" };
        if (RequireContent().Characters.Species.TryGetValue(f.Species ?? "", out var species) && species.DefaultVoice(sex) is { } voice)
            f.Voice = voice;
    });

    public IReadOnlyList<RuleFix> SetVoice(string voice) => Edit(f => f.Voice = voice);

    /// <summary>Names what the character spawns as in a role that allows it, such as a borg; empty for a random one.</summary>
    public IReadOnlyList<RuleFix> SetRoleName(string jobId, string name) =>
        Edit(f => f.SetRoleName(OutfitCatalog.RoleFor(jobId), string.IsNullOrWhiteSpace(name) ? null : name));

    public IReadOnlyList<RuleFix> SetCustomSpeciesName(string name) => Edit(f => f.SetValue(CustomSpeciesName.Key, name));

    public IReadOnlyList<RuleFix> SetRecordText(string key, string value) => Edit(f => CharacterRecords.SetText(f, key, value, Fork!.RecordFormat));

    public IReadOnlyList<RuleFix> SetRecordNumber(string key, int value) => Edit(f => CharacterRecords.SetNumber(f, key, value, Fork!.RecordFormat));

    public IReadOnlyList<RuleFix> SetWorkAuthorization(bool value) => Edit(f => CharacterRecords.SetWorkAuthorization(f, value, Fork!.RecordFormat));

    public IReadOnlyList<RuleFix> SetRecordEntries(string list, IReadOnlyList<RecordEntry> entries) => Edit(f => CharacterRecords.SetEntries(f, list, entries, Fork!.RecordFormat));

    public IReadOnlyList<RuleFix> SetAllergies(IEnumerable<(string Reagent, float Amount)> allergies) => Edit(f => Allergies.Write(f, allergies));

    private (ForkContent Content, IReadOnlyList<ReagentInfo> List)? _reagents;

    /// <summary>Every reagent of the loaded fork, for the allergy picker.</summary>
    public IReadOnlyList<ReagentInfo> Reagents()
    {
        var content = RequireContent();
        if (_reagents is not { } cached || cached.Content != content)
            _reagents = cached = (content, Allergies.Reagents(content.Prototypes));
        return cached.List;
    }

    /// <summary>
    /// Why a cybernetic part cannot be installed: with the parts it brings, it costs more points
    /// than the species has left. Null if it can.
    /// </summary>
    public string? WhyNotInstall(string partId)
    {
        var catalog = RequireContent().Characters;
        var capacity = catalog.Species[Look!.Species].CyberwareCapacity;
        var installed = RequireFile().Cybernetics;
        var used = Cybernetics.Cost(installed, catalog.Cybernetics);
        var needed = Cybernetics.Cost(Cybernetics.With(installed, partId, catalog.Cybernetics), catalog.Cybernetics) - used;
        return used + needed <= capacity ? null
            : $"Needs {needed} point{(needed == 1 ? "" : "s")} with the parts it brings; {capacity - used} of {capacity} left.";
    }

    /// <summary>Why a cybernetic part cannot be removed: another installed part brought it. Null if it can.</summary>
    public string? WhyNotRemove(string partId) =>
        Cybernetics.AttachedBy(RequireFile().Cybernetics, partId, RequireContent().Characters.Cybernetics) is { } holder
            ? $"Comes attached to the {EntityName(holder)}; remove that instead."
            : null;

    /// <summary>Installs a cybernetic part and the parts attached to it, within the species' points.</summary>
    public IReadOnlyList<RuleFix> InstallCybernetic(string partId) =>
        WhyNotInstall(partId) is { } reason
            ? [new RuleFix(Cybernetics.Key, reason)]
            : Edit(f => f.SetCybernetics(Cybernetics.With(f.Cybernetics, partId, RequireContent().Characters.Cybernetics)));

    /// <summary>Removes a cybernetic part and the parts attached to it, unless another part brought it.</summary>
    public IReadOnlyList<RuleFix> RemoveCybernetic(string partId) =>
        WhyNotRemove(partId) is { } reason
            ? [new RuleFix(Cybernetics.Key, reason)]
            : Edit(f => f.SetCybernetics(Cybernetics.Without(f.Cybernetics, partId, RequireContent().Characters.Cybernetics)));

    /// <summary>Adds or removes an antagonist the character is willing to be.</summary>
    public IReadOnlyList<RuleFix> ToggleAntag(string antagId) => Edit(f =>
    {
        var wanted = f.AntagPreferences.ToList();
        if (!wanted.Remove(antagId))
            wanted.Add(antagId);
        f.SetAntagPreferences(wanted);
    });

    /// <summary>Masculine, Feminine, Neutral and so on.</summary>
    public string VoiceName(string voice) =>
        Content?.Characters.VoiceNames.TryGetValue(voice, out var key) == true ? Content.Strings[key] ?? voice : voice;

    /// <summary>
    /// Opens an exported character and fits it to the loaded fork. A file that cannot be read or
    /// checked leaves the open character as it was.
    /// </summary>
    /// <param name="keepUntilEdited">
    /// Keep the text as it was, as the working copy, until the character is edited, when the
    /// rules change it (reopening a working copy in another fork than it was made for).
    /// </param>
    public IReadOnlyList<RuleFix> Open(string yaml, bool keepUntilEdited = false)
    {
        RequireContent();
        var file = CharacterFile.Parse(yaml);
        var (fixes, look) = Checked(file);
        (File, LastFixes, Look, _beforeSwitch) = (file, fixes, look, keepUntilEdited && fixes.Count > 0 ? yaml : null);
        ClearDressUp();
        return fixes;
    }

    /// <summary>
    /// Keeps what a fork switch changed, as saving does: switching back no longer brings back the
    /// version from before.
    /// </summary>
    public void KeepSwitchChanges() => _beforeSwitch = null;

    /// <summary>A character's text as <see cref="Export"/> would give it, without opening it.</summary>
    public string Normalized(string yaml)
    {
        RequireContent();
        var file = CharacterFile.Parse(yaml);
        Checked(file);
        file.LabelFor(Fork!);
        return file.ToYaml();
    }

    /// <summary>Text for the lobby's Import button.</summary>
    public string Export()
    {
        var file = RequireFile();
        ApplyRules();
        file.LabelFor(Fork!);
        return file.ToYaml();
    }

    /// <summary>Changes the character, then applies the game's rules.</summary>
    public IReadOnlyList<RuleFix> Edit(Action<CharacterFile> change)
    {
        change(RequireFile());
        _beforeSwitch = null;
        ApplyRules();
        return LastFixes;
    }

    /// <summary>Changes the look (markings, colours), then applies the game's rules.</summary>
    public IReadOnlyList<RuleFix> EditLook(Func<CharacterLook, CharacterLook> change)
    {
        var file = RequireFile();
        file.WriteLook(change(Look!), RequireContent().Characters);
        _beforeSwitch = null;
        ApplyRules();
        return LastFixes;
    }

    /// <summary>
    /// Switches species. Skin colour is kept if the new species allows it; markings the new species
    /// cannot have are dropped by the rules, and its required markings are added.
    /// </summary>
    public IReadOnlyList<RuleFix> ChangeSpecies(string speciesId) => EditLook(look =>
    {
        var species = RequireContent().Characters.Species[speciesId];
        var sex = species.Sexes.Contains(look.Sex) ? look.Sex : species.Sexes[0];
        return new CharacterLook { Species = speciesId, Sex = sex, SkinColor = look.SkinColor, EyeColor = look.EyeColor, Markings = look.Markings };
    });

    /// <summary>Markings that may go on this organ layer for the character's sex, by name.</summary>
    // What each layer offers, worked out once per fork, species, sex, organ and layer: the window
    // asks for every layer after every edit, and each answer looks through every marking.
    private readonly Dictionary<(string Species, string Sex, string Organ, string Layer), IReadOnlyList<MarkingInfo>> _available = [];

    public IReadOnlyList<MarkingInfo> AvailableMarkings(OrganInfo organ, string layer)
    {
        var key = (Look!.Species, Look.Sex, organ.Category, layer);
        if (!_available.TryGetValue(key, out var available))
            _available[key] = available = FindAvailableMarkings(organ, layer);
        return available;
    }

    private IReadOnlyList<MarkingInfo> FindAvailableMarkings(OrganInfo organ, string layer)
    {
        var catalog = RequireContent().Characters;
        var sex = Look!.Sex;
        IEnumerable<MarkingInfo> offered;
        if (OldBody() is { } old)
        {
            // The old model offers a category's markings for the species and sex.
            offered = catalog.Markings.Values.Where(m => m.Category == organ.Category && m.Layer == layer
                && old.Offers(m, Look.Species) && (m.SexRestriction == null || m.SexRestriction == sex));
        }
        else
        {
            if (organ.MarkingGroup == null || !catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group))
                return [];
            offered = catalog.Markings.Values.Where(m => m.Layer == layer && CharacterRules.CanBeApplied(group, sex, m));
        }
        return offered.OrderBy(m => MarkingName(m.Id), StringComparer.CurrentCulture).ToList();
    }

    /// <summary>
    /// The limit on a layer's markings, or null when there is none. On the old appearance model it
    /// is the category's points, which all the category's layers share.
    /// </summary>
    public LayerLimit? LimitFor(OrganInfo organ, string layer)
    {
        var catalog = RequireContent().Characters;
        if (OldBody() is { } old)
        {
            return old.Points.TryGetValue(organ.Category, out var points)
                ? new LayerLimit(points.Points, points.Required, points.OnlyWhitelisted, points.Defaults, [])
                : null;
        }
        return organ.MarkingGroup != null && catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group)
            && group.Limits.TryGetValue(layer, out var limit) ? limit : null;
    }

    /// <summary>How many markings the layer may hold; null when there is no limit.</summary>
    public int? LayerLimit(OrganInfo organ, string layer) => LimitFor(organ, layer)?.Limit;

    /// <summary>How many markings count against the layer's limit: its own, or on the old model the whole category's.</summary>
    public int LimitCount(OrganInfo organ, string layer)
    {
        var byLayer = Look!.Markings.GetValueOrDefault(organ.Category);
        if (byLayer == null)
            return 0;
        return OldBody() != null ? byLayer.Values.Sum(l => l.Count) : byLayer.GetValueOrDefault(layer)?.Count ?? 0;
    }

    /// <summary>Whether the organ's layers share one limit (old-model categories spanning several layers).</summary>
    public bool SharesLimit(OrganInfo organ) => OldBody() != null && organ.MarkingLayers.Count > 1;

    // The character's species on the old appearance model, or null on the new one.
    private OldBody? OldBody() =>
        Look != null && Content?.Characters.Species.GetValueOrDefault(Look.Species) is { } species ? species.Old : null;

    /// <summary>
    /// Adds a marking with its default colours (upstream <c>MarkingsViewModel.TrySelectMarking</c>,
    /// MIT): on a layer that takes one marking and has it, the new one replaces it; a full layer
    /// that takes more refuses.
    /// </summary>
    public IReadOnlyList<RuleFix> AddMarking(string organ, string layer, string markingId) =>
        EditLook(look => WithMarking(look, organ, layer, markingId, preview: false));

    /// <summary>
    /// The character facing south without clothes, wearing the marking as adding it would, for a
    /// preview. On a full layer that takes more, it is simply drawn along with the others.
    /// </summary>
    public SKBitmap RenderWithMarking(string organ, string layer, string markingId) =>
        Renderer!.Render(WithMarking(Look!, organ, layer, markingId, preview: true));

    /// <summary>
    /// The marking alone, facing the way that shows the most of it, as the character wears it on
    /// that layer or, if it does not, as it would once added; for pictures in lists.
    /// </summary>
    public SKBitmap MarkingPicture(string organ, string layer, string markingId)
    {
        var look = Look!;
        var worn = look.Markings.GetValueOrDefault(organ)?.GetValueOrDefault(layer)?.Any(e => e.Id == markingId) == true;
        return Renderer!.RenderMarking(worn ? look : WithMarking(look, organ, layer, markingId, preview: true), markingId);
    }

    private CharacterLook WithMarking(CharacterLook look, string organ, string layer, string markingId, bool preview)
    {
        var catalog = RequireContent().Characters;
        var marking = catalog.Markings[markingId];
        var markings = Copy(look.Markings);
        if (!markings.TryGetValue(organ, out var byLayer))
            markings[organ] = byLayer = [];
        if (!byLayer.TryGetValue(layer, out var list))
            byLayer[layer] = list = [];
        var old = catalog.Species[look.Species].Old;
        // Colours are worked out before any replacement, from the markings already there.
        var colors = old == null
            ? MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, list)
            : organ is "Hair" or "FacialHair"
                // The old model's hair colour is its own field, kept when the style changes.
                ? [FirstIn(organ)?.Colors.FirstOrDefault() ?? File?.OldHairColor(organ == "FacialHair") ?? Rgba.White]
                : old.Layer(marking.Layer) is { MarkingsMatchSkin: true }
                    ? Enumerable.Repeat(look.SkinColor, marking.Sprites.Count).ToList()
                    : MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, [], FirstIn);
        var limit = catalog.Species[look.Species].Organs.FirstOrDefault(o => o.Category == organ) is { } info ? LayerLimit(info, layer) : null;
        // On the old model a category's layers share its limit.
        var counted = old != null ? byLayer.Values.Sum(l => l.Count) : list.Count;
        if (limit == 1 && counted == 1)
        {
            foreach (var markingsOnLayer in byLayer.Values)
                markingsOnLayer.Clear();
        }
        else if (limit is { } max && counted >= max)
        {
            if (!preview)
                throw new InvalidOperationException($"This {(old != null ? "category" : "layer")} already holds {max}; remove one first.");
            // The old model draws no more than the limit, so the preview stands in for the last one.
            if (old != null && byLayer.Values.LastOrDefault(l => l.Count > 0) is { } last)
                last.RemoveAt(last.Count - 1);
        }
        list.Add(new MarkingEntry(markingId, colors));
        return With(look, markings);

        MarkingEntry? FirstIn(string category) =>
            look.Markings.GetValueOrDefault(category)?.Values.SelectMany(l => l).FirstOrDefault();
    }

    public IReadOnlyList<RuleFix> RemoveMarking(string organ, string layer, int index) => EditLook(look =>
    {
        var markings = Copy(look.Markings);
        markings[organ][layer].RemoveAt(index);
        return With(look, markings);
    });

    /// <summary>
    /// Moves a marking within its layer. Order matters: the game draws a layer's first marking
    /// on top and each later one underneath.
    /// </summary>
    public IReadOnlyList<RuleFix> MoveMarking(string organ, string layer, int index, int delta) => EditLook(look =>
    {
        var markings = Copy(look.Markings);
        var list = markings[organ][layer];
        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        var entry = list[index];
        list.RemoveAt(index);
        list.Insert(target, entry);
        return With(look, markings);
    });

    /// <summary>The prototype file a species, marking or other prototype comes from.</summary>
    public string? SourceOf(string kind, string id) =>
        Content != null && Content.Prototypes.TryGet(kind, id, out var proto) ? "Resources/Prototypes/" + proto.Path : null;

    public IReadOnlyList<RuleFix> SetMarkingColor(string organ, string layer, int index, int colorIndex, Rgba color) => EditLook(look =>
    {
        var markings = Copy(look.Markings);
        var entry = markings[organ][layer][index];
        var colors = entry.Colors.ToList();
        colors[colorIndex] = color;
        markings[organ][layer][index] = entry with { Colors = colors };
        return With(look, markings);
    });

    public IReadOnlyList<RuleFix> SetSkin(Rgba color) => EditLook(look => new CharacterLook
    {
        Species = look.Species, Sex = look.Sex, SkinColor = color, EyeColor = look.EyeColor, Markings = look.Markings,
    });

    public IReadOnlyList<RuleFix> SetEyes(Rgba color) => EditLook(look => new CharacterLook
    {
        Species = look.Species, Sex = look.Sex, SkinColor = look.SkinColor, EyeColor = color, Markings = look.Markings,
    });

    /// <summary>
    /// The entity the lobby shows instead of the character for the job being dressed for, such as a
    /// borg's body (the job's <c>jobPreviewEntity</c> or <c>jobEntity</c>); null for most jobs.
    /// </summary>
    public string? PreviewEntity() =>
        ShowClothes && DressedJob() is { } job && RequireContent().Outfits.Jobs.TryGetValue(job, out var info) ? info.PreviewEntity : null;

    public SKBitmap Render(Direction direction = Direction.South, double seconds = 0) =>
        PreviewEntity() is { } entity ? Renderer!.RenderEntity(entity, direction, seconds) : Renderer!.Render(Look!, direction, Outfit(), seconds, OutfitTints());

    /// <summary>
    /// Worn items coloured by Euphoria's loadout tint, by slot: a chosen loadout with a colour that
    /// spawns one item, when that item is what the preview shows in its slot.
    /// </summary>
    public IReadOnlyDictionary<string, Rgba>? OutfitTints()
    {
        if (!Fork!.Extras.HasFlag(ProfileExtras.ItemCustomization) || Outfit() is not { } outfit || DressedJob() is not { } job)
            return null;
        var outfits = RequireContent().Outfits;
        var role = OutfitCatalog.RoleFor(job);
        var tints = new Dictionary<string, Rgba>(StringComparer.Ordinal);
        foreach (var (group, _) in LoadoutFor(job).Groups)
        {
            foreach (var entry in RequireFile().LoadoutEntries(role, group))
            {
                if (entry.Color == null || !Rgba.TryParse(entry.Color, out var color) || !outfits.Loadouts.TryGetValue(entry.Prototype, out var loadout)
                    || OutfitCatalog.SpawnCount(loadout) != 1 || loadout.Equipment.Count != 1)
                    continue;
                var (slot, item) = loadout.Equipment.First();
                if (outfit.GetValueOrDefault(slot) == item)
                    tints[slot] = CustomizationColor(color);
            }
        }
        return tints;
    }

    /// <summary>Euphoria's spawn-time fix-up: see-through turns pink, lightness clamped to 0.25..1.</summary>
    public static Rgba CustomizationColor(Rgba color)
    {
        if (color.A < 1f)
            color = new Rgba(1f, 192 / 255f, 203 / 255f);
        var (h, s, l, _) = color.ToHsl();
        return l is >= 0.25f and <= 1f ? color : Rgba.FromHsl(h, s, Math.Clamp(l, 0.25f, 1f));
    }

    /// <summary>A chosen loadout's name, description and colour, as saved.</summary>
    public LoadoutEntry? CustomizationOf(string jobId, string groupId, string loadoutId) =>
        RequireFile().LoadoutEntries(OutfitCatalog.RoleFor(jobId), groupId).FirstOrDefault(e => e.Prototype == loadoutId);

    public IReadOnlyList<RuleFix> SetCustomization(string jobId, string groupId, LoadoutEntry entry) =>
        Edit(f => f.SetLoadoutCustomization(OutfitCatalog.RoleFor(jobId), groupId, entry));

    /// <summary>Whether anything on the character moves, facing any of the four ways the preview draws (animated markings or clothes).</summary>
    public bool IsAnimated()
    {
        if (PreviewEntity() is { } entity)
            return Facings.Any(d => Renderer!.IsEntityAnimated(entity, d));
        var outfit = Outfit();
        return Facings.Any(d => Renderer!.IsAnimated(Look!, d, outfit));
    }

    // The character is drawn facing these; diagonals are for things placed in a room.
    private static readonly Direction[] Facings = [Direction.South, Direction.North, Direction.East, Direction.West];

    /// <summary>Each sprite folder on screen with its licence and credit, in drawing order.</summary>
    public IReadOnlyList<CreditLine> Credits()
    {
        if (Renderer == null || Look == null)
            return [];
        var layers = PreviewEntity() is { } entity ? EntitySprite.Layers(Content!.Prototypes, entity) : Renderer.Layers(Look, Outfit());
        return layers
            .Select(l => l.Sprite.Rsi)
            .Distinct(StringComparer.Ordinal)
            .Select(rsi => Renderer.Meta(rsi) is { } meta ? new CreditLine(rsi, meta.License, meta.Copyright) : new CreditLine(rsi, null, null))
            .ToList();
    }

    /// <summary>A random name for the species, already in the form the game's name rule leaves it (so "Vish'ra" is "Vish'Ra").</summary>
    public string RandomName(SpeciesInfo species, string? gender) => RandomNamer(RequireContent(), Fork!)(species, gender);

    /// <summary>
    /// Randomises the chosen parts, keeping the rest (its locks). Unlike the game's randomiser,
    /// which starts a fresh profile, jobs, loadouts, traits, antagonists, records and the
    /// description are kept.
    /// </summary>
    /// <param name="strength">How busy random markings get, 0 to 1; 1 is upstream's lobby randomiser.</param>
    public IReadOnlyList<RuleFix> Randomize(RandomParts parts, Random? random = null, float strength = 1f) => Edit(file =>
    {
        ClearDressUp();
        var catalog = RequireContent().Characters;
        var randomizer = new Randomizer(catalog, random ??= Random.Shared) { Strength = strength };
        var current = file.ReadLook(catalog);
        var choices = Selectable();
        var species = parts.HasFlag(RandomParts.Species) && choices.Count > 0
            ? choices[random.Next(choices.Count)]
            : catalog.Species.GetValueOrDefault(current.Species) ?? catalog.Species[CharacterRules.DefaultSpecies];
        var sex = parts.HasFlag(RandomParts.Sex) ? randomizer.Sex(species) : file.Sex ?? species.Sexes[0];
        var gender = parts.HasFlag(RandomParts.Pronouns) ? Randomizer.PronounsFor(sex) : file.Gender;

        file.Species = species.Id;
        file.Sex = sex;
        file.Gender = gender;
        // The game always gives a randomised character the species' voice for its sex.
        if (catalog.HasVoices && species.DefaultVoice(sex) is { } voice)
            file.Voice = voice;
        if (parts.HasFlag(RandomParts.Name))
            file.Name = RandomName(species, gender);
        if (parts.HasFlag(RandomParts.Age))
            file.Age = randomizer.Age(species);
        // Goob's randomiser picks height and width each anywhere in the species' range.
        if (parts.HasFlag(RandomParts.Size) && CharacterSize.HasWidth(Fork!))
        {
            var (height, width) = randomizer.Size(species);
            CharacterSize.WriteHeight(file, Fork!, height);
            CharacterSize.WriteWidth(file, width);
        }

        // Kept colours stand in for the palette's, so random markings match them.
        var palette = randomizer.RandomPalette(species);
        palette = palette with
        {
            Skin = parts.HasFlag(RandomParts.Skin) ? palette.Skin : current.SkinColor,
            Eyes = parts.HasFlag(RandomParts.Eyes) ? palette.Eyes : current.EyeColor,
        };
        file.WriteLook(new CharacterLook
        {
            Species = species.Id,
            Sex = sex,
            SkinColor = palette.Skin,
            EyeColor = palette.Eyes,
            Markings = parts.HasFlag(RandomParts.Markings) ? randomizer.Markings(species, sex, palette) : current.Markings,
        }, catalog);
    });

    public IReadOnlyList<RuleFix> RandomizeName() => Edit(f =>
        f.Name = RandomName(RequireContent().Characters.Species[Look!.Species], f.Gender));

    private void ApplyRules() => (LastFixes, Look) = Checked(RequireFile());

    // The game's rules applied to a file, and the look they leave it with.
    private (IReadOnlyList<RuleFix> Fixes, CharacterLook Look) Checked(CharacterFile file) => Checked(file, RequireContent(), Fork!);

    private static (IReadOnlyList<RuleFix> Fixes, CharacterLook Look) Checked(CharacterFile file, ForkContent content, ForkInfo fork)
    {
        var fixes = CharacterRules.EnsureValid(file, content.Characters, fork, RandomNamer(content, fork), content.Outfits, content.Traits);
        return (fixes, file.ReadLook(content.Characters));
    }

    // Random names for a fork, already in the form its name rule leaves them.
    private static Func<SpeciesInfo, string?, string> RandomNamer(ForkContent content, ForkInfo fork) =>
        (species, gender) => CharacterRules.CheckName(new NameGenerator(content.Prototypes, content.Strings).Next(species, gender), fork.NameRule);

    private ForkContent RequireContent() => Content ?? throw new InvalidOperationException("No fork is loaded.");
    private CharacterFile RequireFile() => File ?? throw new InvalidOperationException("No character is open.");

    private static Dictionary<string, Dictionary<string, List<MarkingEntry>>> Copy(Dictionary<string, Dictionary<string, List<MarkingEntry>>> markings) =>
        markings.ToDictionary(o => o.Key, o => o.Value.ToDictionary(l => l.Key, l => l.Value.ToList()));

    private static CharacterLook With(CharacterLook look, Dictionary<string, Dictionary<string, List<MarkingEntry>>> markings) =>
        new() { Species = look.Species, Sex = look.Sex, SkinColor = look.SkinColor, EyeColor = look.EyeColor, Markings = markings };

    public async ValueTask DisposeAsync()
    {
        if (_reader != null)
            await _reader.DisposeAsync();
    }
}
