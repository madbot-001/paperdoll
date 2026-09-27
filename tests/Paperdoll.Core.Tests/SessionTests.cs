using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Tests;

/// <summary>Sessions over the made-up forks in <see cref="TestForks"/>.</summary>
public class SessionTests
{
    // A lizard with a scar, made in fork A.
    private static async Task<EditorSession> LizardInA()
    {
        var session = TestForks.Session();
        await session.LoadForkAsync(TestForks.A, update: false, ct: TestContext.Current.CancellationToken);
        session.Edit(f => f.Name = "Kept Person");
        session.ChangeSpecies("Lizard");
        session.AddMarking("Torso", "Chest", "Scar");
        return session;
    }

    [Fact]
    public async Task Switching_forks_and_back_restores_what_the_other_fork_lacked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = await LizardInA();

        await session.LoadForkAsync(TestForks.B, update: false, ct: ct);
        Assert.Equal("Human", session.Look!.Species);
        Assert.True(session.ChangedBySwitch);
        Assert.Contains("Lizard", session.WorkingCopy());

        await session.LoadForkAsync(TestForks.A, update: false, ct: ct);
        Assert.Equal("Lizard", session.Look!.Species);
        Assert.Equal("Scar", Assert.Single(session.Look.Markings["Torso"]["Chest"]).Id);
        Assert.False(session.ChangedBySwitch);
    }

    [Fact]
    public async Task An_edit_after_a_switch_keeps_what_the_switch_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = await LizardInA();

        await session.LoadForkAsync(TestForks.B, update: false, ct: ct);
        session.Edit(f => f.Name = "Renamed Person");
        await session.LoadForkAsync(TestForks.A, update: false, ct: ct);

        Assert.Equal("Renamed Person", session.File!.Name);
        Assert.Equal("Human", session.Look!.Species);
    }

    [Fact]
    public async Task A_fork_that_fails_to_load_leaves_the_session_as_it_was()
    {
        await using var session = await LizardInA();
        // A fork with nothing in it: no species at all, so no character can be fitted to it.
        var empty = new ForkInfo("c", "Fork C", "o/c", "main", AppearanceModel.New, false, ["c"]);

        await Assert.ThrowsAnyAsync<Exception>(() => session.LoadForkAsync(empty, update: false, ct: TestContext.Current.CancellationToken));

        Assert.Equal("a", session.Fork!.Id);
        Assert.Equal("Lizard", session.Look!.Species);
        Assert.Equal("Scar", Assert.Single(session.Look.Markings["Torso"]["Chest"]).Id);
        // Never loaded, so it is not left looking downloaded.
        Assert.Null(await session.Store.CommitOfAsync("c", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saving_after_a_switch_keeps_what_the_switch_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = await LizardInA();

        await session.LoadForkAsync(TestForks.B, update: false, ct: ct);
        session.KeepSwitchChanges();
        await session.LoadForkAsync(TestForks.A, update: false, ct: ct);

        Assert.Equal("Human", session.Look!.Species);
    }

    [Fact]
    public async Task A_file_that_fails_to_open_leaves_the_open_character_alone()
    {
        await using var session = await LizardInA();
        var bad = CharacterFile.Parse(session.Export());
        bad.Name = "Other Person";
        // A key the game's files never have: a list where a job id belongs.
        bad.Profile.Children[new YamlScalarNode("_jobPriorities")] = new YamlMappingNode { { new YamlSequenceNode(new YamlScalarNode("a")), new YamlScalarNode("High") } };

        Assert.ThrowsAny<Exception>(() => session.Open(bad.ToYaml()));

        Assert.Equal("Kept Person", session.File!.Name);
        Assert.Equal("Lizard", session.Look!.Species);
    }
}
