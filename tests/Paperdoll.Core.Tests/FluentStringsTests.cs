using Paperdoll.Core.Locale;

namespace Paperdoll.Core.Tests;

public class FluentStringsTests
{
    [Fact]
    public void Reads_messages_and_skips_comments_terms_and_attributes()
    {
        var strings = new FluentStrings();
        strings.Add("""
            # A comment
            species-name-human = Human
            -brand = Nanotrasen
            marking-Scar = Scar
                .desc = A scar.
            species-name-vox=Vox
            """);

        Assert.Equal("Human", strings["species-name-human"]);
        Assert.Equal("Scar", strings["marking-Scar"]);
        Assert.Equal("Vox", strings["species-name-vox"]);
        Assert.Null(strings["-brand"]);
        Assert.Equal(3, strings.Count);
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_the_first_message()
    {
        var strings = new FluentStrings();
        strings.Add(System.Text.Encoding.UTF8.GetString([0xEF, 0xBB, 0xBF, .. "trait-name-EggLayer = Egg layer\n"u8]));

        Assert.Equal("Egg layer", strings["trait-name-EggLayer"]);
    }

    [Fact]
    public void Joins_values_continued_on_indented_lines()
    {
        var strings = new FluentStrings();
        strings.Add("guide =\n    First line\n    second line\nnext = Next\n");

        Assert.Equal("First line\nsecond line", strings["guide"]);
        Assert.Equal("Next", strings["next"]);
    }

    [Fact]
    public void Falls_back_to_the_id_for_a_missing_message()
    {
        Assert.Equal("species-name-nobody", new FluentStrings().Get("species-name-nobody"));
    }
}
