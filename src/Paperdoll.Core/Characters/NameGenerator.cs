using System.Text.RegularExpressions;
using Paperdoll.Core.Locale;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Characters;

/// <summary>
/// Random names (<c>NamingSystem</c>): first names from the species' male or
/// female list (either, for other pronouns), last names from its last-name list, put together by
/// the fork's <c>namepreset-&lt;naming&gt;</c> text, or first and last when the fork has none.
/// </summary>
public sealed partial class NameGenerator(PrototypeIndex prototypes, FluentStrings strings, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public string Next(SpeciesInfo species, string? gender)
    {
        var node = species.Node;
        var naming = Scalar(node, "naming") ?? "FirstLast";
        var pattern = strings[$"namepreset-{naming.ToLowerInvariant()}"]
            ?? strings["namepreset-firstlast"]
            ?? "{$first} {$last}";

        // Older forks' defaults have the older dataset ids.
        var male = Scalar(node, "maleFirstNames") ?? Existing("NamesFirstMale", "names_first_male");
        var female = Scalar(node, "femaleFirstNames") ?? Existing("NamesFirstFemale", "names_first_female");
        var last = Scalar(node, "lastNames") ?? Existing("NamesLast", "names_last");

        string First() => Pick(gender switch
        {
            "Male" => male,
            "Female" => female,
            _ => _random.Next(2) == 0 ? male : female,
        });

        // Starlight's presets also use prefix and suffix (first and last) and id (a number of four digits).
        return Placeholder().Replace(pattern, m => m.Groups[1].Value switch
        {
            "first" or "first1" or "first2" or "prefix" => First(),
            "last" or "suffix" => Pick(last),
            "id" => _random.Next(100, 9999).ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            _ => "",
        }).Trim();
    }

    private string Existing(string id, string older) =>
        prototypes.Resolve("localizedDataset", id) != null || prototypes.Resolve("dataset", id) != null ? id : older;

    // A localizedDataset (message ids prefix1..prefixN) or a plain dataset (a list of values).
    private string Pick(string datasetId)
    {
        if (prototypes.Resolve("localizedDataset", datasetId) is { } localized
            && localized.Children.TryGetValue(new YamlScalarNode("values"), out var values) && values is YamlMappingNode map
            && Scalar(map, "prefix") is { } prefix && int.TryParse(Scalar(map, "count"), out var count) && count > 0)
            return strings.Get(prefix + (_random.Next(count) + 1));

        if (prototypes.Resolve("dataset", datasetId) is { } plain
            && plain.Children.TryGetValue(new YamlScalarNode("values"), out var list) && list is YamlSequenceNode seq && seq.Children.Count > 0)
            return ((YamlScalarNode)seq.Children[_random.Next(seq.Children.Count)]).Value ?? "";

        return "";
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    [GeneratedRegex(@"\{\s*\$([a-z0-9]+)\s*\}")]
    private static partial Regex Placeholder();
}
