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

    [Fact]
    public async Task A_goob_character_is_edited_and_exported_in_the_old_model()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPERDOLL_NETWORK_TESTS") == "1",
            "Set PAPERDOLL_NETWORK_TESTS=1 to run tests that use the network.");
        var ct = TestContext.Current.CancellationToken;
        await using var session = await EditorSession.OpenAsync(_root, ct);

        await session.LoadForkAsync(KnownForks.Find("goob")!, update: false, ct: ct);
        session.Edit(f => f.Name = "wren holloway");
        var species = session.Content!.Characters.Species["Human"];
        var hairOrgan = species.Organs.Single(o => o.Category == "Hair");
        var hairs = session.AvailableMarkings(hairOrgan, "Hair");
        session.AddMarking("Hair", "Hair", hairs[0].Id);
        session.SetMarkingColor("Hair", "Hair", 0, 0, Rgba.Parse("#654321"));
        // Hair takes one, so another swaps it, keeping the hair colour.
        session.AddMarking("Hair", "Hair", hairs[1].Id);
        Assert.Equal(hairs[1].Id, Assert.Single(session.Look!.Markings["Hair"]["Hair"]).Id);
        Assert.Equal(Rgba.Parse("#654321"), session.Look.Markings["Hair"]["Hair"][0].Colors[0]);

        // A category with points refuses more than it takes.
        var organ = species.Organs.First(o => session.LayerLimit(o, o.MarkingLayers[0]) is > 0 and < 5
            && o.Category is not ("Hair" or "FacialHair") && session.AvailableMarkings(o, o.MarkingLayers[0]).Count > 5);
        var layer = organ.MarkingLayers[0];
        var limit = session.LayerLimit(organ, layer)!.Value;
        var choices = session.AvailableMarkings(organ, layer);
        for (var i = 0; i < limit; i++)
            session.AddMarking(organ.Category, layer, choices[i].Id);
        Assert.Equal(limit, session.LimitCount(organ, layer));
        if (limit > 1)
            Assert.Throws<InvalidOperationException>(() => session.AddMarking(organ.Category, layer, choices[limit].Id));

        var text = session.Export();
        var exported = CharacterFile.Parse(text);
        Assert.True(exported.IsOldModel);
        Assert.Contains($"hair: {hairs[1].Id}", text);
        Assert.Contains("hairColor: '#654321FF'", text);
        Assert.Equal("Wren Holloway", exported.Name);
        // What Paperdoll exports needs no fixes and comes back the same.
        Assert.Empty(CharacterRules.EnsureValid(exported, session.Content.Characters, session.Fork!));
        Assert.Equal(text, exported.ToYaml());
        using var image = session.Render();
        Assert.True(image.Width >= 32);

        for (var seed = 0; seed < 20; seed++)
        {
            session.Randomize(Characters.RandomParts.All, new Random(seed));
            Assert.True(session.LastFixes.Count == 0, $"seed {seed}: " + string.Join("; ", session.LastFixes.Select(f => f.Message)));
        }
    }
}
