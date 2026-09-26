// Upstream's trait rules are ported from Space Station 14 (HumanoidCharacterProfile.GetValidTraits),
// Copyright (c) 2017-2026 Space Wizards Federation, MIT licence. See THIRD-PARTY-NOTICES.md.
// The Delta-V-style limits (per-category trait counts, overall count and points, conditions and
// conflicts) are written from how those forks behave, not from their code.

using System.Text.RegularExpressions;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Traits;

/// <summary>Which trait system a fork has.</summary>
public enum TraitStyle
{
    /// <summary>Categories may cap points (<c>maxTraitPoints</c>); no conditions.</summary>
    Upstream,

    /// <summary>
    /// Delta-V's rewrite, also in Euphoria and Floof: categories cap points and trait counts, traits
    /// have conditions and conflicts, and there are overall limits on count and points.
    /// </summary>
    DeltaV,
}

/// <summary>A fork's trait system and its overall limits (the server settings' defaults).</summary>
public sealed record TraitRules(TraitStyle Style, int? MaxCount, int? MaxPoints)
{
    public static readonly TraitRules Upstream = new(TraitStyle.Upstream, null, null);

    /// <summary>Delta-V's <c>traits.max_count</c> 25 and <c>traits.max_points</c> 15.</summary>
    public static readonly TraitRules DeltaV = new(TraitStyle.DeltaV, 25, 15);
}

public sealed record TraitCategoryInfo(string Id, string NameKey, int? MaxPoints, int? MaxTraits, int Priority, string? AccentColor);

/// <summary>A trait condition: what kind, the values it names, and whether it is inverted.</summary>
public sealed record TraitCondition(string Kind, bool Invert, IReadOnlyList<string> Values, IReadOnlyList<string> Conflicts, IReadOnlyList<TraitCondition> Children);

public sealed record TraitInfo(
    string Id,
    string NameKey,
    string? DescriptionKey,
    string? Category,
    int Cost,
    IReadOnlyList<TraitCondition> Conditions,
    IReadOnlyList<string> Conflicts,
    bool UsesSlots,
    bool HasWhitelist,
    string Source);

public enum TraitAvailability
{
    Available,

    /// <summary>Its conditions are not met, as far as Paperdoll can tell.</summary>
    Blocked,

    /// <summary>It depends on something only the server knows (the body's components, antagonist eligibility).</summary>
    Depends,
}

public sealed record TraitStatus(TraitAvailability Availability, string? Reason);

/// <summary>What a trait's conditions are checked against.</summary>
public sealed record TraitContext(string Species, string? Job, string? Department, IReadOnlyCollection<string> Selected);

/// <summary>The traits and trait categories of one fork, and the rules for picking them.</summary>
public sealed partial class TraitCatalog
{
    public required IReadOnlyDictionary<string, TraitInfo> Traits { get; init; }
    public required IReadOnlyDictionary<string, TraitCategoryInfo> Categories { get; init; }

    public static TraitCatalog Build(PrototypeIndex index)
    {
        var categories = index.OfKind("traitCategory").ToDictionary(p => p.Id, p =>
        {
            var node = index.Resolve("traitCategory", p.Id)!;
            return new TraitCategoryInfo(p.Id, Str(node, "name") ?? p.Id,
                Int(node, "maxPoints") ?? Int(node, "maxTraitPoints"), Int(node, "maxTraits"),
                Int(node, "priority") ?? 0, Str(node, "accentColor"));
        }, StringComparer.Ordinal);

        var traits = index.OfKind("trait").ToDictionary(p => p.Id, p =>
        {
            var node = index.Resolve("trait", p.Id)!;
            return new TraitInfo(p.Id, Str(node, "name") ?? p.Id, Str(node, "description"), Str(node, "category"),
                Int(node, "cost") ?? 0, Conditions(node), Strings(node, "conflicts"),
                Str(node, "usesSlots") is not ("false" or "False"),
                node.Children.ContainsKey(new YamlScalarNode("whitelist")) || node.Children.ContainsKey(new YamlScalarNode("blacklist")),
                "Resources/Prototypes/" + p.Path);
        }, StringComparer.Ordinal);

        return new TraitCatalog { Traits = traits, Categories = categories };
    }

    /// <summary>
    /// A trait id as written in a file. Old Einstein Engines-based exports wrote
    /// <c>{Prototype: AnimalFriend}</c>; that becomes <c>AnimalFriend</c>.
    /// </summary>
    public static string NormalizeId(string written)
    {
        var match = OldStyleId().Match(written);
        return match.Success ? match.Groups[1].Value : written.Trim();
    }

    /// <summary>
    /// The traits the game keeps when it checks a character (<c>GetValidTraits</c>): known traits
    /// with a known category (or, upstream, none), taken in order while each category's points stay
    /// within its cap.
    /// </summary>
    public List<string> Valid(IEnumerable<string> traits, TraitRules rules)
    {
        var points = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var id in traits.Distinct(StringComparer.Ordinal))
        {
            if (!Traits.TryGetValue(id, out var trait))
                continue;
            if (trait.Category == null)
            {
                if (rules.Style == TraitStyle.Upstream)
                    result.Add(id);
                continue;
            }
            if (!Categories.TryGetValue(trait.Category, out var category))
                continue;
            var spent = points.GetValueOrDefault(category.Id) + trait.Cost;
            if (category.MaxPoints is { } max && spent > max)
                continue;
            points[category.Id] = spent;
            result.Add(id);
        }
        return result;
    }

    /// <summary>Whether the trait's conditions hold for the character.</summary>
    public TraitStatus Evaluate(TraitInfo trait, TraitContext context)
    {
        var depends = (string?)null;
        foreach (var condition in trait.Conditions)
        {
            var (met, reason) = Evaluate(condition, context);
            if (met == false)
                return new TraitStatus(TraitAvailability.Blocked, reason);
            if (met == null)
                depends ??= reason;
        }
        return depends != null ? new TraitStatus(TraitAvailability.Depends, depends) : new TraitStatus(TraitAvailability.Available, null);
    }

    // True, false, or null when only the server can tell; with a reason for the last two.
    private (bool? Met, string? Reason) Evaluate(TraitCondition condition, TraitContext context)
    {
        (bool? met, string reason) = condition.Kind switch
        {
            "Species" => (condition.Values.Contains(context.Species), $"only for {string.Join(", ", condition.Values)}"),
            "Job" => context.Job == null ? (null, $"only as {string.Join(", ", condition.Values)}") : (condition.Values.Contains(context.Job), $"only as {string.Join(", ", condition.Values)}"),
            "Department" => context.Department == null ? (null, $"only in {string.Join(", ", condition.Values)}") : (condition.Values.Contains(context.Department), $"only in {string.Join(", ", condition.Values)}"),
            "Dependency" => (condition.Values.All(context.Selected.Contains) && !condition.Conflicts.Any(context.Selected.Contains),
                condition.Values.Count > 0 ? $"needs {string.Join(", ", condition.Values)}" : $"not with {string.Join(", ", condition.Conflicts)}"),
            "AnyOf" => AnyOf(condition, context),
            "Component" => ((bool?)null, "depends on the body; the server checks"),
            "AntagEligible" => ((bool?)null, "depends on antagonist eligibility; the server checks"),
            _ => ((bool?)null, "has a condition only the server checks"),
        };
        if (met is { } value && condition.Invert)
            return (!value, "not " + reason);
        return (met, reason);
    }

    private (bool? Met, string Reason) AnyOf(TraitCondition condition, TraitContext context)
    {
        var results = condition.Children.Select(c => Evaluate(c, context)).ToList();
        if (results.Any(r => r.Met == true))
            return (true, "");
        return (results.Any(r => r.Met == null) ? null : false, string.Join(" or ", results.Select(r => r.Reason)));
    }

    /// <summary>
    /// Why the trait cannot be added now, or null if it can: its conditions, conflicts with
    /// traits already picked, and the category and overall limits.
    /// </summary>
    public string? WhyNot(TraitInfo trait, TraitContext context, TraitRules rules)
    {
        if (Evaluate(trait, context) is { Availability: TraitAvailability.Blocked, Reason: var reason })
            return $"This trait is {reason}.";

        var selected = context.Selected.Where(Traits.ContainsKey).Select(id => Traits[id]).ToList();
        if (selected.FirstOrDefault(t => t.Conflicts.Contains(trait.Id) || trait.Conflicts.Contains(t.Id)) is { } conflict)
            return $"It conflicts with {conflict.Id}.";

        if (trait.Category != null && Categories.TryGetValue(trait.Category, out var category))
        {
            var inCategory = selected.Where(t => t.Category == category.Id).ToList();
            if (category.MaxTraits is { } maxTraits && trait.UsesSlots && inCategory.Count(t => t.UsesSlots) >= maxTraits)
                return $"This category takes at most {maxTraits} traits.";
            if (category.MaxPoints is { } maxPoints && inCategory.Sum(t => t.Cost) + trait.Cost > maxPoints)
                return $"This category has {maxPoints} points to spend.";
        }
        if (rules.MaxCount is { } maxCount && trait.UsesSlots && selected.Count(t => t.UsesSlots) >= maxCount)
            return $"At most {maxCount} traits in all.";
        if (rules.MaxPoints is { } maxAll && selected.Sum(t => t.Cost) + trait.Cost > maxAll)
            return $"At most {maxAll} trait points in all.";
        return null;
    }

    private static List<TraitCondition> Conditions(YamlMappingNode node, string key = "conditions")
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var list) || list is not YamlSequenceNode seq)
            return [];
        return seq.Children.OfType<YamlMappingNode>().Select(Condition).ToList();
    }

    private static TraitCondition Condition(YamlMappingNode node)
    {
        var tag = node.Tag.IsEmpty ? "" : node.Tag.Value;
        var invert = Str(node, "invert") is "true" or "True";
        return tag switch
        {
            "!type:IsSpeciesCondition" => new("Species", invert, Scalars(node, "species"), [], []),
            "!type:OneOfSpeciesCondition" => new("Species", invert, Scalars(node, "species"), [], []),
            "!type:HasJobCondition" => new("Job", invert, Scalars(node, "job"), [], []),
            "!type:InDepartmentCondition" => new("Department", invert, Scalars(node, "department"), [], []),
            "!type:HasCompCondition" => new("Component", invert, Scalars(node, "component"), [], []),
            "!type:IsAntagEligibleCondition" => new("AntagEligible", invert, [], [], []),
            "!type:TraitDependencyCondition" => new("Dependency", invert, Strings(node, "requires"), Strings(node, "conflicts"), []),
            "!type:AnyOfCondition" => new("AnyOf", invert, [], [], Conditions(node)),
            _ => new("Unknown", invert, [], [], []),
        };
    }

    // A value written either as one scalar or as a list.
    private static List<string> Scalars(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) switch
        {
            false => [],
            _ => value switch
            {
                YamlScalarNode { Value: { } one } => [one],
                YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList(),
                _ => [],
            },
        };

    private static List<string> Strings(YamlMappingNode node, string key) => Scalars(node, key);

    private static string? Str(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private static int? Int(YamlMappingNode node, string key) => int.TryParse(Str(node, key), out var value) ? value : null;

    [GeneratedRegex(@"^\s*\{\s*Prototype\s*:\s*([^}\s]+)\s*\}\s*$")]
    private static partial Regex OldStyleId();
}
