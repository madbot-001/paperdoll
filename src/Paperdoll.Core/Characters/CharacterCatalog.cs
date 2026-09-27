using System.Globalization;
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

    /// <summary>Allowed character heights (Delta-V's and Goob's <c>minHeight</c> and <c>maxHeight</c>, 0.8 to 1.2 by default).</summary>
    public float MinHeight { get; init; } = 0.8f;
    public float MaxHeight { get; init; } = 1.2f;
    public float DefaultHeight { get; init; } = 1f;

    /// <summary>Allowed character widths, where the fork has them (Goob's <c>minWidth</c> and <c>maxWidth</c>).</summary>
    public float MinWidth { get; init; } = 0.85f;
    public float MaxWidth { get; init; } = 1.15f;
    public float DefaultWidth { get; init; } = 1f;

    /// <summary>How far height and width may part in the lobby's sliders, either way (Goob's <c>sizeRatio</c>).</summary>
    public float SizeRatio { get; init; } = 1.2f;

    /// <summary>Centimetres at height or width 1, for showing sizes (Goob's <c>averageHeight</c> and <c>averageWidth</c>).</summary>
    public float AverageHeight { get; init; } = 176.1f;
    public float AverageWidth { get; init; } = 40f;

    /// <summary>
    /// Kilograms at size 1, from the species entity's main fixture (its area times density), for
    /// showing weight; null when it has no simple shape.
    /// </summary>
    public float? Mass { get; init; }

    /// <summary>The old appearance model's body, for forks still on it; null on the new model.</summary>
    public OldBody? Old { get; init; }

    /// <summary>Whether characters of this species may have a custom species name (Euphoria's <c>customName</c>).</summary>
    public bool CustomName { get; init; } = true;

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

    public string? DefaultVoice(string sex) => DefaultVoices.Count < 3 ? null : DefaultVoices[sex switch
    {
        "Female" => 1,
        "Unsexed" => 2,
        _ => 0,
    }];

    /// <summary>The clothing map for the slot, using the sex's override set if the species has one.</summary>
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
    IReadOnlyList<string> NudityDefault)
{
    /// <summary>The chance, per marking the layer takes, that a random character gets one (<c>weight</c>).</summary>
    public float Weight { get; init; } = 0.6f;
}

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

    /// <summary>How likely random characters are to get this marking, against the others (<c>randomWeight</c>).</summary>
    public float RandomWeight { get; init; } = 1f;

    /// <summary>Old model: the points category it counts against (<c>markingCategory</c>).</summary>
    public string? Category { get; init; }

    /// <summary>Old model: the species that may have it (<c>speciesRestriction</c>); null for any.</summary>
    public IReadOnlyList<string>? SpeciesRestriction { get; init; }

    /// <summary>Old model: coloured like the skin (<c>followSkinColor</c>), from before colouring rules existed.</summary>
    public bool FollowSkinColor { get; init; }

    /// <summary>
    /// Euphoria's <c>layering</c>: sprite states drawn on another body layer than the marking's,
    /// such as a tail's back half on <c>TailBehind</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Layering { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Euphoria's <c>colorLinks</c>: sprite states drawn in another state's colour (linked state
    /// to the state it copies). Its lobby hides their colour pickers.
    /// </summary>
    public IReadOnlyDictionary<string, string> ColorLinks { get; init; } = new Dictionary<string, string>();

    /// <summary>The body layer a sprite is drawn on.</summary>
    public string LayerOf(SpriteRef sprite) =>
        sprite.State != null && Layering.TryGetValue(sprite.State, out var layer) ? layer : Layer;

    /// <summary>Whether a sprite takes another sprite's colour, and so has no colour of its own to choose.</summary>
    public bool IsColorLinked(int sprite) =>
        sprite < Sprites.Count && Sprites[sprite].State is { } state && ColorLinks.ContainsKey(state);

    /// <summary>
    /// The colours the sprites are drawn in: linked sprites take the colour of the sprite they
    /// name; missing colours are white.
    /// </summary>
    public List<Rendering.Rgba> DrawnColors(IReadOnlyList<Rendering.Rgba> colors)
    {
        if (ColorLinks.Count == 0)
            return Sprites.Select((_, i) => i < colors.Count ? colors[i] : Rendering.Rgba.White).ToList();
        var byState = new Dictionary<string, Rendering.Rgba>(StringComparer.Ordinal);
        for (var i = 0; i < Sprites.Count; i++)
        {
            if (Sprites[i].State is { } state)
                byState.TryAdd(state, i < colors.Count ? colors[i] : Rendering.Rgba.White);
        }
        // In the order the prototype lists them, as the game does.
        foreach (var (child, parent) in ColorLinks)
        {
            if (byState.TryGetValue(parent, out var color))
                byState[child] = color;
        }
        return Sprites.Select((sprite, i) => sprite.State is { } state ? byState[state] : i < colors.Count ? colors[i] : Rendering.Rgba.White).ToList();
    }
}

/// <summary>A base body layer in the old appearance model (<c>humanoidBaseSprite</c>).</summary>
/// <param name="AllowsMarkings">Whether markings on this layer are drawn.</param>
public sealed record OldBodyLayer(string Layer, SpriteRef? Sprite, bool MatchSkin, bool MarkingsMatchSkin, float LayerAlpha, bool AllowsMarkings)
{
    /// <summary>
    /// The versions drawn for Male and Female, where the fork has them: the game looks for the
    /// layer's id with the sex added (<c>MobHumanHead</c> becomes <c>MobHumanHeadMale</c>), on the
    /// chest and head only.
    /// </summary>
    public IReadOnlyDictionary<string, OldBodyLayer> BySex { get; init; } = new Dictionary<string, OldBodyLayer>();

    public OldBodyLayer ForSex(string sex) => BySex.GetValueOrDefault(sex) ?? this;
}

/// <summary>How many markings a category takes in the old model, and which it starts with.</summary>
/// <param name="OnlyWhitelisted">Whether the lobby offers only markings made for the species in this category.</param>
public sealed record MarkingPoints(int Points, bool Required, IReadOnlyList<string> Defaults, bool OnlyWhitelisted);

/// <summary>
/// A species under the old appearance model: base sprites by layer (<c>speciesBaseSprites</c>) and
/// marking points by category (<c>markingPoints</c>), instead of organs.
/// </summary>
public sealed record OldBody(IReadOnlyList<OldBodyLayer> Layers, IReadOnlyDictionary<string, MarkingPoints> Points, bool OnlyWhitelisted)
{
    /// <summary>Body layers that worn items may hide (<c>hideLayersOnEquip</c>); the game's default is hair only.</summary>
    public IReadOnlyList<string> HideOnEquip { get; init; } = ["Hair"];

    /// <summary>Maps reshaping markings on a layer, such as hair on Vox (<c>markingsDisplacement</c>).</summary>
    public IReadOnlyDictionary<string, DisplacementRef> MarkingsDisplacement { get; init; } = new Dictionary<string, DisplacementRef>();

    /// <summary>The base layer drawn for a body layer, if the species has one.</summary>
    public OldBodyLayer? Layer(string layer) => Layers.FirstOrDefault(l => l.Layer == layer);

    /// <summary>
    /// Whether the lobby offers the marking to the species (<c>MarkingManager.MarkingsByCategoryAndSpecies</c>):
    /// markings made for other species never, and ones made for no species in particular unless
    /// the species or the category takes only its own. The rules only check the species-wide switch.
    /// </summary>
    public bool Offers(MarkingInfo marking, string speciesId) =>
        marking.Category != null && (marking.SpeciesRestriction == null
            ? !OnlyWhitelisted && !(Points.TryGetValue(marking.Category, out var points) && points.OnlyWhitelisted)
            : marking.SpeciesRestriction.Contains(speciesId));
}

/// <summary>
/// The character data in one fork: species with their organs, marking groups, markings and skin
/// colorations. Species on the old appearance model are given an organ per marking category and
/// a group of their own, so both models can be edited the same way.
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
        foreach (var old in Species.Values.Select(s => s.Old).OfType<OldBody>())
        {
            foreach (var layer in old.Layers.SelectMany(l => l.BySex.Values.Append(l)))
            {
                if (layer.Sprite is { } sprite)
                    folders.Add(sprite.Rsi);
            }
            folders.UnionWith(old.MarkingsDisplacement.Values.SelectMany(m => m.SizeMaps.Values).Select(m => m.Rsi));
        }
        foreach (var species in Species.Values)
        {
            foreach (var map in species.ClothingDisplacements.Values.Concat(species.MaleClothingDisplacements.Values).Concat(species.FemaleClothingDisplacements.Values))
                folders.UnionWith(map.SizeMaps.Values.Select(m => m.Rsi));
        }
        return folders;
    }

    /// <param name="defaultHeights">The fork's height range for species that name none.</param>
    public static CharacterCatalog Build(PrototypeIndex index, (float Min, float Max)? defaultHeights = null)
    {
        var heights = defaultHeights ?? (0.8f, 1.2f);
        // A fork has voices when any species names them; the rest then get the game's defaults.
        var hasVoices = index.OfKind("species").Any(p =>
            index.Resolve("species", p.Id) is { } node && (Get(node, "voices") != null || Get(node, "defaultSoundsBySex") != null));
        var species = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);
        foreach (var proto in index.OfKind("species"))
        {
            var node = index.Resolve("species", proto.Id)!;
            species[proto.Id] = ReadSpecies(index, proto.Id, node, hasVoices, heights);
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

        if (species.Values.Any(sp => sp.Old != null))
            ShapeOldModel(species, groups, markings);

        return new CharacterCatalog
        {
            Species = species,
            MarkingsGroups = groups,
            Markings = markings,
            SkinColorations = colorations,
            VoiceNames = voiceNames,
        };
    }

    // The old model has no organs or marking groups. Each species gets a group of its own, with a
    // marking's species restriction as the group's whitelist, and an organ per marking category
    // holding the body layers that category's markings go on. The editor, the marking lists and
    // the checks then work as on the new model; points are counted per category elsewhere.
    private static void ShapeOldModel(Dictionary<string, SpeciesInfo> species, Dictionary<string, MarkingsGroupInfo> groups, Dictionary<string, MarkingInfo> markings)
    {
        foreach (var (id, marking) in markings.ToList())
        {
            if (marking.GroupWhitelist == null && marking.SpeciesRestriction != null)
                markings[id] = marking with { GroupWhitelist = marking.SpeciesRestriction };
        }

        foreach (var sp in species.Values.Where(sp => sp.Old != null).ToList())
        {
            var usable = markings.Values.Where(m => sp.Old!.Offers(m, sp.Id)).ToList();
            var categories = sp.Old!.Points.Keys
                .Concat(usable.Select(m => m.Category!))
                .Distinct(StringComparer.Ordinal);
            var organs = categories.Select(category => new OrganInfo(
                    category, "", null, null, new Dictionary<string, string>(),
                    usable.Where(m => m.Category == category).Select(m => m.Layer).Distinct(StringComparer.Ordinal).ToList(),
                    sp.Id)
                { TakesMarkings = true })
                .Where(o => o.MarkingLayers.Count > 0 || sp.Old.Points.ContainsKey(o.Category))
                .ToList();
            groups[sp.Id] = new MarkingsGroupInfo(sp.Id, sp.Old.OnlyWhitelisted, new Dictionary<string, LayerLimit>());
            species[sp.Id] = sp with { Organs = organs };
        }
    }

    // The old model's base sprites (layer to humanoidBaseSprite) and marking points, and from the
    // lobby doll's HumanoidAppearance, which layers clothing may hide and marking displacements.
    private static OldBody ReadOldBody(PrototypeIndex index, string baseSpritesId, string? pointsId, string? doll)
    {
        var layers = new List<OldBodyLayer>();
        if (index.Resolve("speciesBaseSprites", baseSpritesId) is { } baseSprites && Get(baseSprites, "sprites") is YamlMappingNode byLayer)
        {
            foreach (var (key, value) in byLayer.Children)
            {
                var name = ((YamlScalarNode)key).Value!;
                if (value is not YamlScalarNode { Value: { } layerId } || BaseLayer(name, layerId) is not { } layer)
                    continue;
                if (name is "Chest" or "Head")
                {
                    layer = layer with
                    {
                        BySex = new[] { "Male", "Female" }
                            .Select(sex => (sex, variant: BaseLayer(name, layerId + sex)))
                            .Where(v => v.variant != null)
                            .ToDictionary(v => v.sex, v => v.variant!),
                    };
                }
                layers.Add(layer);
            }
        }

        OldBodyLayer? BaseLayer(string name, string id)
        {
            if (index.Resolve("humanoidBaseSprite", id) is not { } node)
                return null;
            var sprite = Get(node, "baseSprite") is YamlMappingNode spec && Str(spec, "sprite") is { } rsi
                ? new SpriteRef(TexturePath(rsi), Str(spec, "state"))
                : (SpriteRef?)null;
            return new OldBodyLayer(name, sprite, Bool(node, "matchSkin") ?? true, Bool(node, "markingsMatchSkin") ?? false,
                Float(node, "layerAlpha") ?? 1f, Bool(node, "allowsMarkings") ?? true);
        }

        var points = new Dictionary<string, MarkingPoints>(StringComparer.Ordinal);
        var onlyWhitelisted = false;
        if (pointsId != null && index.Resolve("markingPoints", pointsId) is { } limits)
        {
            onlyWhitelisted = Bool(limits, "onlyWhitelisted") ?? false;
            if (Get(limits, "points") is YamlMappingNode byCategory)
            {
                foreach (var (key, value) in byCategory.Children)
                {
                    if (value is YamlMappingNode entry)
                        points[((YamlScalarNode)key).Value!] = new MarkingPoints(Int(entry, "points") ?? 0, Bool(entry, "required") ?? false,
                            Strings(entry, "defaultMarkings") ?? [], Bool(entry, "onlyWhitelisted") ?? false);
                }
            }
        }
        var appearance = Component(doll != null ? index.Resolve("entity", doll) : null, "HumanoidAppearance");
        var displacements = new Dictionary<string, DisplacementRef>(StringComparer.Ordinal);
        if (Get(appearance, "markingsDisplacement") is YamlMappingNode byLayerMap)
        {
            foreach (var (layerKey, data) in byLayerMap.Children)
            {
                if (ReadDisplacement(data as YamlMappingNode) is { } map)
                    displacements[Layer(((YamlScalarNode)layerKey).Value)!] = map;
            }
        }
        return new OldBody(layers, points, onlyWhitelisted)
        {
            HideOnEquip = (appearance != null ? Strings(appearance, "hideLayersOnEquip") : null)?.Select(l => Layer(l)!).ToList() ?? ["Hair"],
            MarkingsDisplacement = displacements,
        };
    }

    private static SpeciesInfo ReadSpecies(PrototypeIndex index, string id, YamlMappingNode node, bool hasVoices, (float Min, float Max) heights)
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
            MinHeight = Float(node, "minHeight") ?? heights.Min,
            MaxHeight = Float(node, "maxHeight") ?? heights.Max,
            CustomName = Bool(node, "customName") ?? true,
            DefaultHeight = Float(node, "defaultHeight") ?? 1f,
            MinWidth = Float(node, "minWidth") ?? 0.85f,
            MaxWidth = Float(node, "maxWidth") ?? 1.15f,
            DefaultWidth = Float(node, "defaultWidth") ?? 1f,
            SizeRatio = Float(node, "sizeRatio") ?? 1.2f,
            AverageHeight = Float(node, "averageHeight") ?? 176.1f,
            AverageWidth = Float(node, "averageWidth") ?? 40f,
            Mass = Str(node, "prototype") is { } entity ? FixtureMass(Component(index.Resolve("entity", entity), "Fixtures")) : null,
            Old = Str(node, "sprites") is { } baseSprites ? ReadOldBody(index, baseSprites, Str(node, "markingLimits"), doll) : null,
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

    // The mass of the "fix1" fixture: its shape's area times its density (a circle, a box or a polygon).
    private static float? FixtureMass(YamlMappingNode? fixtures)
    {
        if (Get(Get(fixtures, "fixtures") as YamlMappingNode, "fix1") is not YamlMappingNode fixture
            || Get(fixture, "shape") is not YamlMappingNode shape)
            return null;
        var density = Float(fixture, "density") ?? 1f;
        float? area = shape.Tag.IsEmpty ? null : shape.Tag.Value switch
        {
            "!type:PhysShapeCircle" => MathF.PI * MathF.Pow(Float(shape, "radius") ?? 0.5f, 2),
            "!type:PhysShapeAabb" => Floats(Str(shape, "bounds")) is [var left, var bottom, var right, var top] ? (right - left) * (top - bottom) : null,
            "!type:PolygonShape" => Get(shape, "vertices") is YamlSequenceNode vertices ? PolygonArea(vertices) : null,
            _ => null,
        };
        return area * density;

        static float[]? Floats(string? text) => text?.Split(',').Select(v => float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : float.NaN).ToArray();

        static float? PolygonArea(YamlSequenceNode vertices)
        {
            var points = vertices.Children.OfType<YamlScalarNode>().Select(v => Floats(v.Value)).OfType<float[]>().Where(p => p.Length == 2).ToList();
            if (points.Count < 3)
                return null;
            var twice = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                var (a, b) = (points[i], points[(i + 1) % points.Count]);
                twice += a[0] * b[1] - b[0] * a[1];
            }
            return MathF.Abs(twice) / 2;
        }
    }

    // Two-number vectors are written "1.1, 1.1".
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
                    Strings(limit, "nudityDefault") ?? [])
                {
                    Weight = Float(limit, "weight") ?? 0.6f,
                };
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
            RandomWeight = Float(node, "randomWeight") ?? 1f,
            Category = Str(node, "markingCategory"),
            SpeciesRestriction = Strings(node, "speciesRestriction"),
            FollowSkinColor = Bool(node, "followSkinColor") ?? false,
            Layering = Pairs(node, "layering", Layer),
            ColorLinks = Pairs(node, "colorLinks", value => value),
        };
    }

    private static Dictionary<string, string> Pairs(YamlMappingNode node, string key, Func<string?, string?> value)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Get(node, key) is YamlMappingNode map)
        {
            foreach (var (k, v) in map.Children)
            {
                if (k is YamlScalarNode { Value: { } name } && v is YamlScalarNode scalar && value(scalar.Value) is { } read)
                    pairs[name] = read;
            }
        }
        return pairs;
    }

    // A DisplacementData block: sizeMaps: { 32: { sprite: ..., state: ... } }.
    private static DisplacementRef? ReadDisplacement(YamlMappingNode? data)
    {
        if (data == null || Get(data, "sizeMaps") is not YamlMappingNode sizes)
            return null;
        var maps = new Dictionary<int, SpriteRef>();
        foreach (var (sizeKey, layer) in sizes.Children)
        {
            if (int.TryParse(((YamlScalarNode)sizeKey).Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && layer is YamlMappingNode l && Str(l, "sprite") is { } rsi)
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
        int.TryParse(Str(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static List<string>? Strings(YamlMappingNode node, string key) =>
        Get(node, key) switch
        {
            YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList(),
            YamlScalarNode { Value: { Length: > 0 } one } => [one],
            _ => null,
        };
}
