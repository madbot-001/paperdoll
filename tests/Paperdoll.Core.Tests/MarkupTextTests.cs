using Paperdoll.Core.Profiles;

namespace Paperdoll.Core.Tests;

public class MarkupTextTests
{
    [Theory]
    // As the game reads them: what it keeps, and where it refuses (-1: it does not) or stops.
    [InlineData("A [color=red]tall[/color] man.", "A tall man.", -1, -1)]
    [InlineData("[bold]x[/bold] [color=#FF0000]y[/color] [font size=12]z[/font] [br/]", "x y z ", -1, -1)]
    [InlineData("[head=\"Big\"]t[/head]", "t", -1, -1)]
    [InlineData("Frowns :[", "Frowns :", 8, -1)]
    [InlineData("note [1] here", "note ", 5, -1)]
    [InlineData("[ left cheek", "", 0, -1)]
    [InlineData("Waves \\o/", "Waves ", -1, 6)]
    [InlineData("a \\[b\\] c \\\\ d", "a [b] c \\ d", -1, -1)]
    public void Text_is_read_as_the_game_reads_it(string text, string kept, int refusedAt, int stoppedAt) =>
        Assert.Equal(new MarkupText.Reading(kept, refusedAt, stoppedAt), MarkupText.Read(text));

    [Theory]
    [InlineData("A [color=red]tall[/color] man.", "A tall man.")]
    [InlineData("Frowns :[", "Frowns :(")]
    [InlineData("note [1] here", "note (1) here")]
    [InlineData("Waves \\o/", "Waves o/")]
    [InlineData("a \\[b\\] c", "a (b) c")]
    [InlineData("Plain text.", "Plain text.")]
    public void Stable_text_is_what_the_game_keeps_however_often_it_checks(string text, string stable)
    {
        var result = MarkupText.Stable(text);

        Assert.Equal(stable, result);
        Assert.Equal(new MarkupText.Reading(result, -1, -1), MarkupText.Read(result));
    }

    [Theory]
    // A description made of stray brackets, or of tags whose quotes never close, was mended one
    // place at a time, reading from the start each time: hours for a file of them.
    [InlineData("[")]
    [InlineData("[a=\"\\\"")]
    [InlineData("\\x")]
    public void Text_made_of_many_things_to_mend_is_mended_quickly(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 100_000 / unit.Length));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkupText.Stable(text);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Assert.Equal(new MarkupText.Reading(result, -1, -1), MarkupText.Read(result));
    }
}
