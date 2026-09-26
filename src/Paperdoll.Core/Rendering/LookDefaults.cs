using Paperdoll.Core.Characters;

namespace Paperdoll.Core.Rendering;

public static class LookDefaults
{
    /// <summary>
    /// A new character of the species: the given colours, and every required marking layer filled
    /// with its group's default markings, coloured by their rules.
    /// </summary>
    public static CharacterLook Create(CharacterCatalog catalog, string speciesId, string sex, Rgba skin, Rgba eyes)
    {
        var species = catalog.Species[speciesId];
        var markings = new Dictionary<string, Dictionary<string, List<MarkingEntry>>>();

        foreach (var organ in species.Organs)
        {
            if (organ.MarkingGroup == null || !catalog.MarkingsGroups.TryGetValue(organ.MarkingGroup, out var group))
                continue;
            foreach (var layer in organ.MarkingLayers)
            {
                if (!group.Limits.TryGetValue(layer, out var limit) || !limit.Required)
                    continue;
                var entries = new List<MarkingEntry>();
                foreach (var id in limit.Default)
                {
                    if (catalog.Markings.TryGetValue(id, out var marking))
                        entries.Add(new MarkingEntry(id, MarkingColoring.LayerColors(marking, skin, eyes, entries)));
                }
                if (entries.Count == 0)
                    continue;
                if (!markings.TryGetValue(organ.Category, out var byLayer))
                    markings[organ.Category] = byLayer = [];
                byLayer[layer] = entries;
            }
        }

        return new CharacterLook { Species = speciesId, Sex = sex, SkinColor = skin, EyeColor = eyes, Markings = markings };
    }
}

/// <summary>Sprite files held in memory by path under <c>Resources/Textures</c>.</summary>
public sealed class MemoryTextures(IReadOnlyDictionary<string, byte[]> files) : ITextureSource
{
    public byte[]? Read(string path) => files.GetValueOrDefault(path);

    /// <summary>Reads all of a fork's character sprite files out of the store.</summary>
    public static async Task<MemoryTextures> LoadAsync(Forks.ForkContent content, Store.IBlobReader reader, CancellationToken ct = default)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, id) in content.TextureFiles)
        {
            if (await reader.ReadAsync(id, ct) is { } data)
                files[path] = data;
        }
        return new MemoryTextures(files);
    }
}
