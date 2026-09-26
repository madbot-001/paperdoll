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

        progress?.Report("Downloading character and clothing sprites");
        var textures = await FetchSpritesAsync(store, fork.Id, folders, ct);

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
        };
    }

    // Lists a few common ancestor folders rather than every sprite folder, which keeps the number
    // of listings (and of GitHub API requests for the backup store) small.
    private static async Task<IReadOnlyDictionary<string, string>> FetchSpritesAsync(
        IForkStore store, string forkId, IReadOnlySet<string> folders, CancellationToken ct)
    {
        var roots = folders
            .Select(f => f.Split('/'))
            .Select(parts => string.Join('/', parts.Take(Math.Min(3, parts.Length - 1))))
            .Distinct(StringComparer.Ordinal)
            .Select(r => r.Length == 0 ? TexturesFolder : $"{TexturesFolder}/{r}")
            .ToList();
        var listed = await store.ListAsync(forkId, RemoveNested(roots), ct);

        var wanted = listed
            .Where(e => folders.Contains(FolderOf(e.Path)))
            .ToList();
        await store.FetchAsync(forkId, wanted, ct);
        return wanted.ToDictionary(e => e.Path[(TexturesFolder.Length + 1)..], e => e.ObjectId, StringComparer.Ordinal);
    }

    // "Resources/Textures/Mobs/Species/Human/parts.rsi/torso_m.png" -> "Mobs/Species/Human/parts.rsi"
    private static string FolderOf(string path)
    {
        var relative = path[(TexturesFolder.Length + 1)..];
        var slash = relative.LastIndexOf('/');
        return slash < 0 ? "" : relative[..slash];
    }

    private static List<string> RemoveNested(List<string> folders) =>
        folders.Where(f => !folders.Any(o => o != f && f.StartsWith(o + "/", StringComparison.Ordinal))).ToList();
}
