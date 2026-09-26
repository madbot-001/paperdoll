using System.Text;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public class RsiTests
{
    private const string Json = """{"version":1,"license":"CC-BY-SA-3.0","copyright":"someone","size":{"x":32,"y":64},"states":[{"name":"a","directions":4,"delays":[[0.1,0.1],[0.1,0.1],[0.1,0.1],[0.1,0.1]]},{"name":"b"}]}""";

    [Fact]
    public void Reads_size_licence_and_states()
    {
        var meta = RsiMeta.Parse(Encoding.UTF8.GetBytes(Json));

        Assert.Equal(32, meta.FrameWidth);
        Assert.Equal(64, meta.FrameHeight);
        Assert.Equal("CC-BY-SA-3.0", meta.License);
        Assert.Equal([2, 2, 2, 2], meta.States["a"].FramesPerDirection);
        Assert.Equal(1, meta.States["b"].Directions);
    }

    [Fact]
    public void Skips_a_byte_order_mark()
    {
        var meta = RsiMeta.Parse([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Json)]);

        Assert.Equal("someone", meta.Copyright);
    }

    [Fact]
    public void Animations_loop_through_their_frame_delays()
    {
        var meta = RsiMeta.Parse(Encoding.UTF8.GetBytes(
            """{"size":{"x":32,"y":32},"states":[{"name":"blink","delays":[[0.1,0.2,0.3]]},{"name":"still"}]}"""));
        var blink = meta.States["blink"];

        Assert.True(blink.IsAnimated(Direction.South));
        // A one-direction state plays the same frames whichever way the character faces.
        Assert.True(blink.IsAnimated(Direction.West));
        Assert.False(meta.States["still"].IsAnimated(Direction.South));
        Assert.Equal([0, 1, 2, 0], new[] { 0.05, 0.15, 0.35, 0.65 }.Select(t => blink.FrameAt(Direction.South, t)));
    }
}
