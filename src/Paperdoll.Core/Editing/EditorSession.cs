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

/// <summary>A sprite on screen and its licence, for the credits pane.</summary>
public sealed record CreditLine(string Rsi, string? License, string? Copyright)
{
    public bool NonCommercial => License?.Contains("NC", StringComparison.Ordinal) == true;
}

/// <summary>
/// Everything the editor window works on, without the window. Every edit runs through the game's
/// rules, so the character stays one the game would accept.
/// </summary>
public sealed class EditorSession : IAsyncDisposable
{
    private IBlobReader? _reader;

    /// <summary>A session over a given store; <see cref="OpenAsync"/> picks the usual one.</summary>
    public EditorSession(IForkStore store) => Store = store;

    public IForkStore Store { get; }
    public string StoreKind => Store is GitForkStore ? "git" : "GitHub API";

    public ForkInfo? Fork { get; private set; }
    public ForkContent? Content { get; private set; }
    public PaperdollRenderer? Renderer { get; private set; }

    public CharacterFile? File { get; private set; }
    public CharacterLook? Look { get; private set; }

    /// <summary>What the rules changed on the last edit, open or new character.</summary>
    public IReadOnlyList<RuleFix> LastFixes { get; private set; } = [];

    public static async Task<EditorSession> OpenAsync(string? directory = null, CancellationToken ct = default) =>
        new(await ForkStores.OpenAsync(directory ?? ForkStores.DefaultDirectory, ct: ct));

    public async Task<IReadOnlyList<ForkStatus>> ForkStatusesAsync(CancellationToken ct = default)
    {
        var list = new List<ForkStatus>();
        foreach (var fork in KnownForks.All)
            list.Add(new ForkStatus(fork, await Store.CommitOfAsync(fork.Id, ct)));
        return list;
    }

    /// <summary>
    /// Makes a fork the one being edited: downloads it if needed, reads its data and sprites. An
    /// open character is checked against the new data rather than replaced; otherwise a new one starts.
    /// </summary>
    public async Task LoadForkAsync(ForkInfo fork, bool update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (update || await Store.CommitOfAsync(fork.Id, ct) == null)
        {
            progress?.Report($"Checking {fork.Name} for its newest version");
            await Store.SyncAsync(fork, ct);
        }

        var content = await ForkContent.LoadAsync(Store, fork, progress, ct);
        progress?.Report("Reading sprites");
        _reader ??= Store.OpenReader();
        var textures = await MemoryTextures.LoadAsync(content, _reader, ct);

        Fork = fork;
        Content = content;
        Renderer = new PaperdollRenderer(content.Characters, content.Prototypes, textures);
        if (File != null)
            ApplyRules();
        else
        {
            NewCharacter(content.Characters.Species.ContainsKey(CharacterRules.DefaultSpecies)
                ? CharacterRules.DefaultSpecies
                : Selectable().First().Id);
        }
        progress?.Report($"{fork.Name} ready");
    }

    public Task RemoveForkAsync(ForkInfo fork, CancellationToken ct = default) => Store.RemoveAsync(fork.Id, ct);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Loads the fork a server runs, at the commit it was built from, so the choices match that
    /// server rather than the fork's newest code. Update goes back to the newest.
    /// </summary>
    public async Task<(ForkInfo Fork, string Commit)> MatchServerAsync(string address, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Asking the server what it runs");
        var build = await new Servers.GameServers(Http).BuildAsync(address, ct);
        var fork = KnownForks.FindByServerForkId(build.ForkId ?? "")
            ?? throw new InvalidOperationException($"This server runs {build.ForkId ?? "a fork it does not name"}, which Paperdoll does not know.");
        if (!new ForkStatus(fork, null).Editable)
            throw new InvalidOperationException($"This server runs {fork.Name}, which Paperdoll cannot edit yet.");
        if (!build.IsCommit)
            throw new InvalidOperationException($"This server gives its version as {build.Version ?? "nothing"}, not a git commit, so it cannot be matched.");

        progress?.Report($"Getting {fork.Name} at the server's version");
        var commit = await Store.SyncToCommitAsync(fork, build.Version!, ct);
        await LoadForkAsync(fork, update: false, progress, ct);
        return (fork, commit);
    }

    /// <summary>Frees space old fork versions left behind. Returns the bytes freed.</summary>
    public async Task<long> CleanUpStoreAsync(CancellationToken ct = default)
    {
        // Close the reader first; it is opened again when next needed.
        if (_reader != null)
        {
            await _reader.DisposeAsync();
            _reader = null;
        }
        return await Store.CleanUpAsync(ct);
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
        var catalog = RequireContent().Characters;
        var species = catalog.Species[speciesId];
        var file = CharacterFile.CreateNew(Fork!.ServerForkIds.FirstOrDefault() ?? Fork.Id);
        file.Name = RandomName(species, "Epicene");
        file.Age = Math.Max(species.MinAge, Math.Min(species.YoungAge, species.MaxAge));
        file.Gender = "Epicene";
        file.WriteLook(LookDefaults.Create(catalog, speciesId, species.Sexes[0], catalog.DefaultSkin(species), Rgba.Parse("#000000")), catalog);
        // The species' default size, which the lobby's reset buttons give.
        if (CharacterSize.HasHeight(Fork))
            CharacterSize.WriteHeight(file, Fork, CharacterSize.CheckHeight(species.DefaultHeight, species, Fork));
        if (CharacterSize.HasWidth(Fork))
            CharacterSize.WriteWidth(file, CharacterSize.CheckWidth(species.DefaultWidth, species));
        // The game starts every profile on MaleHuman; its rules then give species that cannot use it their default.
        if (catalog.HasVoices)
            file.Voice = species.Voices.Contains(CharacterRules.DefaultVoice) ? CharacterRules.DefaultVoice : species.DefaultVoice(species.Sexes[0]);
        var (fixes, look) = Checked(file);
        (File, LastFixes, Look) = (file, fixes, look);
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

    /// <summary>What the preview puts on the character, slot to item; null when clothes are off.</summary>
    public IReadOnlyDictionary<string, string>? Outfit()
    {
        if (!ShowClothes || DressedJob() is not { } job)
            return null;
        return RequireContent().Outfits.OutfitFor(job, LoadoutFor(job));
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

    public IReadOnlyList<RuleFix> SetRecordText(string key, string value) => Edit(f => CharacterRecords.SetText(f, key, value));

    public IReadOnlyList<RuleFix> SetRecordNumber(string key, int value) => Edit(f => CharacterRecords.SetNumber(f, key, value));

    public IReadOnlyList<RuleFix> SetWorkAuthorization(bool value) => Edit(f => CharacterRecords.SetWorkAuthorization(f, value));

    public IReadOnlyList<RuleFix> SetRecordEntries(string list, IReadOnlyList<RecordEntry> entries) => Edit(f => CharacterRecords.SetEntries(f, list, entries));

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
    public IReadOnlyList<RuleFix> Open(string yaml)
    {
        RequireContent();
        var file = CharacterFile.Parse(yaml);
        var (fixes, look) = Checked(file);
        (File, LastFixes, Look) = (file, fixes, look);
        return fixes;
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
        ApplyRules();
        return LastFixes;
    }

    /// <summary>Changes the look (markings, colours), then applies the game's rules.</summary>
    public IReadOnlyList<RuleFix> EditLook(Func<CharacterLook, CharacterLook> change)
    {
        var file = RequireFile();
        file.WriteLook(change(Look!), RequireContent().Characters);
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
    public IReadOnlyList<MarkingInfo> AvailableMarkings(OrganInfo organ, string layer)
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

    /// <summary>Whether anything on the character moves, facing any way (animated markings or clothes).</summary>
    public bool IsAnimated()
    {
        if (PreviewEntity() is { } entity)
            return Enum.GetValues<Direction>().Any(d => Renderer!.IsEntityAnimated(entity, d));
        var outfit = Outfit();
        return Enum.GetValues<Direction>().Any(d => Renderer!.IsAnimated(Look!, d, outfit));
    }

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
    public string RandomName(SpeciesInfo species, string? gender) =>
        CharacterRules.CheckName(new NameGenerator(RequireContent().Prototypes, Content!.Strings).Next(species, gender), Fork!.NameRule);

    /// <summary>
    /// Randomises the chosen parts, keeping the rest (its locks). Unlike the game's randomiser,
    /// which starts a fresh profile, jobs, loadouts, traits, antagonists, records and the
    /// description are kept.
    /// </summary>
    /// <param name="strength">How busy random markings get, 0 to 1; 1 is upstream's lobby randomiser.</param>
    public IReadOnlyList<RuleFix> Randomize(RandomParts parts, Random? random = null, float strength = 1f) => Edit(file =>
    {
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
    private (IReadOnlyList<RuleFix> Fixes, CharacterLook Look) Checked(CharacterFile file)
    {
        var fixes = CharacterRules.EnsureValid(file, Content!.Characters, Fork!, RandomName, Content.Outfits, Content.Traits);
        return (fixes, file.ReadLook(Content.Characters));
    }

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
