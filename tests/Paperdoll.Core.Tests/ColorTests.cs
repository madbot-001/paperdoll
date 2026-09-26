using System.Text;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class ColorTests
{
    private static SkinColoration Rule(string yaml)
    {
        var index = PrototypeIndex.Load([new PrototypeSource("c.yml", Encoding.UTF8.GetBytes(yaml))]);
        return SkinColoration.Read(index.Resolve("skinColoration", "Test")!);
    }

    [Theory]
    [InlineData("#FF8040FF")]
    [InlineData("#6C741DFF")]
    [InlineData("#000000FF")]
    [InlineData("#FFFFFF80")]
    public void Hex_and_hsv_and_hsl_round_trip(string hex)
    {
        var color = Rgba.Parse(hex);
        var (h, s, v, a) = color.ToHsv();
        var (hh, ss, ll, aa) = color.ToHsl();

        Assert.Equal(hex, color.ToHex());
        Assert.Equal(hex, Rgba.FromHsv(h, s, v, a).ToHex());
        Assert.Equal(hex, Rgba.FromHsl(hh, ss, ll, aa).ToHex());
    }

    [Fact]
    public void Six_digit_colours_are_opaque()
    {
        Assert.Equal("#6C741DFF", Rgba.Parse("#6c741d").ToHex());
    }

    [Fact]
    public void Human_tone_20_is_hue_25_saturation_20_value_100()
    {
        var rule = Rule("- type: skinColoration\n  id: Test\n  strategy: !type:HumanTonedSkinColoration {}\n");

        Assert.True(rule.IsUnary);
        Assert.Equal("#FFE1CCFF", rule.FromUnary(20).ToHex());
        Assert.True(rule.IsValid(rule.FromUnary(0)));
        Assert.True(rule.IsValid(rule.FromUnary(100)));
        Assert.False(rule.IsValid(Rgba.Parse("#0000FF")));
    }

    [Fact]
    public void Clamped_hsv_pulls_value_into_range()
    {
        var rule = Rule("- type: skinColoration\n  id: Test\n  strategy: !type:ClampedHsvColoration\n    value: [0.175, 1]\n");

        Assert.False(rule.IsValid(Rgba.Parse("#000000")));
        Assert.Equal("#2D2D2DFF", rule.EnsureValid(Rgba.Parse("#000000")).ToHex());
        Assert.Equal("#FF8040FF", rule.EnsureValid(Rgba.Parse("#FF8040")).ToHex());
    }

    [Fact]
    public void Clamped_hsl_pulls_lightness_and_saturation_into_range()
    {
        var rule = Rule("- type: skinColoration\n  id: Test\n  strategy: !type:ClampedHslColoration\n    saturation: [0, 0.1]\n    lightness: [0.85, 1]\n");

        var fixedColor = rule.EnsureValid(Rgba.Parse("#FF0000"));

        Assert.True(rule.IsValid(fixedColor));
        Assert.InRange(fixedColor.ToHsl().L, 0.849f, 1f);
    }

    [Fact]
    public void Hue_nodes_blend_ranges_between_points()
    {
        var rule = Rule("""
            # Upstream's VoxFeathers (Resources/Prototypes/Species/skin_colorations.yml, MIT).
            - type: skinColoration
              id: Test
              strategy: !type:HueNodeClampedHsvColoration
                nodes:
                - hue: 0
                  saturation: [ 0.2, 0.5 ]
                  value: [ 0.36, 0.55 ]
                - hue: 0.045
                  saturation: [ 0.2, 0.8 ]
                  value: [ 0.36, 0.55 ]
                - hue: 0.44
                  saturation: [ 0.2, 0.8 ]
                  value: [ 0.36, 0.55 ]
                - hue: 0.55
                  saturation: [ 0.2, 0.5 ]
                  value: [ 0.36, 0.55 ]
                - hue: 1
                  saturation: [ 0.2, 0.5 ]
                  value: [ 0.36, 0.5 ]
            """);

        // Vox's default tone sits inside its range.
        Assert.True(rule.IsValid(Rgba.Parse("#6c741d")));
        var fixedColor = rule.EnsureValid(Rgba.Parse("#FFFFFF"));
        Assert.True(rule.IsValid(fixedColor));
        Assert.InRange(fixedColor.ToHsv().V, 0.35f, 0.56f);
    }

    [Fact]
    public void An_unknown_rule_allows_any_colour()
    {
        var rule = Rule("- type: skinColoration\n  id: Test\n  strategy: !type:SomeForkColoration {}\n");

        Assert.True(rule.IsValid(Rgba.Parse("#123456")));
    }
}
