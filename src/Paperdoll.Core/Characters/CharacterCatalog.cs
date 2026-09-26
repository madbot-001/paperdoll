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
    YamlMappingNode Node)
{
    /// <summary>The species' fixed sprite scale (Delta-V's <c>baseScale</c>); 1 by 1 elsewhere.</summary>
    public (float X, float Y) BaseScale { get; init; } = (1, 1);

    /// <summary>Allowed character heights (Delta-V's <c>minHeight</c> and <c>maxHeight</c>, 0.8 to 1.2 by default).</summary>
    public float MinHeight { get; init; } = 0.8f;
    public float MaxHeight { get; init; } = 1.2f;
    public float DefaultHeight { get; init; } = 1f;

    /// <summary>The name clothing states use for this species' versions (the doll's <c>Inventory.speciesId</c>).</summary>
    public string? ClothingSpeciesId { get; init; }

    /// <summary>Maps that fit clothing to the body, by slot; the male and female sets replace the default when present.</summary>
    public IReadOnlyDictionary<string, DisplacementRef> ClothingDisplacements { get; init; } = new Dictionary<string, DisplacementRef>();
    public IReadOnlyDictionary<string, DisplacementRef> MaleClothingDisplacements { get; init; } = new Dictionary<string, DisplacementRef>();
    public IReadOnlyDictionary<string, DisplacementRef> FemaleClothingDisplacements { get; init; } = new Dictionary<string, DisplacementRef>();

    /// <summary>Voices a player can pick (upstream's <c>voices</c>); empty where the fork has no voice choice.</summary>
    public IReadOnlyList<string> Voices { get; init; } = [];

    /// <summary>The default voice for Male, Female and Unsexed, in that order (<c>defaultSoundsBySex</c>).</summary>
    public IReadOnlyList<string> DefaultVoices { get; init; } = [];

    /// <summary>The voice a character of this sex gets by default.</summary>
    public string? DefaultVoice(string sex) => DefaultVoices.Count < 3 ? null : DefaultVoices[sex switch
    {
        "Female" => 1,
        "Unsexed" => 2,
        _ => 0,
    }];

    /// <summary>The map fitting clothing in a slot to this body and sex, as the game picks it.</summary>
    public DisplacementRef? ClothingDisplacement(string slot, string sex) => sex switch
    {
        "Male" when MaleClothingDisplacements.Count > 0 => MaleClothingDisplacements.GetValueOrDefault(slot),
        "Female" when FemaleClothingDisplacements.Count > 0 => FemaleClothingDisplacements.GetValueOrDefault(slot),
        _ => ClothingDisplacements.GetValueOrDefault(slot),
    };
}

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
    /// <summary>Whether the organ has marking data at all (<c>VisualOrganMarkings</c>); the game saves an entry for each such organ.</summary>
    public bool TakesMarkings { get; init; }

    /// <summary>The map reshaping the organ's own layer, if any.</summary>
    public DisplacementRef? Displacement { get; init; }

    /// <summary>Maps reshaping markings on the organ's layers, by layer.</summary>
    public IReadOnlyDictionary<string, DisplacementRef> MarkingsDisplacement { get; init; } = new Dictionary<string, DisplacementRef>();

    /// <summary>Layers of this organ that clothing may hide, and layers hidden along with each.</summary>
    public IReadOnlyList<string> HideableLayers { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DependentHidingLayers { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
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

    /// <summary>
    /// Whether characters pick a voice (upstream since 2026-06-30, space-wizards/space-station-14
    /// #40593; Delta-V and Euphoria do not have it). Voice ids map to the locale key of their name
    /// in the lobby.
    /// </summary>
    public bool HasVoices => VoiceNames.Count > 0;
    public IReadOnlyDictionary<string, string> VoiceNames { get; init; } = new Dictionary<string, string>();

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
        foreach (var species in Species.Values)
        {
            foreach (var map in species.ClothingDisplacements.Values.Concat(species.MaleClothingDisplacements.Values).Concat(species.FemaleClothingDisplacements.Values))
                folders.UnionWith(map.SizeMaps.Values.Select(m => m.Rsi));
        }
        return folders;
    }

    public static CharacterCatalog Build(PrototypeIndex index)
    {
        // A fork has voices when any species names them; the rest then get the game's defaults.
        var hasVoices = index.OfKind("species").Any(p =>
            index.Resolve("species", p.Id) is { } node && (Get(node, "voices") != null || Get(node, "defaultSoundsBySex") != null));
        var species = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);
        foreach (var proto in index.OfKind("species"))
        {
            var node = index.Resolve("species", proto.Id)!;
            species[proto.Id] = ReadSpecies(index, proto.Id, node, hasVoices);
        }
        var voiceNames = hasVoices
            ? index.OfKind("emoteSounds").Where(p => !p.Abstract).ToDictionary(p => p.Id,
                p => Str(index.Resolve("emoteSounds", p.Id), "voiceSelectorName") ?? "humanoid-profile-editor-voice-none", StringComparer.Ordinal)
            : new Dictionary<string, string>();

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
            VoiceNames = voiceNames,
        };
    }

    private static SpeciesInfo ReadSpecies(PrototypeIndex index, string id, YamlMappingNode node, bool hasVoices)
    {
        var doll = Str(node, "dollPrototype");
        // Old-model species name base sprites under "sprites" and have no organs.
        var organs = doll != null && Get(node, "sprites") == null ? ReadOrgans(index, doll) : [];
        var inventory = doll != null ? Component(index.Resolve("entity", doll), "Inventory") : null;

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
            node)
        {
            BaseScale = Pair(Str(node, "baseScale")) ?? (1, 1),
            MinHeight = Float(node, "minHeight") ?? 0.8f,
            MaxHeight = Float(node, "maxHeight") ?? 1.2f,
            DefaultHeight = Float(node, "defaultHeight") ?? 1f,
            ClothingSpeciesId = Str(inventory, "speciesId"),
            ClothingDisplacements = Displacements(inventory, "displacements"),
            MaleClothingDisplacements = Displacements(inventory, "maleDisplacements"),
            FemaleClothingDisplacements = Displacements(inventory, "femaleDisplacements"),
            // The game's defaults when a species names no voices.
            Voices = hasVoices ? Strings(node, "voices") ?? ["MaleHuman", "FemaleHuman"] : [],
            DefaultVoices = hasVoices ? Strings(node, "defaultSoundsBySex") ?? ["MaleHuman", "FemaleHuman", "MaleHuman"] : [],
        };
    }

    private static Dictionary<string, DisplacementRef> Displacements(YamlMappingNode? inventory, string key)
    {
        var result = new Dictionary<string, DisplacementRef>(StringComparer.Ordinal);
        if (Get(inventory, key) is YamlMappingNode bySlot)
        {
            foreach (var (slot, data) in bySlot.Children)
            {
                if (ReadDisplacement(data as YamlMappingNode) is { } map)
                    result[((YamlScalarNode)slot).Value!] = map;
            }
        }
        return result;
    }

    // "1.1, 1.1" as the game writes a two-number vector.
    private static (float, float)? Pair(string? text)
    {
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 }
            || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
            return null;
        return (x, y);
    }

    private static float? Float(YamlMappingNode node, string key) =>
        float.TryParse(Str(node, key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

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
                TakesMarkings = marks != null,
                Displacement = displacement,
                MarkingsDisplacement = markingsDisplacement,
                HideableLayers = (marks != null ? Strings(marks, "hideableLayers") : null)?.Select(l => Layer(l)!).ToList() ?? [],
                DependentHidingLayers = marks != null && Get(marks, "dependentHidingLayers") is YamlMappingNode dependent
                    ? dependent.Children.ToDictionary(
                        kv => Layer(((YamlScalarNode)kv.Key).Value)!,
                        kv => (IReadOnlyList<string>)((kv.Value as YamlSequenceNode)?.Children.OfType<YamlScalarNode>().Select(v => Layer(v.Value)!).ToList() ?? []))
                    : new Dictionary<string, IReadOnlyList<string>>(),
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
