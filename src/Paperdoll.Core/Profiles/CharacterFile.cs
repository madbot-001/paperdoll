using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Rendering;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// A character as the lobby's Export writes it: <c>forkId</c>, <c>version</c> and <c>profile</c>.
/// The whole file is kept as read, so keys Paperdoll does not know (a fork's extras) are written
/// back unchanged; only the fields Paperdoll edits are touched.
/// </summary>
public sealed class CharacterFile
{
    private readonly YamlMappingNode _root;

    private CharacterFile(YamlMappingNode root) => _root = root;

    public static CharacterFile Parse(string text)
    {
        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(text));
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new FormatException($"Not a YAML file: {e.Message}", e);
        }
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root
            || !root.Children.TryGetValue(new YamlScalarNode("profile"), out var profile) || profile is not YamlMappingNode)
            throw new FormatException("Not a character export: it has no profile.");
        return new CharacterFile(root);
    }

    /// <summary>A new, empty version 2 file, its keys in the order the game writes them.</summary>
    public static CharacterFile CreateNew(string forkId) =>
        new(new YamlMappingNode
        {
            { "version", "2" },
            { "profile", new YamlMappingNode { { "appearance", new YamlMappingNode() } } },
            { "forkId", forkId },
        });

    public string ToYaml()
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        // The game saves with the same library, so this also ends with "...", as its exports do.
        new YamlStream(new YamlDocument(_root)).Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    public string? ForkId { get => Scalar(_root, "forkId"); set => SetScalar(_root, "forkId", value); }

    /// <summary>
    /// Labels the file with the fork it is exported for, as the game's Export does. The game never
    /// reads the label on import; it only records where the file came from.
    /// </summary>
    public void LabelFor(Forks.ForkInfo fork) => ForkId = fork.ServerForkIds.FirstOrDefault() ?? fork.Id;
    public string? Version { get => Scalar(_root, "version"); set => SetScalar(_root, "version", value); }

    public YamlMappingNode Profile => (YamlMappingNode)_root.Children[new YamlScalarNode("profile")];

    public YamlMappingNode Appearance
    {
        get
        {
            if (Profile.Children.TryGetValue(new YamlScalarNode("appearance"), out var node) && node is YamlMappingNode map)
                return map;
            var created = new YamlMappingNode();
            Profile.Children[new YamlScalarNode("appearance")] = created;
            return created;
        }
    }

    /// <summary>
    /// Whether the appearance is in the old model. Decided by shape, because some old-model forks
    /// write <c>version: 2</c>: old files keep hair in its own fields and markings as a list.
    /// </summary>
    public bool IsOldModel =>
        Appearance.Children.ContainsKey(new YamlScalarNode("hair"))
        || Appearance.Children.ContainsKey(new YamlScalarNode("facialHair"))
        || (Appearance.Children.TryGetValue(new YamlScalarNode("markings"), out var m) && m is YamlSequenceNode);

    /// <summary>Chosen trait ids as written (<c>_traitPreferences</c>).</summary>
    public IReadOnlyList<string> TraitPreferences =>
        Profile.Children.TryGetValue(new YamlScalarNode("_traitPreferences"), out var node) && node is YamlSequenceNode list
            ? list.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList()
            : [];

    /// <summary>Antagonist roles the character is willing to be (<c>_antagPreferences</c>).</summary>
    public IReadOnlyList<string> AntagPreferences =>
        Profile.Children.TryGetValue(new YamlScalarNode("_antagPreferences"), out var node) && node is YamlSequenceNode list
            ? list.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList()
            : [];

    public void SetAntagPreferences(IEnumerable<string> antags) =>
        Profile.Children[new YamlScalarNode("_antagPreferences")] =
            new YamlSequenceNode(antags.Select(a => (YamlNode)new YamlScalarNode(a)));

    public void SetTraitPreferences(IEnumerable<string> traits) =>
        Profile.Children[new YamlScalarNode("_traitPreferences")] =
            new YamlSequenceNode(traits.Select(t => (YamlNode)new YamlScalarNode(t)));

    /// <summary>Job preferences, job id to High, Medium or Low (upstream's <c>_jobPriorities</c>).</summary>
    public IReadOnlyDictionary<string, string> JobPriorities =>
        Profile.Children.TryGetValue(new YamlScalarNode("_jobPriorities"), out var node) && node is YamlMappingNode map
            ? map.Children.Where(kv => kv.Value is YamlScalarNode).ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => ((YamlScalarNode)kv.Value).Value!)
            : new Dictionary<string, string>();

    /// <summary>The job with High priority, if any: the one the lobby previews.</summary>
    public string? HighPriorityJob => JobPriorities.FirstOrDefault(kv => kv.Value == "High").Key;

    /// <summary>Saved loadouts: role (such as <c>JobPassenger</c>) to group to selected loadout ids.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Loadouts
    {
        get
        {
            var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
            if (!Profile.Children.TryGetValue(new YamlScalarNode("_loadouts"), out var node) || node is not YamlMappingNode roles)
                return result;
            foreach (var (roleKey, roleNode) in roles.Children)
            {
                if (roleNode is not YamlMappingNode role || !role.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selected)
                    || selected is not YamlMappingNode groups)
                    continue;
                result[((YamlScalarNode)roleKey).Value!] = groups.Children.ToDictionary(
                    kv => ((YamlScalarNode)kv.Key).Value!,
                    kv => (IReadOnlyList<string>)((kv.Value as YamlSequenceNode)?.Children.OfType<YamlMappingNode>()
                        .Select(m => Scalar(m, "prototype")).OfType<string>().ToList() ?? []));
            }
            return result;
        }
    }

    /// <summary>
    /// Sets a job's priority (High, Medium, Low), or removes it for Never. Setting High moves any
    /// other High job to Medium, as the lobby does.
    /// </summary>
    public void SetJobPriority(string jobId, string priority)
    {
        var key = new YamlScalarNode("_jobPriorities");
        if (!Profile.Children.TryGetValue(key, out var node) || node is not YamlMappingNode map)
            Profile.Children[key] = map = new YamlMappingNode();

        if (priority == "High")
        {
            foreach (var (job, value) in map.Children.ToList())
            {
                if (value is YamlScalarNode { Value: "High" })
                    map.Children[job] = new YamlScalarNode("Medium");
            }
        }
        if (priority == "Never")
            map.Children.Remove(new YamlScalarNode(jobId));
        else
            map.Children[new YamlScalarNode(jobId)] = new YamlScalarNode(priority);
    }

    /// <summary>
    /// Replaces the selected loadouts of one group in a role's saved loadout. Entries kept keep
    /// their other fields (some forks store colour or name overrides there).
    /// </summary>
    public void SetLoadoutGroup(string role, string group, IReadOnlyList<string> loadoutIds)
    {
        var loadoutsKey = new YamlScalarNode("_loadouts");
        if (!Profile.Children.TryGetValue(loadoutsKey, out var node) || node is not YamlMappingNode roles)
            Profile.Children[loadoutsKey] = roles = new YamlMappingNode();
        if (!roles.Children.TryGetValue(new YamlScalarNode(role), out var roleNode) || roleNode is not YamlMappingNode roleMap)
            roles.Children[new YamlScalarNode(role)] = roleMap = new YamlMappingNode();
        if (!roleMap.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selected) || selected is not YamlMappingNode groups)
            roleMap.Children[new YamlScalarNode("selectedLoadouts")] = groups = new YamlMappingNode();

        var existing = groups.Children.TryGetValue(new YamlScalarNode(group), out var old) && old is YamlSequenceNode oldList
            ? oldList.Children.OfType<YamlMappingNode>().Where(m => Scalar(m, "prototype") != null)
                .GroupBy(m => Scalar(m, "prototype")!).ToDictionary(g => g.Key, g => g.First())
            : new Dictionary<string, YamlMappingNode>();
        groups.Children[new YamlScalarNode(group)] = new YamlSequenceNode(loadoutIds.Select(id =>
            (YamlNode)(existing.TryGetValue(id, out var kept) ? kept : new YamlMappingNode { { "prototype", id } })));
    }

    /// <summary>Any single value in the profile by key, such as a fork's own <c>height</c>.</summary>
    public string? GetValue(string key) => Scalar(Profile, key);

    public void SetValue(string key, string? value) => SetScalar(Profile, key, value);

    public string? Name { get => Scalar(Profile, "name"); set => SetScalar(Profile, "name", value); }
    public string? FlavorText { get => Scalar(Profile, "flavorText"); set => SetScalar(Profile, "flavorText", value); }

    /// <summary>Where the character spawns when joining mid-round: None, Arrivals or Cryosleep.</summary>
    public string? SpawnPriority { get => Scalar(Profile, "spawnPriority"); set => SetScalar(Profile, "spawnPriority", value); }

    /// <summary>What happens when none of the character's jobs are free: StayInLobby or SpawnAsOverflow.</summary>
    public string? PreferenceUnavailable { get => Scalar(Profile, "preferenceUnavailable"); set => SetScalar(Profile, "preferenceUnavailable", value); }
    public string? Species { get => Scalar(Profile, "species"); set => SetScalar(Profile, "species", value); }
    public string? Sex { get => Scalar(Profile, "sex"); set => SetScalar(Profile, "sex", value); }
    public string? Gender { get => Scalar(Profile, "gender"); set => SetScalar(Profile, "gender", value); }
    public string? Voice { get => Scalar(Profile, "voice"); set => SetScalar(Profile, "voice", value); }

    public int? Age
    {
        get => int.TryParse(Scalar(Profile, "age"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : null;
        set => SetScalar(Profile, "age", value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The appearance as Paperdoll draws it. An old-model file's hair, facial hair and markings
    /// are sorted onto the species' organs the way the game converts version 1 files.
    /// </summary>
    public CharacterLook ReadLook(CharacterCatalog catalog)
    {
        var species = Species ?? "Human";
        var look = new CharacterLook
        {
            Species = species,
            Sex = Sex ?? "Male",
            SkinColor = Rgba.TryParse(Scalar(Appearance, "skinColor"), out var skin) ? skin : Rgba.White,
            EyeColor = Rgba.TryParse(Scalar(Appearance, "eyeColor"), out var eyes) ? eyes : Rgba.White,
        };

        if (IsOldModel)
        {
            var flat = new List<MarkingEntry>();
            if (Appearance.Children.TryGetValue(new YamlScalarNode("markings"), out var list) && list is YamlSequenceNode seq)
                flat.AddRange(seq.Children.OfType<YamlMappingNode>().Select(ReadMarking).OfType<MarkingEntry>());
            // Version 1 kept hair and facial hair apart; the game adds them as markings.
            AddHair(flat, "hair", "hairColor");
            AddHair(flat, "facialHair", "facialHairColor");
            if (catalog.Species.TryGetValue(species, out var info))
                SortOntoOrgans(look.Markings, flat, info, catalog);
            return look;
        }

        if (Appearance.Children.TryGetValue(new YamlScalarNode("markings"), out var byOrgan) && byOrgan is YamlMappingNode organs)
        {
            foreach (var (organKey, layersNode) in organs.Children)
            {
                if (layersNode is not YamlMappingNode layers)
                    continue;
                var byLayer = new Dictionary<string, List<MarkingEntry>>();
                foreach (var (layerKey, markingsNode) in layers.Children)
                {
                    if (markingsNode is YamlSequenceNode markings)
                        byLayer[((YamlScalarNode)layerKey).Value!] = markings.Children.OfType<YamlMappingNode>().Select(ReadMarking).OfType<MarkingEntry>().ToList();
                }
                look.Markings[((YamlScalarNode)organKey).Value!] = byLayer;
            }
        }
        return look;

        void AddHair(List<MarkingEntry> flat, string idKey, string colorKey)
        {
            if (Scalar(Appearance, idKey) is { Length: > 0 } id)
                flat.Add(new MarkingEntry(id, [Rgba.TryParse(Scalar(Appearance, colorKey), out var c) ? c : Rgba.White]));
        }
    }

    /// <summary>
    /// Writes the appearance in the new model (version 2), replacing an old model's hair fields
    /// and marking list. Other appearance keys are kept.
    /// </summary>
    public void WriteLook(CharacterLook look)
    {
        Species = look.Species;
        Sex = look.Sex;
        var appearance = Appearance;
        foreach (var key in new[] { "hair", "hairColor", "facialHair", "facialHairColor" })
            appearance.Children.Remove(new YamlScalarNode(key));

        SetScalar(appearance, "eyeColor", look.EyeColor.ToHex());
        SetScalar(appearance, "skinColor", look.SkinColor.ToHex());

        var organs = new YamlMappingNode();
        foreach (var (organ, byLayer) in look.Markings)
        {
            var layers = new YamlMappingNode();
            foreach (var (layer, markings) in byLayer)
            {
                if (markings.Count == 0)
                    continue;
                layers.Add(layer, new YamlSequenceNode(markings.Select(m => (YamlNode)new YamlMappingNode
                {
                    { "markingColor", new YamlSequenceNode(m.Colors.Select(c => (YamlNode)new YamlScalarNode(c.ToHex()))) },
                    { "markingId", m.Id },
                })));
            }
            // Organs with no markings are written as {}, as the game does.
            organs.Add(organ, layers);
        }
        appearance.Children[new YamlScalarNode("markings")] = organs;
        Version = "2";
    }

    /// <summary>
    /// Puts flat markings on the organs whose marking layers include the marking's layer, as the
    /// game's version 1 import does (<c>MarkingManager.ConvertMarkings</c>). Unknown markings and
    /// markings no organ takes are dropped.
    /// </summary>
    public static void SortOntoOrgans(Dictionary<string, Dictionary<string, List<MarkingEntry>>> target,
        IEnumerable<MarkingEntry> markings, SpeciesInfo species, CharacterCatalog catalog)
    {
        var layerToOrgan = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var organ in species.Organs)
        {
            foreach (var layer in organ.MarkingLayers)
                layerToOrgan.TryAdd(layer, organ.Category);
        }

        foreach (var entry in markings)
        {
            if (!catalog.Markings.TryGetValue(entry.Id, out var marking) || !layerToOrgan.TryGetValue(marking.Layer, out var organ))
                continue;
            if (!target.TryGetValue(organ, out var byLayer))
                target[organ] = byLayer = [];
            if (!byLayer.TryGetValue(marking.Layer, out var list))
                byLayer[marking.Layer] = list = [];
            list.Add(entry);
        }
    }

    private static MarkingEntry? ReadMarking(YamlMappingNode node)
    {
        if (Scalar(node, "markingId") is not { } id)
            return null;
        var colors = node.Children.TryGetValue(new YamlScalarNode("markingColor"), out var c) && c is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(s => Rgba.TryParse(s.Value, out var color) ? color : Rgba.White).ToList()
            : [];
        return new MarkingEntry(id, colors);
    }

    internal static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    internal static void SetScalar(YamlMappingNode node, string key, string? value)
    {
        if (value == null)
            node.Children.Remove(new YamlScalarNode(key));
        else
            node.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }
}
