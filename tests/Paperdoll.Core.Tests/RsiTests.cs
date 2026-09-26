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
}
