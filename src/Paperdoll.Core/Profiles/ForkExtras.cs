// The extra character fields of Delta-V and Euphoria: character records and allergies (from
// Cosmatic Drift) and Euphoria's custom species name. Written for Paperdoll from how those forks
// behave; their code is AGPLv3, so none of it is copied. Limits and defaults match theirs.
// Also the key names of Starlight's character info, read from its profile, and Starlight's
// version of the records.

using System.Globalization;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Profiles;

/// <summary>Euphoria's custom species name (<c>customspeciename</c>), shown in place of the species' name.</summary>
public static class CustomSpeciesName
{
    public const string Key = "customspeciename";

    /// <summary>
    /// As the game checks it: empty when the species allows none (<c>customName: false</c>),
    /// otherwise without markup and cut to the name length (32).
    /// </summary>
    public static void Check(CharacterFile file, SpeciesInfo species, List<RuleFix> fixes)
    {
        var written = file.GetValue(Key);
        var value = !species.CustomName || string.IsNullOrEmpty(written) ? "" : MarkupText.Stable(written);
        if (value.Length > CharacterRules.MaxNameLength)
            value = value[..CharacterRules.MaxNameLength];
        if (!string.IsNullOrEmpty(written) && value != written)
        {
            fixes.Add(new(Key, species.CustomName
                ? $"Custom species names are at most {CharacterRules.MaxNameLength} characters, without markup."
                : $"{species.Id} cannot have a custom species name; it was cleared."));
        }
        if (value != written)
            file.SetValue(Key, value);
    }
}

/// <summary>
/// The keys of Starlight's character info. The game names each after its field with only the
/// first letter lowered, so its <c>OOCNotes</c> is saved as <c>oOCNotes</c>. None has a length
/// limit, and the game writes each one, empty when unused.
/// </summary>
public static class CharacterInfo
{
    public const string PersonalityDescription = "personalityDescription";
    public const string ExploitableInfo = "exploitableInfo";
    public const string Secrets = "secrets";
    public const string PersonalNotes = "personalNotes";
    public const string OocNotes = "oOCNotes";
    public const string SiliconVoice = "siliconVoice";

    // In the appearance.
    public const string HairGlowing = "hairGlowing";
    public const string FacialHairGlowing = "facialHairGlowing";
    public const string EyeGlowing = "eyeGlowing";
}

/// <summary>One entry in a character record list: a title, who was involved, and what happened.</summary>
public sealed record RecordEntry(string Title, string Involved, string Description);

/// <summary>
/// How a fork keeps its character records. Delta-V and Euphoria keep Cosmatic Drift's; Starlight
/// adds admin entries, halves the entry description limit, starts records at 164 cm and 74 kg,
/// and works height and weight out from the character's size.
/// </summary>
/// <param name="EntryLists">The entry lists, in the order Paperdoll shows them.</param>
/// <param name="LongText">Longest entry description.</param>
/// <param name="DefaultHeight">Height in new records, in centimetres.</param>
/// <param name="DefaultWeight">Weight in new records, in kilograms.</param>
/// <param name="SortedByName">
/// Whether the game writes fields sorted by name, last first (Starlight's engine), rather than in
/// the reverse of the order its code declares them. Missing fields are then added after the ones
/// the file has, which keep their place.
/// </param>
/// <param name="HeightFromSize">Whether the game sets height and weight from the character's size and species, so the player cannot.</param>
public sealed record RecordFormat(IReadOnlyList<string> EntryLists, int LongText, int DefaultHeight, int DefaultWeight, bool SortedByName, bool HeightFromSize)
{
    public static readonly RecordFormat CosmaticDrift = new(["employmentEntries", "securityEntries", "medicalEntries"], 8192, 170, 70, false, false);
    public static readonly RecordFormat Starlight = new(["employmentEntries", "securityEntries", "medicalEntries", "adminEntries"], 4096, 164, 74, true, true);
}

/// <summary>
/// Character records (<c>cosmaticDriftCharacterRecords</c>), in Delta-V, Euphoria and Starlight:
/// height and weight, a few short texts, a work authorisation flag, and lists of entries. New
/// records and fields are written in the order the fork's game writes them (see
/// <see cref="RecordFormat"/>). Without a format, the methods use Cosmatic Drift's.
/// </summary>
public static class CharacterRecords
{
    public const string Key = "cosmaticDriftCharacterRecords";

    /// <summary>Longest short text (names, features, instructions); the longest entry description is the format's.</summary>
    public const int ShortText = 64;

    /// <summary>Height in centimetres and weight in kilograms run from 0 to these.</summary>
    public const int MaxHeight = 800;
    public const int MaxWeight = 300;

    public static readonly string[] Texts = ["emergencyContactName", "identifyingFeatures", "allergies", "drugAllergies", "postmortemInstructions"];

    // Every field in the order the game writes them, with its default.
    private static IEnumerable<(string Key, Func<YamlNode> Default)> Fields(RecordFormat format)
    {
        var fields = format.EntryLists.Select(list => (list, (Func<YamlNode>)(() => new YamlSequenceNode())))
            .Concat(new (string, Func<YamlNode>)[]
            {
                ("postmortemInstructions", () => CharacterFile.Text("Return home")),
                ("drugAllergies", () => CharacterFile.Text("None")),
                ("allergies", () => CharacterFile.Text("None")),
                ("identifyingFeatures", () => CharacterFile.Text("")),
                ("hasWorkAuthorization", () => new YamlScalarNode("True")),
                ("emergencyContactName", () => CharacterFile.Text("")),
                ("weight", () => new YamlScalarNode(format.DefaultWeight.ToString(CultureInfo.InvariantCulture))),
                ("height", () => new YamlScalarNode(format.DefaultHeight.ToString(CultureInfo.InvariantCulture))),
            });
        // Field names differ from the code's only in their first letter, which the sort ignores.
        return format.SortedByName ? fields.OrderByDescending(f => f.Item1, StringComparer.OrdinalIgnoreCase) : fields;
    }

    // An entry's fields in the order the game writes them.
    private static string[] EntryKeys(RecordFormat format) =>
        format.SortedByName ? ["title", "involved", "description"] : ["description", "involved", "title"];

    /// <summary>
    /// As the game checks records: missing ones get the defaults, missing fields their default,
    /// numbers are clamped and texts cut to their limits.
    /// </summary>
    public static void Check(CharacterFile file, RecordFormat format, List<RuleFix> fixes)
    {
        var records = Node(file, format);
        var changed = false;
        changed |= ClampNumber(records, "height", MaxHeight, format.DefaultHeight);
        changed |= ClampNumber(records, "weight", MaxWeight, format.DefaultWeight);
        foreach (var key in Texts)
            changed |= Cut(records, key, ShortText);
        if (CharacterFile.Scalar(records, "hasWorkAuthorization") is not ("True" or "False" or "true" or "false"))
        {
            records.Children[new YamlScalarNode("hasWorkAuthorization")] = new YamlScalarNode("True");
            changed = true;
        }
        foreach (var list in format.EntryLists)
        {
            var entries = Entries(file, list, format);
            var fitted = entries.Select(e => new RecordEntry(Fit(e.Title, ShortText), Fit(e.Involved, ShortText), Fit(e.Description, format.LongText))).ToList();
            if (!fitted.SequenceEqual(entries) || !EntriesComplete(records, list))
            {
                SetEntries(file, list, fitted, format);
                changed = true;
            }
        }
        if (changed)
            fixes.Add(new(Key, $"Character records were fitted to the game's limits: texts up to {ShortText} characters, entry descriptions up to {format.LongText}, height up to {MaxHeight} cm and weight up to {MaxWeight} kg."));
    }

    /// <summary>The records mapping, made with the game's defaults (and missing fields filled in, in the game's order) if needed.</summary>
    public static YamlMappingNode Node(CharacterFile file, RecordFormat? format = null)
    {
        format ??= RecordFormat.CosmaticDrift;
        var fields = Fields(format).ToList();
        var profile = file.Profile;
        var existing = profile.Children.TryGetValue(new YamlScalarNode(Key), out var node) ? node as YamlMappingNode : null;
        if (existing != null && fields.All(f => existing.Children.ContainsKey(new YamlScalarNode(f.Key))))
            return existing;
        if (existing != null && format.SortedByName)
        {
            // The game reads fields by name, so the file's keep their place and missing ones follow.
            foreach (var (key, make) in fields.Where(f => !existing.Children.ContainsKey(new YamlScalarNode(f.Key))))
                existing.Add(key, make());
            return existing;
        }

        var records = new YamlMappingNode();
        foreach (var (key, make) in fields)
            records.Add(key, existing != null && existing.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : make());
        if (existing != null)
        {
            // Anything else a fork keeps in its records stays, after the known fields.
            foreach (var (key, value) in existing.Children.Where(kv => !records.Children.ContainsKey(kv.Key)))
                records.Add(key, value);
        }
        profile.Children[new YamlScalarNode(Key)] = records;
        return records;
    }

    public static string Text(CharacterFile file, string key, RecordFormat? format = null) => CharacterFile.Scalar(Node(file, format), key) ?? "";

    public static void SetText(CharacterFile file, string key, string value, RecordFormat? format = null) =>
        Node(file, format).Children[new YamlScalarNode(key)] = CharacterFile.Text(value);

    public static int Number(CharacterFile file, string key, RecordFormat? format = null) =>
        int.TryParse(CharacterFile.Scalar(Node(file, format), key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static void SetNumber(CharacterFile file, string key, int value, RecordFormat? format = null) =>
        Node(file, format).Children[new YamlScalarNode(key)] = new YamlScalarNode(value.ToString(CultureInfo.InvariantCulture));

    public static bool WorkAuthorization(CharacterFile file, RecordFormat? format = null) =>
        CharacterFile.Scalar(Node(file, format), "hasWorkAuthorization") is not ("False" or "false");

    public static void SetWorkAuthorization(CharacterFile file, bool value, RecordFormat? format = null) =>
        Node(file, format).Children[new YamlScalarNode("hasWorkAuthorization")] = new YamlScalarNode(value ? "True" : "False");

    public static IReadOnlyList<RecordEntry> Entries(CharacterFile file, string list, RecordFormat? format = null) =>
        Node(file, format).Children.TryGetValue(new YamlScalarNode(list), out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlMappingNode>().Select(e => new RecordEntry(
                CharacterFile.Scalar(e, "title") ?? "", CharacterFile.Scalar(e, "involved") ?? "", CharacterFile.Scalar(e, "description") ?? "")).ToList()
            : [];

    /// <summary>Entries go out in the game's order: description, involved, title; on Starlight title, involved, description.</summary>
    public static void SetEntries(CharacterFile file, string list, IEnumerable<RecordEntry> entries, RecordFormat? format = null)
    {
        format ??= RecordFormat.CosmaticDrift;
        var keys = EntryKeys(format);
        Node(file, format).Children[new YamlScalarNode(list)] = new YamlSequenceNode(entries.Select(e =>
        {
            var entry = new YamlMappingNode();
            foreach (var key in keys)
                entry.Add(key, CharacterFile.Text(key switch { "title" => e.Title, "involved" => e.Involved, _ => e.Description }));
            return (YamlNode)entry;
        }));
    }

    private static bool EntriesComplete(YamlMappingNode records, string list) =>
        records.Children.TryGetValue(new YamlScalarNode(list), out var node) && node is YamlSequenceNode seq
        && seq.Children.All(e => e is YamlMappingNode m && new[] { "description", "involved", "title" }.All(k => m.Children.ContainsKey(new YamlScalarNode(k))));

    private static bool ClampNumber(YamlMappingNode records, string key, int max, int fallback)
    {
        var text = CharacterFile.Scalar(records, key);
        var value = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 0, max) : fallback;
        if (text == value.ToString(CultureInfo.InvariantCulture))
            return false;
        records.Children[new YamlScalarNode(key)] = new YamlScalarNode(value.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static bool Cut(YamlMappingNode records, string key, int max)
    {
        var text = CharacterFile.Scalar(records, key) ?? "";
        if (text.Length <= max)
            return false;
        records.Children[new YamlScalarNode(key)] = CharacterFile.Text(text[..max]);
        return true;
    }

    private static string Fit(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>A reagent a character can be allergic to, in its reagent group.</summary>
public sealed record ReagentInfo(string Id, string NameKey, string Group);

/// <summary>
/// Allergies to reagents (<c>cosmaticDriftAllergies</c>), in Euphoria: reagent id to how strong the
/// reaction is. The lobby offers four strengths; the game does not check the list.
/// </summary>
public static class Allergies
{
    public const string Key = "cosmaticDriftAllergies";

    /// <summary>The lobby's strengths, with the amount each is saved as.</summary>
    public static readonly (string Name, float Amount)[] Strengths = [("Mild", 0.5f), ("Moderate", 1f), ("Severe", 5f), ("Extreme", 100f)];

    /// <summary>A new allergy starts at this strength, as in the lobby.</summary>
    public const float DefaultAmount = 1f;

    /// <summary>All reagents; the allergy picker lists every one.</summary>
    public static IReadOnlyList<ReagentInfo> Reagents(PrototypeIndex prototypes) =>
        prototypes.OfKind("reagent").Where(p => !p.Abstract).Select(p =>
        {
            var node = prototypes.Resolve("reagent", p.Id)!;
            return new ReagentInfo(p.Id, CharacterFile.Scalar(node, "name") ?? p.Id, CharacterFile.Scalar(node, "group") ?? "Unknown");
        }).ToList();

    public static IReadOnlyList<(string Reagent, float Amount)> Read(CharacterFile file) =>
        file.Profile.Children.TryGetValue(new YamlScalarNode(Key), out var node) && node is YamlMappingNode map
            ? map.Children.Select(kv => (((YamlScalarNode)kv.Key).Value ?? "",
                float.TryParse((kv.Value as YamlScalarNode)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ? amount : DefaultAmount)).ToList()
            : [];

    public static void Write(CharacterFile file, IEnumerable<(string Reagent, float Amount)> allergies)
    {
        var map = new YamlMappingNode();
        foreach (var (reagent, amount) in allergies)
            map.Add(reagent, amount.ToString("0.##", CultureInfo.InvariantCulture));
        file.Profile.Children[new YamlScalarNode(Key)] = map;
    }

    /// <summary>The game saves an empty list when there are none.</summary>
    public static void Check(CharacterFile file)
    {
        if (!file.Profile.Children.TryGetValue(new YamlScalarNode(Key), out var node) || node is not YamlMappingNode)
            Write(file, []);
    }

    /// <summary>The strength's name for an amount, or null for one the lobby does not offer.</summary>
    public static string? StrengthName(float amount) => Strengths.FirstOrDefault(s => Math.Abs(s.Amount - amount) < 0.001f).Name;
}
