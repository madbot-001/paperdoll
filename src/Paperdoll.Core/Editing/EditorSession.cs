using Paperdoll.Core.Characters;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Editing;

/// <summary>A known fork and whether it has been downloaded.</summary>
public sealed record ForkStatus(ForkInfo Fork, string? Commit)
{
    public bool Downloaded => Commit != null;

    /// <summary>Paperdoll can edit forks on the new appearance model.</summary>
    public bool Editable => Fork.Model == AppearanceModel.New;
}

/// <summary>A sprite on screen and its licence, for the credits pane.</summary>
public sealed record CreditLine(string Rsi, string? License, string? Copyright)
{
    public bool NonCommercial => License?.Contains("NC", StringComparison.Ordinal) == true;
}

/// <summary>
/// Everything the editor window works on, without the window: the fork store, the loaded fork,
/// and the character being edited. Every edit goes through the game's rules, so the character is
/// always one the game would accept.
/// </summary>
public sealed class EditorSession : IAsyncDisposable
{
    private IBlobReader? _reader;

    private EditorSession(IForkStore store) => Store = store;

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
    /// Makes a fork the one being edited: downloads it if it never was (or when asked to update),
    /// reads its data and sprites, and starts a new character.
    /// </summary>
    public async Task LoadForkAsync(ForkInfo fork, bool update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (fork.Model != AppearanceModel.New)
            throw new NotSupportedException($"{fork.Name} uses the old appearance model, which Paperdoll cannot edit yet.");

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
        NewCharacter(content.Characters.Species.ContainsKey(CharacterRules.DefaultSpecies)
            ? CharacterRules.DefaultSpecies
            : Selectable().First().Id);
        progress?.Report($"{fork.Name} ready");
    }

    public Task RemoveForkAsync(ForkInfo fork, CancellationToken ct = default) => Store.RemoveAsync(fork.Id, ct);

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
        file.WriteLook(LookDefaults.Create(catalog, speciesId, species.Sexes[0], catalog.DefaultSkin(species), Rgba.Parse("#000000")));
        if (CharacterSize.HasHeight(Fork))
            CharacterSize.WriteHeight(file, CharacterSize.CheckHeight(species.DefaultHeight, species));
        File = file;
        ApplyRules();
    }

    /// <summary>How much the character is scaled on screen, across and up.</summary>
    public (float X, float Y) SpriteScale()
    {
        var species = RequireContent().Characters.Species[Look!.Species];
        return CharacterSize.SpriteScale(Fork!, species, RequireFile());
    }

    /// <summary>Sets the character's height (forks with a height setting only).</summary>
    public IReadOnlyList<RuleFix> SetHeight(float height) => Edit(f => CharacterSize.WriteHeight(f, height));

    /// <summary>Opens an exported character and fits it to the loaded fork.</summary>
    public IReadOnlyList<RuleFix> Open(string yaml)
    {
        RequireContent();
        File = CharacterFile.Parse(yaml);
        ApplyRules();
        return LastFixes;
    }

    /// <summary>The character as the lobby imports it.</summary>
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
        file.WriteLook(change(Look!));
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
        if (organ.MarkingGroup == null || !catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group))
            return [];
        return catalog.Markings.Values
            .Where(m => m.Layer == layer && CharacterRules.CanBeApplied(group, Look!.Sex, m))
            .OrderBy(m => MarkingName(m.Id), StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>How many markings the layer may hold; null when the group sets no limit.</summary>
    public int? LayerLimit(OrganInfo organ, string layer)
    {
        var catalog = RequireContent().Characters;
        return organ.MarkingGroup != null && catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group)
            && group.Limits.TryGetValue(layer, out var limit) ? limit.Limit : null;
    }

    /// <summary>Adds a marking with its default colours.</summary>
    public IReadOnlyList<RuleFix> AddMarking(string organ, string layer, string markingId) => EditLook(look =>
    {
        var marking = RequireContent().Characters.Markings[markingId];
        var markings = Copy(look.Markings);
        if (!markings.TryGetValue(organ, out var byLayer))
            markings[organ] = byLayer = [];
        if (!byLayer.TryGetValue(layer, out var list))
            byLayer[layer] = list = [];
        list.Add(new MarkingEntry(markingId, MarkingColoring.LayerColors(marking, look.SkinColor, look.EyeColor, list)));
        return With(look, markings);
    });

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

    public SKBitmap Render(Direction direction = Direction.South) => Renderer!.Render(Look!, direction);

    /// <summary>Each sprite folder on screen with its licence and credit, in drawing order.</summary>
    public IReadOnlyList<CreditLine> Credits()
    {
        if (Renderer == null || Look == null)
            return [];
        return Renderer.Layers(Look)
            .Select(l => l.Sprite.Rsi)
            .Distinct(StringComparer.Ordinal)
            .Select(rsi => Renderer.Meta(rsi) is { } meta ? new CreditLine(rsi, meta.License, meta.Copyright) : new CreditLine(rsi, null, null))
            .ToList();
    }

    /// <summary>A random name for the species and pronouns, as the game makes them.</summary>
    public string RandomName(SpeciesInfo species, string? gender) =>
        new NameGenerator(RequireContent().Prototypes, Content!.Strings).Next(species, gender);

    /// <summary>Gives the character a new random name.</summary>
    public IReadOnlyList<RuleFix> RandomizeName() => Edit(f =>
        f.Name = RandomName(RequireContent().Characters.Species[Look!.Species], f.Gender));

    private void ApplyRules()
    {
        var file = RequireFile();
        LastFixes = CharacterRules.EnsureValid(file, Content!.Characters, Fork!, RandomName);
        Look = file.ReadLook(Content.Characters);
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
