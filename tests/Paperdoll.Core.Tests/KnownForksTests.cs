using Paperdoll.Core.Forks;

namespace Paperdoll.Core.Tests;

public class KnownForksTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var ids = KnownForks.All.Select(f => f.Id.ToLowerInvariant()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Server_fork_ids_belong_to_one_fork_each()
    {
        var ids = KnownForks.All.SelectMany(f => f.ServerForkIds).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("deltav", "DeltaV-Station/Delta-v")]
    [InlineData("upstream", "space-wizards/space-station-14")]
    public void The_first_targets_are_known(string id, string repository)
    {
        Assert.Equal(repository, KnownForks.Find(id)?.Repository);
    }

    [Theory]
    [InlineData("euphoria", "euphoria")]
    [InlineData("floof-station-nova", "floof")]
    [InlineData("delta-v", "deltav")]
    [InlineData("wizards-testing", "upstream")]
    [InlineData("GoobLRP", "goob")]
    public void Export_files_map_to_forks_by_fork_id(string forkId, string expected)
    {
        Assert.Equal(expected, KnownForks.FindByServerForkId(forkId)?.Id);
    }
}
