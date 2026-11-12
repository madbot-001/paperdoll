using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Locale;
using Paperdoll.Core.Outfits;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Store;

namespace Paperdoll.Core.Forks;

/// <summary>What Paperdoll reads from one synced fork: prototypes, English strings and characters.</summary>
public sealed class ForkContent
{
    public const string PrototypesFolder = "Resources/Prototypes";
    public const string LocaleFolder = "Resources/Locale/en-US";
    public const string TexturesFolder = "Resources/Textures";

    public required ForkInfo Fork { get; init; }
    public required string Commit { get; init; }
    public required PrototypeIndex Prototypes { get; init; }
    public required FluentStrings Strings { get; init; }
    public required CharacterCatalog Characters { get; init; }
    public required OutfitCatalog Outfits { get; init; }
    public required Traits.TraitCatalog Traits { get; init; }

    /// <summary>Every file under the character sprite folders, by path, once fetched.</summary>
    public required IReadOnlyDictionary<string, string> TextureFiles { get; init; }

    /// <summary>The clothing that can be tried on in the preview, by slot.</summary>
    public Wardrobe Wardrobe { get; init; } = new() { BySlot = new Dictionary<string, IReadOnlyList<string>>() };

    /// <summary>
    /// The files of the wardrobe's sprite folders that were listed but not fetched with the fork,
    /// by path: each item's are fetched when it is first tried on.
    /// </summary>
    public IReadOnlyDictionary<string, StoreEntry> WardrobeFiles { get; init; } = new Dictionary<string, StoreEntry>();

    /// <summary>
    /// Fetches and reads a synced fork: prototypes and English strings first, then every sprite
    /// folder that species and markings refer to.
    /// </summary>
    public static async Task<ForkContent> LoadAsync(IForkStore store, ForkInfo fork, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var commit = await store.CommitOfAsync(fork.Id, ct)
            ?? throw new InvalidOperationException($"{fork.Name} has not been synced.");

        progress?.Report($"Listing {fork.Name}'s prototypes and strings");
        var entries = await store.ListAsync(fork.Id, [PrototypesFolder, LocaleFolder], ct);
        progress?.Report($"Downloading {entries.Count} files");
        await store.FetchAsync(fork.Id, entries, ct);

        var sources = new List<PrototypeSource>();
        var strings = new FluentStrings();
        await using (var reader = store.OpenReader())
        {
            foreach (var entry in entries)
            {
                var data = await reader.ReadAsync(entry.ObjectId, ct)
                    ?? throw new InvalidOperationException($"{entry.Path} is missing from the store.");
                if (entry.Path.StartsWith(PrototypesFolder + "/", StringComparison.Ordinal))
                    sources.Add(new PrototypeSource(entry.Path[(PrototypesFolder.Length + 1)..], data));
                else if (entry.Path.EndsWith(".ftl", StringComparison.Ordinal))
                    strings.Add(Encoding.UTF8.GetString(data));
            }
        }

        progress?.Report("Reading prototypes");
        var prototypes = PrototypeIndex.Load(sources);
        var characters = CharacterCatalog.Build(prototypes, fork.DefaultHeights);
        var outfits = OutfitCatalog.Build(prototypes);

        // Clothing folders come from the items' prototypes; whether a sprite has a species version
        // is only known once its meta.json is here, so no meta is needed to list them.
        var folders = new HashSet<string>(characters.SpriteFolders(), StringComparer.Ordinal);
        // Jobs the lobby shows as their own body, such as a borg.
        foreach (var job in outfits.Jobs.Values.Where(j => j.PreviewEntity != null))
            folders.UnionWith(Rendering.EntitySprite.SpriteFolders(prototypes, job.PreviewEntity!));
        var clothing = new ClothingResolver(prototypes, _ => null);
        foreach (var entity in outfits.AllGearEntities())
            folders.UnionWith(clothing.SpriteFolders(entity));
        // The pictures beside loadouts, including items that are carried rather than worn.
        foreach (var loadout in outfits.Loadouts.Values)
        {
            if (outfits.PictureOf(loadout) is { } picture)
                folders.UnionWith(Rendering.EntitySprite.SpriteFolders(prototypes, picture));
        }

        // The rest of the clothing is only listed: an item's files are fetched when it is tried on.
        var wardrobe = Wardrobe.Build(prototypes);
        var wardrobeFolders = new HashSet<string>(wardrobe.Entities().SelectMany(clothing.SpriteFolders), StringComparer.Ordinal);
        wardrobeFolders.ExceptWith(folders);

        progress?.Report("Downloading character and clothing sprites");
        var (textures, wardrobeFiles) = await FetchSpritesAsync(store, fork.Id, folders, wardrobeFolders, ct);

        return new ForkContent
        {
            Fork = fork,
            Commit = commit,
            Prototypes = prototypes,
            Strings = strings,
            Characters = characters,
            Outfits = outfits,
            Traits = Paperdoll.Core.Traits.TraitCatalog.Build(prototypes),
            TextureFiles = textures,
            Wardrobe = wardrobe,
            WardrobeFiles = wardrobeFiles,
        };
    }

    // One listing for both: the folders to fetch now, and the ones only listed for later.
    private static async Task<(IReadOnlyDictionary<string, string> Fetched, IReadOnlyDictionary<string, StoreEntry> Listed)> FetchSpritesAsync(
        IForkStore store, string forkId, IReadOnlySet<string> folders, IReadOnlySet<string> later, CancellationToken ct)
    {
        var listed = await store.ListAsync(forkId, ListingRoots(folders.Concat(later)), ct);

        var wanted = listed
            .Where(e => folders.Contains(FolderOf(e.Path)))
            .ToList();
        await store.FetchAsync(forkId, wanted, ct);
        return (wanted.ToDictionary(e => e.Path[(TexturesFolder.Length + 1)..], e => e.ObjectId, StringComparer.Ordinal),
            listed.Where(e => later.Contains(FolderOf(e.Path))).ToDictionary(e => e.Path[(TexturesFolder.Length + 1)..], StringComparer.Ordinal));
    }

    // "Resources/Textures/Mobs/Species/Human/parts.rsi/torso_m.png" -> "Mobs/Species/Human/parts.rsi"
    private static string FolderOf(string path)
    {
        var relative = path[(TexturesFolder.Length + 1)..];
        var slash = relative.LastIndexOf('/');
        return slash < 0 ? "" : relative[..slash];
    }

    /// <summary>
    /// The folders to list to find the given sprite folders: the ones just below Textures that
    /// hold them (Mobs, Clothing, Objects...). A dozen or so listings, where one per sprite folder
    /// would be a hundred or more: the GitHub API store allows only 60 requests an hour.
    /// </summary>
    public static IReadOnlyList<string> ListingRoots(IEnumerable<string> folders) => RemoveNested(folders
        .Select(f => f.Split('/'))
        .Select(parts => parts.Length > 1 ? $"{TexturesFolder}/{parts[0]}" : TexturesFolder)
        .Distinct(StringComparer.Ordinal)
        .ToList());

    private static List<string> RemoveNested(List<string> folders) =>
        folders.Where(f => !folders.Any(o => o != f && f.StartsWith(o + "/", StringComparison.Ordinal))).ToList();
}
