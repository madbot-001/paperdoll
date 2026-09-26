using System.Text;
using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Tests;

public class PrototypeIndexTests
{
    private static PrototypeIndex Load(params (string Path, string Yaml)[] files) =>
        PrototypeIndex.Load(files.Select(f => new PrototypeSource(f.Path, Encoding.UTF8.GetBytes(f.Yaml))));

    private static string? Get(YamlMappingNode node, params string[] path)
    {
        YamlNode current = node;
        foreach (var key in path)
        {
            if (current is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(key), out var next))
                return null;
            current = next;
        }
        return current is YamlScalarNode scalar ? scalar.Value : null;
    }

    private static YamlMappingNode Component(YamlMappingNode entity, string type) =>
        ((YamlSequenceNode)entity.Children[new YamlScalarNode("components")]).Children
            .OfType<YamlMappingNode>()
            .Single(c => Get(c, "type") == type);

    [Fact]
    public void Reads_prototypes_by_kind_and_id_and_skips_a_byte_order_mark()
    {
        var index = Load(("a.yml", "﻿- type: species\n  id: Human\n  roundStart: true\n- type: marking\n  id: Scar\n"));

        Assert.True(index.TryGet("species", "Human", out var human));
        Assert.Equal("true", Get(human.Node, "roundStart"));
        Assert.True(index.TryGet("marking", "Scar", out _));
        Assert.Empty(index.Problems);
    }

    [Fact]
    public void A_bare_null_means_no_value_and_still_overrides_a_parent()
    {
        var index = Load(("m.yml", """
            - type: marking
              id: Base
              groupWhitelist: [ Human ]
            - type: marking
              parent: Base
              id: Anyone
              groupWhitelist: null # Delta-V's way of lifting the restriction
            - type: marking
              id: Quoted
              sexRestriction: 'null'
            """));

        Assert.Null(Get(index.Resolve("marking", "Anyone")!, "groupWhitelist"));
        Assert.False(index.Resolve("marking", "Anyone")!.Children.ContainsKey(new YamlScalarNode("groupWhitelist")));
        Assert.NotNull(index.Resolve("marking", "Base")!.Children[new YamlScalarNode("groupWhitelist")]);
        // Only a bare null counts: a quoted one is the text "null".
        Assert.Equal("null", Get(index.Resolve("marking", "Quoted")!, "sexRestriction"));
    }

    [Fact]
    public void Keeps_tagged_values()
    {
        var index = Load(("a.yml", "- type: skinColoration\n  id: Tone\n  strategy: !type:HumanToned\n    min: 1\n"));

        var strategy = (YamlMappingNode)index.Resolve("skinColoration", "Tone")!.Children[new YamlScalarNode("strategy")];
        Assert.Equal("!type:HumanToned", strategy.Tag.Value);
        Assert.Equal("1", Get(strategy, "min"));
    }

    [Fact]
    public void A_broken_file_is_reported_and_the_rest_still_loads()
    {
        var index = Load(("bad.yml", "- type: species\n  id: [unclosed\n"), ("good.yml", "- type: species\n  id: Human\n"));

        Assert.Single(index.Problems);
        Assert.Equal("bad.yml", index.Problems[0].Path);
        Assert.True(index.TryGet("species", "Human", out _));
    }

    [Fact]
    public void A_child_field_replaces_the_parents_whole_value()
    {
        var index = Load(("a.yml", """
            - type: entity
              id: Base
              abstract: true
              name: base
              components:
              - type: Sprite
                sprite: base.rsi
                state: torso
            - type: entity
              id: Child
              parent: Base
              components:
              - type: Sprite
                sprite: child.rsi
            """));

        var child = index.Resolve("entity", "Child")!;

        Assert.Equal("base", Get(child, "name"));
        Assert.Null(Get(child, "abstract"));
        var sprite = Component(child, "Sprite");
        Assert.Equal("child.rsi", Get(sprite, "sprite"));
        Assert.Equal("torso", Get(sprite, "state"));
    }

    [Fact]
    public void Nested_values_are_replaced_whole_unless_the_field_always_merges()
    {
        var index = Load(("a.yml", """
            - type: entity
              id: Base
              components:
              - type: Sprite
                layers:
                  one: { state: a }
              - type: VisualOrgan
                layer: enum.HumanoidVisualLayers.Chest
                data:
                  sprite: base.rsi
                  state: torso
            - type: entity
              id: Child
              parent: Base
              components:
              - type: Sprite
                layers:
                  two: { state: b }
              - type: VisualOrgan
                data:
                  sprite: child.rsi
            """));

        var child = index.Resolve("entity", "Child")!;

        Assert.Null(Get(Component(child, "Sprite"), "layers", "one", "state"));
        var organ = Component(child, "VisualOrgan");
        Assert.Equal("child.rsi", Get(organ, "data", "sprite"));
        Assert.Equal("torso", Get(organ, "data", "state"));
        Assert.Equal("enum.HumanoidVisualLayers.Chest", Get(organ, "layer"));
    }

    [Fact]
    public void Components_only_the_parent_has_are_added()
    {
        var index = Load(("a.yml", """
            - type: entity
              id: Base
              components:
              - type: Organ
                category: Torso
            - type: entity
              id: Child
              parent: Base
              components:
              - type: Sprite
                sprite: a.rsi
            """));

        var child = index.Resolve("entity", "Child")!;

        Assert.Equal("Torso", Get(Component(child, "Organ"), "category"));
        Assert.Equal("a.rsi", Get(Component(child, "Sprite"), "sprite"));
    }

    [Fact]
    public void With_several_parents_the_first_listed_wins()
    {
        var index = Load(("a.yml", """
            - type: entity
              id: First
              components:
              - type: Sprite
                sprite: first.rsi
            - type: entity
              id: Second
              name: second
              components:
              - type: Sprite
                sprite: second.rsi
                state: from-second
            - type: entity
              id: Child
              parent: [ First, Second ]
            """));

        var child = index.Resolve("entity", "Child")!;

        var sprite = Component(child, "Sprite");
        Assert.Equal("first.rsi", Get(sprite, "sprite"));
        Assert.Equal("from-second", Get(sprite, "state"));
        Assert.Equal("second", Get(child, "name"));
    }

    [Fact]
    public void Grandparents_apply_through_parents()
    {
        var index = Load(("a.yml", """
            - type: entity
              id: Grand
              components:
              - type: Organ
                category: Head
            - type: entity
              id: Parent
              parent: Grand
            - type: entity
              id: Child
              parent: Parent
            """));

        Assert.Equal("Head", Get(Component(index.Resolve("entity", "Child")!, "Organ"), "category"));
    }

    [Fact]
    public void Marking_group_limits_merge_layer_by_layer()
    {
        var index = Load(("a.yml", """
            - type: markingsGroup
              id: Undergarments
              limits:
                enum.HumanoidVisualLayers.UndergarmentTop:
                  limit: 1
                enum.HumanoidVisualLayers.Hair:
                  limit: 1
            - type: markingsGroup
              id: Vox
              parent: Undergarments
              limits:
                enum.HumanoidVisualLayers.Hair:
                  limit: 2
            """));

        var vox = index.Resolve("markingsGroup", "Vox")!;

        Assert.Equal("2", Get(vox, "limits", "enum.HumanoidVisualLayers.Hair", "limit"));
        Assert.Equal("1", Get(vox, "limits", "enum.HumanoidVisualLayers.UndergarmentTop", "limit"));
    }

    [Fact]
    public void A_duplicate_id_keeps_the_first_and_is_reported()
    {
        var index = Load(("a.yml", "- type: species\n  id: Human\n  name: first\n"), ("b.yml", "- type: species\n  id: Human\n  name: second\n"));

        Assert.Equal("first", Get(index.Resolve("species", "Human")!, "name"));
        Assert.Contains(index.Problems, p => p.Message.Contains("Duplicate"));
    }
}
