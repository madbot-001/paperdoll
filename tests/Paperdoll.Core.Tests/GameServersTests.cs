using System.Text.Json;
using Paperdoll.Core.Servers;

namespace Paperdoll.Core.Tests;

public class GameServersTests
{
    [Theory]
    [InlineData("ss14://play.example.org", "http://play.example.org:1212/info")]
    [InlineData("ss14://play.example.org:1300/", "http://play.example.org:1300/info")]
    [InlineData("ss14s://play.example.org/server-2/", "https://play.example.org/server-2/info")]
    [InlineData("ss14s://play.example.org:4430", "https://play.example.org:4430/info")]
    [InlineData("192.0.2.5", "http://192.0.2.5:1212/info")]
    [InlineData("192.0.2.5:1215", "http://192.0.2.5:1215/info")]
    public void Addresses_turn_into_info_pages_as_the_launcher_reads_them(string address, string expected)
    {
        Assert.Equal(new Uri(expected), GameServers.InfoUri(address));
    }

    [Fact]
    public void Other_schemes_are_refused()
    {
        Assert.Throws<FormatException>(() => GameServers.InfoUri("https://play.example.org"));
    }

    [Fact]
    public void A_servers_build_says_its_fork_and_commit()
    {
        using var doc = JsonDocument.Parse("""
            {"connect_address":"","build":{"engine_version":"260.0.0","fork_id":"delta-v",
             "version":"b712672c5f8734c2f0b5fd2c0e2a1a7f7c7e1b2a","download_url":"https://example.org/client.zip"}}
            """);

        var build = GameServers.ReadBuild(doc.RootElement);

        Assert.Equal("delta-v", build.ForkId);
        Assert.True(build.IsCommit);
        Assert.False(new ServerBuild("x", "EF7FF844F6B46B1592AECCD59A47E414317CA20262A158C0A1780481FD0E2AC8", null).IsCommit);
        Assert.False(GameServers.ReadBuild(JsonDocument.Parse("{}").RootElement).IsCommit);
    }

    [Fact]
    public void The_hub_list_is_read_busiest_first()
    {
        using var doc = JsonDocument.Parse("""
            [{"address":"ss14://quiet.example.org","statusData":{"name":"Quiet","players":2,"soft_max_players":40}},
             {"address":"ss14s://busy.example.org","statusData":{"name":"Busy","players":80}},
             {"address":"ss14://broken.example.org","statusData":null}]
            """);

        var servers = GameServers.ReadHub(doc.RootElement);

        Assert.Equal(["Busy", "Quiet"], servers.Select(s => s.Name));
        Assert.Equal(40, servers[1].MaxPlayers);
    }
}
