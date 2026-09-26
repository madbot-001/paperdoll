// Ported from RobustToolbox (Robust.Shared/Serialization/Manager/SerializationManager.Composition.cs
// and Robust.Shared/Serialization/TypeSerializers/Implementations/ComponentRegistrySerializer.cs,
// written 2021-2022), Copyright (c) 2019 Space Station 14 Contributors, MIT licence.
// See THIRD-PARTY-NOTICES.md.

using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Prototypes;

/// <summary>
/// How a prototype takes values from its parents, following the game engine:
/// <list type="bullet">
/// <item>A field the child sets replaces the parent's whole value.</item>
/// <item>A field the child leaves out comes from the parent.</item>
/// <item>A few fields merge instead (the engine's <c>[AlwaysPushInheritance]</c>): mappings combine
/// key by key with the child winning, lists get the parent's items after the child's.</item>
/// <item>An entity's components merge by type, each component by the same rules; a component only
/// the parent has is added.</item>
/// <item>With several parents, each is applied in turn, so the first listed wins.</item>
/// </list>
/// </summary>
public static class Inheritance
{
    private static readonly HashSet<string> NeverInherited = ["abstract", "parent", "id", "type"];

    /// <summary>Prototype fields that merge with the parent's value (by prototype kind).</summary>
    public static readonly Dictionary<string, HashSet<string>> MergedFields = new()
    {
        ["entity"] = ["components"],
        ["markingsGroup"] = ["limits", "appearances"],
        // Euphoria lets role loadouts have parents and adds a parent's groups after the child's
        // (its RoleLoadoutPrototype.Groups is [AlwaysPushInheritance]). Upstream's have no parents.
        ["roleLoadout"] = ["groups"],
        // A loadout group adds its parents' loadouts after its own (LoadoutGroupPrototype.Loadouts).
        ["loadoutGroup"] = ["loadouts"],
    };

    /// <summary>Component fields that merge with the parent's value (by component type).</summary>
    public static readonly Dictionary<string, HashSet<string>> MergedComponentFields = new()
    {
        ["VisualOrgan"] = ["data"],
        ["VisualOrganMarkings"] = ["markingData"],
    };

    private static readonly YamlScalarNode ComponentsKey = new("components");
    private static readonly YamlScalarNode TypeKey = new("type");

    /// <summary>A new node: the child with one resolved parent applied.</summary>
    public static YamlMappingNode Apply(string kind, YamlMappingNode parent, YamlMappingNode child)
    {
        MergedFields.TryGetValue(kind, out var merged);
        var result = new YamlMappingNode(child.Children);

        foreach (var (keyNode, parentValue) in parent.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value!;
            if (NeverInherited.Contains(key))
                continue;

            if (!result.Children.TryGetValue(keyNode, out var childValue))
                result.Children[keyNode] = parentValue;
            else if (kind == "entity" && keyNode.Equals(ComponentsKey))
                result.Children[keyNode] = MergeComponents((YamlSequenceNode)parentValue, (YamlSequenceNode)childValue);
            else if (merged != null && merged.Contains(key))
                result.Children[keyNode] = Combine(parentValue, childValue);
        }
        return result;
    }

    private static YamlSequenceNode MergeComponents(YamlSequenceNode parent, YamlSequenceNode child)
    {
        var result = new List<YamlNode>(child.Children);
        foreach (var parentComponent in parent.Children.OfType<YamlMappingNode>())
        {
            var type = PrototypeIndex.Scalar(parentComponent, "type");
            var index = result.FindIndex(c => c is YamlMappingNode m && PrototypeIndex.Scalar(m, "type") == type);
            if (index < 0)
                result.Add(parentComponent);
            else
                result[index] = ApplyComponent(type!, parentComponent, (YamlMappingNode)result[index]);
        }
        return new YamlSequenceNode(result);
    }

    private static YamlMappingNode ApplyComponent(string type, YamlMappingNode parent, YamlMappingNode child)
    {
        MergedComponentFields.TryGetValue(type, out var merged);
        var result = new YamlMappingNode(child.Children);
        foreach (var (keyNode, parentValue) in parent.Children)
        {
            if (!result.Children.TryGetValue(keyNode, out var childValue))
                result.Children[keyNode] = parentValue;
            else if (merged != null && merged.Contains(((YamlScalarNode)keyNode).Value!) && !keyNode.Equals(TypeKey))
                result.Children[keyNode] = Combine(parentValue, childValue);
        }
        return result;
    }

    private static YamlNode Combine(YamlNode parent, YamlNode child) => (parent, child) switch
    {
        (YamlMappingNode p, YamlMappingNode c) => CombineMappings(p, c),
        (YamlSequenceNode p, YamlSequenceNode c) => new YamlSequenceNode(c.Children.Concat(p.Children)),
        _ => child,
    };

    private static YamlMappingNode CombineMappings(YamlMappingNode parent, YamlMappingNode child)
    {
        var result = new YamlMappingNode(child.Children);
        foreach (var (key, value) in parent.Children)
            result.Children.TryAdd(key, value);
        return result;
    }
}
