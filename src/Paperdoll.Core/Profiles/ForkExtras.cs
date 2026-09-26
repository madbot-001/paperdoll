// The extra character fields of Delta-V and Euphoria: character records and allergies (from
// Cosmatic Drift) and Euphoria's custom species name. Written for Paperdoll from how those forks
// behave; their code is AGPLv3, so none of it is copied. Limits and defaults match theirs.

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
        var value = !species.CustomName || string.IsNullOrEmpty(written) ? "" : CharacterRules.RemoveMarkup(written);
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

/// <summary>One entry in a character record list: a title, who was involved, and what happened.</summary>
public sealed record RecordEntry(string Title, string Involved, string Description);

/// <summary>
/// Character records (<c>cosmaticDriftCharacterRecords</c>), in Delta-V and Euphoria: height and
/// weight, a few short texts, a work authorisation flag, and employment, security and medical
/// entries. The game writes a record's fields in the reverse of the order its code declares them,
/// so new records here are written in that order too.
/// </summary>
public static class CharacterRecords
{
    public const string Key = "cosmaticDriftCharacterRecords";

    /// <summary>Longest short text (names, features, instructions) and longest entry description.</summary>
    public const int ShortText = 64;
    public const int LongText = 8192;

    /// <summary>Height in centimetres and weight in kilograms run from 0 to these.</summary>
    public const int MaxHeight = 800;
    public const int MaxWeight = 300;

    public static readonly string[] EntryLists = ["employmentEntries", "securityEntries", "medicalEntries"];

    public static readonly string[] Texts = ["emergencyContactName", "identifyingFeatures", "allergies", "drugAllergies", "postmortemInstructions"];

    // Every field in the order the game writes them, with its default.
    private static readonly (string Key, Func<YamlNode> Default)[] Fields =
    [
        ("employmentEntries", () => new YamlSequenceNode()),
        ("securityEntries", () => new YamlSequenceNode()),
        ("medicalEntries", () => new YamlSequenceNode()),
        ("postmortemInstructions", () => CharacterFile.Text("Return home")),
        ("drugAllergies", () => CharacterFile.Text("None")),
        ("allergies", () => CharacterFile.Text("None")),
        ("identifyingFeatures", () => CharacterFile.Text("")),
        ("hasWorkAuthorization", () => new YamlScalarNode("True")),
        ("emergencyContactName", () => CharacterFile.Text("")),
        ("weight", () => new YamlScalarNode("70")),
        ("height", () => new YamlScalarNode("170")),
    ];

    /// <summary>
    /// As the game checks records: missing ones get the defaults, missing fields their default,
    /// numbers are clamped and texts cut to their limits.
    /// </summary>
    public static void Check(CharacterFile file, List<RuleFix> fixes)
    {
        var records = Node(file);
        var changed = false;
        changed |= ClampNumber(records, "height", MaxHeight, 170);
        changed |= ClampNumber(records, "weight", MaxWeight, 70);
        foreach (var key in Texts)
            changed |= Cut(records, key, ShortText);
        if (CharacterFile.Scalar(records, "hasWorkAuthorization") is not ("True" or "False" or "true" or "false"))
        {
            records.Children[new YamlScalarNode("hasWorkAuthorization")] = new YamlScalarNode("True");
            changed = true;
        }
        foreach (var list in EntryLists)
        {
            var entries = Entries(file, list);
            var fitted = entries.Select(e => new RecordEntry(Fit(e.Title, ShortText), Fit(e.Involved, ShortText), Fit(e.Description, LongText))).ToList();
            if (!fitted.SequenceEqual(entries) || !EntriesComplete(records, list))
            {
                SetEntries(file, list, fitted);
                changed = true;
            }
        }
        if (changed)
            fixes.Add(new(Key, $"Character records were fitted to the game's limits: texts up to {ShortText} characters, entry descriptions up to {LongText}, height up to {MaxHeight} cm and weight up to {MaxWeight} kg."));
    }

    /// <summary>The records mapping, made with the game's defaults (and missing fields filled in, in the game's order) if needed.</summary>
    public static YamlMappingNode Node(CharacterFile file)
    {
        var profile = file.Profile;
        var existing = profile.Children.TryGetValue(new YamlScalarNode(Key), out var node) ? node as YamlMappingNode : null;
        if (existing != null && Fields.All(f => existing.Children.ContainsKey(new YamlScalarNode(f.Key))))
            return existing;

        var records = new YamlMappingNode();
        foreach (var (key, make) in Fields)
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

    public static string Text(CharacterFile file, string key) => CharacterFile.Scalar(Node(file), key) ?? "";

    public static void SetText(CharacterFile file, string key, string value) => Node(file).Children[new YamlScalarNode(key)] = CharacterFile.Text(value);

    public static int Number(CharacterFile file, string key) =>
        int.TryParse(CharacterFile.Scalar(Node(file), key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static void SetNumber(CharacterFile file, string key, int value) =>
        Node(file).Children[new YamlScalarNode(key)] = new YamlScalarNode(value.ToString(CultureInfo.InvariantCulture));

    public static bool WorkAuthorization(CharacterFile file) => CharacterFile.Scalar(Node(file), "hasWorkAuthorization") is not ("False" or "false");

    public static void SetWorkAuthorization(CharacterFile file, bool value) =>
        Node(file).Children[new YamlScalarNode("hasWorkAuthorization")] = new YamlScalarNode(value ? "True" : "False");

    public static IReadOnlyList<RecordEntry> Entries(CharacterFile file, string list) =>
        Node(file).Children.TryGetValue(new YamlScalarNode(list), out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlMappingNode>().Select(e => new RecordEntry(
                CharacterFile.Scalar(e, "title") ?? "", CharacterFile.Scalar(e, "involved") ?? "", CharacterFile.Scalar(e, "description") ?? "")).ToList()
            : [];

    /// <summary>Writes a list's entries, each as the game writes them: description, involved, title.</summary>
    public static void SetEntries(CharacterFile file, string list, IEnumerable<RecordEntry> entries) =>
        Node(file).Children[new YamlScalarNode(list)] = new YamlSequenceNode(entries.Select(e => (YamlNode)new YamlMappingNode
        {
            { "description", CharacterFile.Text(e.Description) },
            { "involved", CharacterFile.Text(e.Involved) },
            { "title", CharacterFile.Text(e.Title) },
        }));

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

    /// <summary>Every reagent, as the lobby's allergy picker offers them.</summary>
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
