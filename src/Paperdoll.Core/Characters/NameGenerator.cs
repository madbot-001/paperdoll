using System.Text.RegularExpressions;
using Paperdoll.Core.Locale;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Characters;

/// <summary>
/// Random names as the game picks them (<c>NamingSystem</c>): first names from the species' male or
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

        var male = Scalar(node, "maleFirstNames") ?? "NamesFirstMale";
        var female = Scalar(node, "femaleFirstNames") ?? "NamesFirstFemale";
        var last = Scalar(node, "lastNames") ?? "NamesLast";

        string First() => Pick(gender switch
        {
            "Male" => male,
            "Female" => female,
            _ => _random.Next(2) == 0 ? male : female,
        });

        return Placeholder().Replace(pattern, m => m.Groups[1].Value switch
        {
            "first" or "first1" or "first2" => First(),
            "last" => Pick(last),
            _ => "",
        }).Trim();
    }

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
