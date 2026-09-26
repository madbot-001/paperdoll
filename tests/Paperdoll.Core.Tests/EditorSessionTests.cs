using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Tests;

public sealed class EditorSessionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("paperdoll-session-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_delta_v_character_can_be_made_edited_and_exported()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        await using var session = await EditorSession.OpenAsync(_root, ct);

        await session.LoadForkAsync(KnownForks.Find("deltav")!, update: false, ct: ct);
        session.Edit(f => f.Name = "urist mchands");
        session.ChangeSpecies("Harpy");
        var head = session.Content!.Characters.Species["Harpy"].Organs.First(o => o.MarkingLayers.Contains("Hair"));
        var hair = session.AvailableMarkings(head, "Hair").First();
        session.AddMarking(head.Category, "Hair", hair.Id);
        // Hair takes one marking, so picking another swaps it.
        var otherHair = session.AvailableMarkings(head, "Hair").First(m => m.Id != hair.Id);
        session.AddMarking(head.Category, "Hair", otherHair.Id);
        Assert.Equal(otherHair.Id, Assert.Single(session.Look!.Markings[head.Category]["Hair"]).Id);
        session.AddMarking(head.Category, "Hair", hair.Id);
        session.SetMarkingColor(head.Category, "Hair", 0, 0, Rgba.Parse("#123456"));

        var exported = CharacterFile.Parse(session.Export());
        var look = exported.ReadLook(session.Content.Characters);

        Assert.Equal("Urist Mchands", exported.Name);
        Assert.Equal("Harpy", exported.Species);
        Assert.Equal("delta-v", exported.ForkId);
        Assert.Equal(hair.Id, look.Markings[head.Category]["Hair"][0].Id);
        Assert.Equal(Rgba.Parse("#123456"), look.Markings[head.Category]["Hair"][0].Colors[0]);
        Assert.NotEmpty(session.Credits());
        using var image = session.Render();
        Assert.True(image.Width >= 32);

        // Random characters already pass the game's rules; only required-marking fixes are expected.
        for (var seed = 0; seed < 30; seed++)
        {
            session.Randomize(Characters.RandomParts.All, new Random(seed));
            var problems = session.LastFixes.Where(f => f.Field != "markings" || f.Message.Contains("removed")).ToList();
            Assert.True(problems.Count == 0, $"seed {seed}, {session.Look!.Species}, name '{session.File!.Name}': " + string.Join("; ", problems.Select(p => p.Message)));
            Assert.Empty(Profiles.CharacterRules.EnsureValid(Profiles.CharacterFile.Parse(session.File!.ToYaml()), session.Content.Characters, session.Fork!));
        }
        session.Edit(f => f.Name = "urist mchands");
        session.ChangeSpecies("Harpy");

        // Loading the fork again (as Update does) keeps the character open.
        await session.LoadForkAsync(KnownForks.Find("deltav")!, update: false, ct: ct);
        Assert.Equal("Urist Mchands", session.File!.Name);
        Assert.Equal("Harpy", session.Look!.Species);
    }
}
