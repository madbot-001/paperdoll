using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Rendering;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Profiles;

/// <summary>A chosen loadout, with the name, description and colour Euphoria lets players give it.</summary>
public sealed record LoadoutEntry(string Prototype, string? Name = null, string? Description = null, string? Color = null);

/// <summary>
/// A file from the lobby's Export: <c>forkId</c>, <c>version</c> and <c>profile</c>.
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
    /// Labels the file with the fork it is exported for. The game never reads this label on
    /// import; it only records where the file came from.
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
    /// other High job to Medium.
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
        var roleMap = RoleLoadout(role, create: true)!;
        if (!roleMap.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selected) || selected is not YamlMappingNode groups)
            roleMap.Children[new YamlScalarNode("selectedLoadouts")] = groups = new YamlMappingNode();

        var existing = groups.Children.TryGetValue(new YamlScalarNode(group), out var old) && old is YamlSequenceNode oldList
            ? oldList.Children.OfType<YamlMappingNode>().Where(m => Scalar(m, "prototype") != null)
                .GroupBy(m => Scalar(m, "prototype")!).ToDictionary(g => g.Key, g => g.First())
            : new Dictionary<string, YamlMappingNode>();
        groups.Children[new YamlScalarNode(group)] = new YamlSequenceNode(loadoutIds.Select(id =>
            (YamlNode)(existing.TryGetValue(id, out var kept) ? kept : new YamlMappingNode { { "prototype", id } })));
    }

    /// <summary>
    /// A job's saved loadout, made with the game's field order if it does not exist yet:
    /// <c>entityName</c>, <c>selectedLoadouts</c> and <c>role</c>.
    /// </summary>
    public YamlMappingNode? RoleLoadout(string role, bool create)
    {
        var loadoutsKey = new YamlScalarNode("_loadouts");
        if (!Profile.Children.TryGetValue(loadoutsKey, out var node) || node is not YamlMappingNode roles)
        {
            if (!create)
                return null;
            Profile.Children[loadoutsKey] = roles = new YamlMappingNode();
        }
        if (roles.Children.TryGetValue(new YamlScalarNode(role), out var roleNode) && roleNode is YamlMappingNode existing)
            return existing;
        if (!create)
            return null;
        var made = new YamlMappingNode
        {
            { "entityName", Null() },
            { "selectedLoadouts", new YamlMappingNode() },
            { "role", role },
        };
        roles.Children[new YamlScalarNode(role)] = made;
        return made;
    }

    /// <summary>The chosen loadouts of a job's group as saved, with Euphoria's name, description and colour for each.</summary>
    public IReadOnlyList<LoadoutEntry> LoadoutEntries(string role, string group) =>
        RoleLoadout(role, create: false) is { } map && map.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selected)
        && selected is YamlMappingNode groups && groups.Children.TryGetValue(new YamlScalarNode(group), out var list) && list is YamlSequenceNode entries
            ? entries.Children.OfType<YamlMappingNode>().Where(e => Scalar(e, "prototype") != null)
                .Select(e => new LoadoutEntry(Scalar(e, "prototype")!, NullableScalar(e, "nameOverride"), NullableScalar(e, "descriptionOverride"), NullableScalar(e, "colorOverride")))
                .ToList()
            : [];

    /// <summary>
    /// Sets a chosen loadout's name, description and colour (null for none), writing its fields in
    /// the order Euphoria saves them: colour, description, name, then the loadout.
    /// </summary>
    public void SetLoadoutCustomization(string role, string group, LoadoutEntry entry)
    {
        if (RoleLoadout(role, create: false) is not { } map || !map.Children.TryGetValue(new YamlScalarNode("selectedLoadouts"), out var selected)
            || selected is not YamlMappingNode groups || !groups.Children.TryGetValue(new YamlScalarNode(group), out var list) || list is not YamlSequenceNode entries)
            return;
        for (var i = 0; i < entries.Children.Count; i++)
        {
            if (entries.Children[i] is YamlMappingNode e && Scalar(e, "prototype") == entry.Prototype)
                entries.Children[i] = CustomizedEntry(entry);
        }
    }

    internal static YamlMappingNode CustomizedEntry(LoadoutEntry entry) => new()
    {
        { "colorOverride", entry.Color == null ? Null() : Text(entry.Color) },
        { "descriptionOverride", entry.Description == null ? Null() : Text(entry.Description) },
        { "nameOverride", entry.Name == null ? Null() : Text(entry.Name) },
        { "prototype", entry.Prototype },
    };

    /// <summary>The name the character takes in a role that allows one, such as a borg's; null for none.</summary>
    public string? RoleName(string role) => RoleLoadout(role, create: false) is { } map ? NullableScalar(map, "entityName") : null;

    public void SetRoleName(string role, string? name) =>
        RoleLoadout(role, create: true)!.Children[new YamlScalarNode("entityName")] = name == null ? Null() : Text(name);

    /// <summary>A plain <c>null</c>, which is what the game writes for no value.</summary>
    internal static YamlScalarNode Null() => new("null");

    /// <summary>A scalar that may be written as a bare <c>null</c>; quoted "null" stays text.</summary>
    internal static string? NullableScalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
        && !(scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && scalar.Value is "null" or "~")
            ? scalar.Value
            : null;

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
            var flat = OldMarkingList().ToList();
            if (catalog.Species.TryGetValue(species, out var info) && info.Old != null)
            {
                // Hair and markings stay as the old model has them; unknown ones are left for the
                // rules to report.
                SortByCategory(look.Markings, flat, catalog);
                foreach (var (idKey, colorKey, category) in new[] { ("hair", "hairColor", "Hair"), ("facialHair", "facialHairColor", "FacialHair") })
                {
                    if (Hair(idKey, colorKey) is not { } hair)
                        continue;
                    var layer = catalog.Markings.TryGetValue(hair.Id, out var style) ? style.Layer : category;
                    if (!look.Markings.TryGetValue(category, out var byLayer))
                        look.Markings[category] = byLayer = [];
                    if (!byLayer.TryGetValue(layer, out var list))
                        byLayer[layer] = list = [];
                    list.Insert(0, hair);
                }
                return look;
            }
            // Version 1 kept hair and facial hair apart; the game adds them as markings.
            if (Hair("hair", "hairColor") is { } hairMarking)
                flat.Add(hairMarking);
            if (Hair("facialHair", "facialHairColor") is { } facialHairMarking)
                flat.Add(facialHairMarking);
            if (info != null)
                SortOntoOrgans(look.Markings, flat, info, catalog);
            return look;
        }

        var oldSpecies = catalog.Species.TryGetValue(species, out var speciesInfo) && speciesInfo.Old != null;
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
        // A new-model file for a species on the old model: its markings sorted by category instead.
        if (oldSpecies)
        {
            var flat = look.Markings.Values.SelectMany(o => o.Values).SelectMany(l => l).ToList();
            look.Markings.Clear();
            SortByCategory(look.Markings, flat, catalog);
        }
        return look;

        MarkingEntry? Hair(string idKey, string colorKey) =>
            Scalar(Appearance, idKey) is { Length: > 0 } id && id is not (NoHair or NoFacialHair)
                ? new MarkingEntry(id, [Rgba.TryParse(Scalar(Appearance, colorKey), out var c) ? c : Rgba.White])
                : null;
    }

    /// <summary>An old-model file's marking list as written, without hair; empty for other files.</summary>
    public IReadOnlyList<MarkingEntry> OldMarkingList() =>
        Appearance.Children.TryGetValue(new YamlScalarNode("markings"), out var list) && list is YamlSequenceNode seq
            ? seq.Children.OfType<YamlMappingNode>().Select(ReadMarking).OfType<MarkingEntry>().ToList()
            : [];

    /// <summary>The old model's hair or facial hair colour, which the file keeps even without hair.</summary>
    public Rgba? OldHairColor(bool facial) =>
        Rgba.TryParse(Scalar(Appearance, facial ? "facialHairColor" : "hairColor"), out var color) ? color : null;

    /// <summary>What an old-model file names for no hair or no facial hair (upstream's <c>HairStyles</c> defaults).</summary>
    public const string NoHair = "HairBald";
    public const string NoFacialHair = "FacialHairShaved";

    /// <summary>Writes the appearance in the model the species uses.</summary>
    /// <param name="catalog">When given and the species is on the old model, the look is written the old way.</param>
    public void WriteLook(CharacterLook look, CharacterCatalog? catalog)
    {
        if (catalog != null && catalog.Species.TryGetValue(look.Species, out var species) && species.Old != null)
        {
            WriteOldLook(look);
            return;
        }
        WriteLook(look);
    }

    // Old model: markings by category (the category is the look's "organ"), as the game groups them.
    // Unknown markings go under their own id, for the rules to report and drop.
    private static void SortByCategory(Dictionary<string, Dictionary<string, List<MarkingEntry>>> target, IEnumerable<MarkingEntry> markings, CharacterCatalog catalog)
    {
        foreach (var entry in markings)
        {
            var (category, layer) = catalog.Markings.TryGetValue(entry.Id, out var marking) && marking.Category != null
                ? (marking.Category, marking.Layer)
                : (entry.Id, entry.Id);
            if (!target.TryGetValue(category, out var byLayer))
                target[category] = byLayer = [];
            if (!byLayer.TryGetValue(layer, out var list))
                byLayer[layer] = list = [];
            list.Add(entry);
        }
    }

    /// <summary>
    /// Writes an old-model look: hair and facial hair in their own fields, every other marking in
    /// one list. Only what changed is rewritten, so a file the game wrote comes out as it went in: a
    /// category whose markings are unchanged keeps its entries where they were; a changed one is
    /// written where it first appeared; new categories go at the end.
    /// </summary>
    private void WriteOldLook(CharacterLook look)
    {
        if (Species != look.Species)
            Species = look.Species;
        if (Sex != look.Sex)
            Sex = look.Sex;
        var appearance = Appearance;
        if (!IsOldModel)
        {
            // A new-model or empty appearance: the old fields go first, in the order the game
            // writes them, and the file becomes version 1 as the old model's exports are.
            var others = appearance.Children
                .Where(kv => ((YamlScalarNode)kv.Key).Value is not ("markings" or "skinColor" or "eyeColor"))
                .ToList();
            appearance.Children.Clear();
            appearance.Add("markings", new YamlSequenceNode());
            appearance.Add("skinColor", look.SkinColor.ToHex());
            appearance.Add("eyeColor", look.EyeColor.ToHex());
            appearance.Add("facialHairColor", Black);
            appearance.Add("facialHair", NoFacialHair);
            appearance.Add("hairColor", Black);
            appearance.Add("hair", NoHair);
            foreach (var (key, value) in others)
                appearance.Children[key] = value;
            Version = "1";
        }
        SetIfChanged("skinColor", look.SkinColor.ToHex());
        SetIfChanged("eyeColor", look.EyeColor.ToHex());
        WriteHair("Hair", "hair", "hairColor", NoHair);
        WriteHair("FacialHair", "facialHair", "facialHairColor", NoFacialHair);

        var wanted = look.Markings.Where(kv => kv.Key is not ("Hair" or "FacialHair"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var old = appearance.Children.TryGetValue(new YamlScalarNode("markings"), out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlMappingNode>().ToList()
            : [];
        // The category of each old entry, as the look grouped it.
        string? CategoryOf(YamlMappingNode entry) =>
            ReadMarking(entry) is { } read ? wanted.FirstOrDefault(kv => kv.Value.Values.Any(l => l.Any(e => e.Id == read.Id))).Key : null;
        bool Unchanged(string category)
        {
            var before = old.Where(e => CategoryOf(e) == category).Select(ReadMarking).OfType<MarkingEntry>().ToList();
            var after = wanted[category].Values.SelectMany(l => l).ToList();
            return before.Count == after.Count && before.All(b => after.Any(a => a.Id == b.Id && a.Colors.SequenceEqual(b.Colors)))
                && wanted[category].All(layer => layer.Value.Select(e => e.Id).SequenceEqual(
                    before.Where(b => layer.Value.Any(e => e.Id == b.Id)).Select(b => b.Id)));
        }

        var result = new List<YamlNode>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in old)
        {
            if (CategoryOf(entry) is not { } category)
                continue;
            if (Unchanged(category))
            {
                result.Add(entry);
                continue;
            }
            if (done.Add(category))
                result.AddRange(wanted[category].Values.SelectMany(l => l).Select(e => OldEntry(e, old)));
        }
        foreach (var (category, layers) in wanted)
        {
            if (!done.Contains(category) && !old.Any(e => CategoryOf(e) == category))
                result.AddRange(layers.Values.SelectMany(l => l).Select(e => OldEntry(e, old)));
        }
        if (!result.SequenceEqual(old) || node is not YamlSequenceNode)
            appearance.Children[new YamlScalarNode("markings")] = new YamlSequenceNode(result);

        void SetIfChanged(string key, string value)
        {
            if (!Rgba.TryParse(Scalar(appearance, key), out var current) || current.ToHex() != value)
                SetScalar(appearance, key, value);
        }

        void WriteHair(string category, string idKey, string colorKey, string none)
        {
            var hair = look.Markings.TryGetValue(category, out var layers) ? layers.Values.SelectMany(l => l).FirstOrDefault() : null;
            var id = hair?.Id ?? none;
            if (Scalar(appearance, idKey) != id && !(hair == null && Scalar(appearance, idKey) is null))
                SetScalar(appearance, idKey, id);
            if (hair?.Colors.FirstOrDefault() is { } color && (!Rgba.TryParse(Scalar(appearance, colorKey), out var current) || current.ToHex() != color.ToHex()))
                SetScalar(appearance, colorKey, color.ToHex());
        }
    }

    // The old model's hair colour for a new character (Color.Black).
    private const string Black = "#000000FF";

    // An old-model marking entry: the file's own entry when nothing about it changed, else a new
    // one in the order the game writes them.
    private static YamlMappingNode OldEntry(MarkingEntry entry, List<YamlMappingNode> old) =>
        old.FirstOrDefault(e => ReadMarking(e) is { } read && read.Id == entry.Id && read.Colors.SequenceEqual(entry.Colors))
        ?? new YamlMappingNode
        {
            { "markingId", entry.Id },
            { "visible", "True" },
            { "markingColor", new YamlSequenceNode(entry.Colors.Select(c => (YamlNode)new YamlScalarNode(c.ToHex()))) },
        };

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
            // Empty organs still get written, as {}.
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
            node.Children[new YamlScalarNode(key)] = Text(value);
    }

    /// <summary>
    /// Text as the game writes it (<c>ValueDataNode</c>): quoted when blank or when it reads as
    /// null ("null", "NULL", " Null "), which the game would otherwise take for no value at all.
    /// Halves of broken character pairs are dropped, as nothing can write them.
    /// </summary>
    internal static YamlScalarNode Text(string value)
    {
        value = WithoutLoneSurrogates(value);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase)
            ? new YamlScalarNode(value) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted }
            : new YamlScalarNode(value);
    }

    private static string WithoutLoneSurrogates(string value)
    {
        if (!value.Any(char.IsSurrogate))
            return value;
        var kept = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                kept.Append(value[i]).Append(value[++i]);
            else if (!char.IsSurrogate(value[i]))
                kept.Append(value[i]);
        }
        return kept.ToString();
    }
}
