using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Characters;

/// <summary>A sprite: an RSI folder under <c>Resources/Textures</c> and a state in it.</summary>
public readonly record struct SpriteRef(string Rsi, string? State);

/// <summary>
/// A displacement map: how the game reshapes a layer to fit a body (Dwarf bodies, hair on Vox).
/// Each map pixel moves the layer's pixel by (red - 128, green - 128) and masks it by its alpha.
/// Maps are chosen by the layer's frame size; 32 is the default.
/// </summary>
public sealed record DisplacementRef(IReadOnlyDictionary<int, SpriteRef> SizeMaps)
{
    public SpriteRef? For(int frameSize) =>
        SizeMaps.TryGetValue(frameSize, out var map) ? map : SizeMaps.TryGetValue(32, out var fallback) ? fallback : null;
}

/// <summary>A species prototype (new appearance model).</summary>
public sealed record SpeciesInfo(
    string Id,
    string NameKey,
    bool RoundStart,
    string? Prototype,
    string? DollPrototype,
    string? SkinColoration,
    string? DefaultSkinTone,
    IReadOnlyList<string> Sexes,
    string Naming,
    int MinAge,
    int YoungAge,
    int OldAge,
    int MaxAge,
    IReadOnlyList<OrganInfo> Organs,
    YamlMappingNode Node);

/// <summary>
/// A body organ from a species' doll, as it is drawn: its own layer and sprite, and the marking
/// layers and marking group it accepts.
/// </summary>
public sealed record OrganInfo(
    string Category,
    string EntityId,
    string? Layer,
    SpriteRef? Sprite,
    IReadOnlyDictionary<string, string> SexStates,
    IReadOnlyList<string> MarkingLayers,
    string? MarkingGroup)
{
    /// <summary>The map reshaping the organ's own layer, if any.</summary>
    public DisplacementRef? Displacement { get; init; }

    /// <summary>Maps reshaping markings on the organ's layers, by layer.</summary>
    public IReadOnlyDictionary<string, DisplacementRef> MarkingsDisplacement { get; init; } = new Dictionary<string, DisplacementRef>();
}

/// <summary>How many markings a layer takes, and which it starts with.</summary>
public sealed record LayerLimit(
    int Limit,
    bool Required,
    bool? OnlyGroupWhitelisted,
    IReadOnlyList<string> Default,
    IReadOnlyList<string> NudityDefault);

public sealed record MarkingsGroupInfo(string Id, bool OnlyGroupWhitelisted, IReadOnlyDictionary<string, LayerLimit> Limits);

public sealed record MarkingInfo(
    string Id,
    string Layer,
    IReadOnlyList<string>? GroupWhitelist,
    string? SexRestriction,
    bool ForcedColoring,
    IReadOnlyList<SpriteRef> Sprites,
    YamlMappingNode Node)
{
    /// <summary>Whether the organ's displacement map for this layer applies (the game's <c>canBeDisplaced</c>).</summary>
    public bool CanBeDisplaced { get; init; } = true;
}

/// <summary>
/// The character data in one fork: species with their organs, marking groups, markings and skin
/// colorations. New appearance model only; old-model species load with no organs.
/// </summary>
public sealed class CharacterCatalog
{
    private const string LayerPrefix = "enum.HumanoidVisualLayers.";

    public required IReadOnlyDictionary<string, SpeciesInfo> Species { get; init; }
    public required IReadOnlyDictionary<string, MarkingsGroupInfo> MarkingsGroups { get; init; }
    public required IReadOnlyDictionary<string, MarkingInfo> Markings { get; init; }
    public required IReadOnlyDictionary<string, YamlMappingNode> SkinColorations { get; init; }

    /// <summary>Species a player can pick: round-start and not hidden by the fork.</summary>
    public IEnumerable<SpeciesInfo> Selectable(IEnumerable<string> hidden)
    {
        var hide = hidden.ToHashSet(StringComparer.Ordinal);
        return Species.Values.Where(s => s.RoundStart && !hide.Contains(s.Id)).OrderBy(s => s.Id, StringComparer.Ordinal);
    }

    /// <summary>The species' skin colour rule; any colour if the species names none.</summary>
    public SkinColoration SkinRuleFor(SpeciesInfo species) =>
        SkinColoration.Read(species.SkinColoration != null && SkinColorations.TryGetValue(species.SkinColoration, out var node)
            ? node
            : new YamlMappingNode());

    /// <summary>A new character's skin colour for the species, as the game picks it.</summary>
    public Rendering.Rgba DefaultSkin(SpeciesInfo species) => SkinColoration.DefaultFor(species, SkinRuleFor(species));

    /// <summary>Every RSI folder the species' organs and the markings draw from.</summary>
    public IReadOnlySet<string> SpriteFolders()
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var organ in Species.Values.SelectMany(s => s.Organs))
        {
            if (organ.Sprite is { } sprite)
                folders.Add(sprite.Rsi);
        }
        foreach (var marking in Markings.Values)
            folders.UnionWith(marking.Sprites.Select(s => s.Rsi));
        foreach (var organ in Species.Values.SelectMany(s => s.Organs))
        {
            foreach (var map in organ.MarkingsDisplacement.Values.Append(organ.Displacement).OfType<DisplacementRef>())
                folders.UnionWith(map.SizeMaps.Values.Select(m => m.Rsi));
        }
        return folders;
    }

    public static CharacterCatalog Build(PrototypeIndex index)
    {
        var species = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);
        foreach (var proto in index.OfKind("species"))
        {
            var node = index.Resolve("species", proto.Id)!;
            species[proto.Id] = ReadSpecies(index, proto.Id, node);
        }

        var groups = new Dictionary<string, MarkingsGroupInfo>(StringComparer.Ordinal);
        foreach (var proto in index.OfKind("markingsGroup").Where(p => !p.Abstract))
            groups[proto.Id] = ReadGroup(proto.Id, index.Resolve("markingsGroup", proto.Id)!);

        var markings = new Dictionary<string, MarkingInfo>(StringComparer.Ordinal);
        foreach (var proto in index.OfKind("marking"))
            markings[proto.Id] = ReadMarking(proto.Id, index.Resolve("marking", proto.Id)!);

        var colorations = index.OfKind("skinColoration")
            .ToDictionary(p => p.Id, p => index.Resolve("skinColoration", p.Id)!, StringComparer.Ordinal);

        return new CharacterCatalog
        {
            Species = species,
            MarkingsGroups = groups,
            Markings = markings,
            SkinColorations = colorations,
        };
    }

    private static SpeciesInfo ReadSpecies(PrototypeIndex index, string id, YamlMappingNode node)
    {
        var doll = Str(node, "dollPrototype");
        // Old-model species name base sprites under "sprites" and have no organs.
        var organs = doll != null && Get(node, "sprites") == null ? ReadOrgans(index, doll) : [];

        return new SpeciesInfo(
            id,
            Str(node, "name") ?? id,
            Bool(node, "roundStart") ?? false,
            Str(node, "prototype"),
            doll,
            Str(node, "skinColoration"),
            Str(node, "defaultSkinTone"),
            Strings(node, "sexes") ?? ["Male", "Female"],
            Str(node, "naming") ?? "FirstLast",
            Int(node, "minAge") ?? 18,
            Int(node, "youngAge") ?? 30,
            Int(node, "oldAge") ?? 60,
            Int(node, "maxAge") ?? 120,
            organs,
            node);
    }

    private static List<OrganInfo> ReadOrgans(PrototypeIndex index, string dollId)
    {
        var doll = index.Resolve("entity", dollId);
        if (doll == null || Component(doll, "InitialBody") is not { } body || Get(body, "organs") is not YamlMappingNode organs)
            return [];

        var result = new List<OrganInfo>();
        foreach (var (categoryNode, organNode) in organs.Children)
        {
            var organId = ((YamlScalarNode)organNode).Value!;
            var organ = index.Resolve("entity", organId);
            if (organ == null)
                continue;
            // Saved markings are keyed by the organ's own category, which normally matches the slot.
            var category = Str(Component(organ, "Organ"), "category") ?? ((YamlScalarNode)categoryNode).Value!;

            var visual = Component(organ, "VisualOrgan");
            var marks = Component(organ, "VisualOrganMarkings");
            // Not drawn (brain, lungs...) and takes no markings: nothing to show.
            if (visual == null && marks == null)
                continue;

            SpriteRef? sprite = null;
            IReadOnlyDictionary<string, string> sexStates = new Dictionary<string, string>();
            if (visual != null)
            {
                var data = Get(visual, "data") as YamlMappingNode;
                var rsi = (data != null ? Str(data, "sprite") : null) ?? Str(Component(organ, "Sprite"), "sprite");
                var state = (data != null ? Str(data, "state") : null) ?? Str(Component(organ, "Sprite"), "state");
                if (rsi != null)
                    sprite = new SpriteRef(TexturePath(rsi), state);
                if (Get(visual, "sexStateOverrides") is YamlMappingNode overrides)
                    sexStates = overrides.Children.ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => ((YamlScalarNode)kv.Value).Value!);
            }

            var markingData = marks != null ? Get(marks, "markingData") as YamlMappingNode : null;
            var markingsDisplacement = new Dictionary<string, DisplacementRef>(StringComparer.Ordinal);
            if (marks != null && Get(marks, "markingsDisplacement") is YamlMappingNode byLayer)
            {
                foreach (var (layerKey, data) in byLayer.Children)
                {
                    if (ReadDisplacement(data as YamlMappingNode) is { } map)
                        markingsDisplacement[Layer(((YamlScalarNode)layerKey).Value)!] = map;
                }
            }
            DisplacementRef? displacement = null;
            if (visual != null && Str(visual, "displacement") is { } displacementId
                && index.Resolve("displacementData", displacementId) is { } displacementProto)
                displacement = ReadDisplacement(Get(displacementProto, "displacement") as YamlMappingNode);

            result.Add(new OrganInfo(
                category,
                organId,
                visual != null ? Layer(Str(visual, "layer")) : null,
                sprite,
                sexStates,
                (markingData != null ? Strings(markingData, "layers") : null)?.Select(l => Layer(l)!).ToList() ?? [],
                markingData != null ? Str(markingData, "group") : null)
            {
                Displacement = displacement,
                MarkingsDisplacement = markingsDisplacement,
            });
        }
        return result;
    }

    private static MarkingsGroupInfo ReadGroup(string id, YamlMappingNode node)
    {
        var limits = new Dictionary<string, LayerLimit>(StringComparer.Ordinal);
        if (Get(node, "limits") is YamlMappingNode map)
        {
            foreach (var (key, value) in map.Children)
            {
                if (value is not YamlMappingNode limit)
                    continue;
                limits[Layer(((YamlScalarNode)key).Value)!] = new LayerLimit(
                    Int(limit, "limit") ?? 0,
                    Bool(limit, "required") ?? false,
                    Bool(limit, "onlyGroupWhitelisted"),
                    Strings(limit, "default") ?? [],
                    Strings(limit, "nudityDefault") ?? []);
            }
        }
        return new MarkingsGroupInfo(id, Bool(node, "onlyGroupWhitelisted") ?? false, limits);
    }

    private static MarkingInfo ReadMarking(string id, YamlMappingNode node)
    {
        var sprites = new List<SpriteRef>();
        if (Get(node, "sprites") is YamlSequenceNode list)
        {
            foreach (var item in list.Children)
            {
                if (item is YamlMappingNode spec && Str(spec, "sprite") is { } rsi)
                    sprites.Add(new SpriteRef(TexturePath(rsi), Str(spec, "state")));
            }
        }
        return new MarkingInfo(
            id,
            Layer(Str(node, "bodyPart")) ?? "",
            Strings(node, "groupWhitelist"),
            Str(node, "sexRestriction"),
            Bool(node, "forcedColoring") ?? false,
            sprites,
            node)
        {
            CanBeDisplaced = Bool(node, "canBeDisplaced") ?? true,
        };
    }

    // A DisplacementData block: sizeMaps: { 32: { sprite: ..., state: ... } }.
    private static DisplacementRef? ReadDisplacement(YamlMappingNode? data)
    {
        if (data == null || Get(data, "sizeMaps") is not YamlMappingNode sizes)
            return null;
        var maps = new Dictionary<int, SpriteRef>();
        foreach (var (sizeKey, layer) in sizes.Children)
        {
            if (int.TryParse(((YamlScalarNode)sizeKey).Value, out var size) && layer is YamlMappingNode l && Str(l, "sprite") is { } rsi)
                maps[size] = new SpriteRef(TexturePath(rsi), Str(l, "state"));
        }
        return maps.Count == 0 ? null : new DisplacementRef(maps);
    }

    /// <summary>
    /// A sprite path relative to <c>Resources/Textures</c>. Prototypes write it either way:
    /// <c>Mobs/…</c> or <c>/Textures/Mobs/…</c>.
    /// </summary>
    public static string TexturePath(string path)
    {
        path = path.TrimStart('/');
        return path.StartsWith("Textures/", StringComparison.Ordinal) ? path["Textures/".Length..] : path;
    }

    private static string? Layer(string? value) =>
        value == null ? null : value.StartsWith(LayerPrefix, StringComparison.Ordinal) ? value[LayerPrefix.Length..] : value;

    private static YamlMappingNode? Component(YamlMappingNode? entity, string type)
    {
        if (entity == null || Get(entity, "components") is not YamlSequenceNode components)
            return null;
        return components.Children.OfType<YamlMappingNode>().FirstOrDefault(c => Str(c, "type") == type);
    }

    private static YamlNode? Get(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Str(YamlMappingNode? node, string key) => (Get(node, key) as YamlScalarNode)?.Value;

    private static bool? Bool(YamlMappingNode node, string key) =>
        bool.TryParse(Str(node, key), out var value) ? value : null;

    private static int? Int(YamlMappingNode node, string key) =>
        int.TryParse(Str(node, key), out var value) ? value : null;

    private static List<string>? Strings(YamlMappingNode node, string key) =>
        Get(node, key) switch
        {
            YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList(),
            YamlScalarNode { Value: { Length: > 0 } one } => [one],
            _ => null,
        };
}
