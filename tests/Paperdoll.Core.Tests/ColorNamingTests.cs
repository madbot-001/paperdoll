using Paperdoll.Core.Locale;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class ColorNamingTests
{
    // A made-up table with the message ids the game's colour names use, in odd words so that
    // each name shows which messages built it.
    private static FluentStrings Strings()
    {
        var strings = new FluentStrings();
        strings.Add("""
            color-hue-chroma-lightness = {$lightness} {$chroma} {$hue}
            color-hue-chroma = {$chroma} {$hue}
            color-hue-lightness = {$lightness} {$hue}
            color-very-dark = deep
            color-dark = dim
            color-light = bright
            color-very-light = glowing
            color-mixed-hue = {$a}-{$b}
            color-pale = washed
            color-gray-adjective = dull
            color-strong = vivid
            color-pink = rose
            color-red = scarlet
            color-orange = amber
            color-yellow = lemon
            color-green = leaf
            color-cyan = teal
            color-blue = sky
            color-purple = violet
            color-brown = umber
            color-white = snow
            color-gray = ash
            color-black = coal
            color-yellow-color-green = lime
            """);
        return strings;
    }

    [Theory]
    [InlineData("#000000", "coal")]
    [InlineData("#FFFFFF", "snow")]
    [InlineData("#808080", "ash")]
    [InlineData("#FF0000", "vivid scarlet")]
    [InlineData("#FFE0E0", "glowing washed scarlet")]
    [InlineData("#FFFF99", "glowing lemon")]
    public void Names_a_colour_from_the_forks_strings(string hex, string name)
    {
        Assert.Equal(name, ColorNaming.Describe(Rgba.Parse(hex), Strings()));
    }

    [Fact]
    public void A_hue_between_two_names_uses_their_pair_or_both_names()
    {
        // Oklch hue about 123 degrees: past the middle of yellow and green.
        Assert.Equal("vivid lime", ColorNaming.Describe(Rgba.Parse("#88AA00"), Strings()));
        // About 80 degrees: past the middle of orange and yellow, which have no pair name.
        Assert.Equal("amber-lemon", ColorNaming.Describe(Rgba.Parse("#E0A000"), Strings()));
    }

    [Fact]
    public void Names_nothing_when_the_fork_lacks_the_strings()
    {
        Assert.Null(ColorNaming.Describe(Rgba.Parse("#FF0000"), new FluentStrings()));
        Assert.Null(ColorNaming.Describe(Rgba.Parse("#000000"), new FluentStrings()));

        // One message missing is enough: no name is better than a raw id.
        var some = new FluentStrings();
        some.Add("color-hue-chroma = {$chroma} {$hue}\ncolor-red = scarlet\n");
        Assert.Null(ColorNaming.Describe(Rgba.Parse("#FF0000"), some));
    }

    [Fact]
    public void The_bundled_engine_strings_name_a_colour_alone()
    {
        Assert.Equal("very light yellow", ColorNaming.Describe(Rgba.Parse("#F6E2A3"), new FluentStrings(), ColorNaming.Bundled));
        Assert.Equal("strong red", ColorNaming.Describe(Rgba.Parse("#FF0000"), new FluentStrings(), ColorNaming.Bundled));
    }

    [Fact]
    public void A_forks_own_string_wins_over_the_bundled_one()
    {
        var fork = new FluentStrings();
        fork.Add("color-yellow = custard\n");

        Assert.Equal("very light custard", ColorNaming.Describe(Rgba.Parse("#F6E2A3"), fork, ColorNaming.Bundled));
    }

    [Fact]
    public void A_forks_message_this_reader_cannot_fill_gives_way_to_the_bundled_one()
    {
        var fork = new FluentStrings();
        fork.Add("color-yellow = { $unknown }\ncolor-yellow-color-green = { NUMBER($x) }\n");

        Assert.Equal("very light yellow", ColorNaming.Describe(Rgba.Parse("#F6E2A3"), fork, ColorNaming.Bundled));
        // A pair name too: the bundled "yellowish green" rather than raw braces.
        Assert.Equal("strong yellowish green", ColorNaming.Describe(Rgba.Parse("#88AA00"), fork, ColorNaming.Bundled));
    }
}
