using Paperdoll.Core.Editing;

namespace Paperdoll.Core.Tests;

/// <summary>Trying clothing on in the preview, on made-up fork "w".</summary>
public class WardrobeTests
{
    private static async Task<(EditorSession Session, MemoryForkStore Store)> Dressed()
    {
        var store = TestForks.Store();
        var session = new EditorSession(store) { DressUpShown = true };
        await session.LoadForkAsync(TestForks.W, update: false, ct: TestContext.Current.CancellationToken);
        return (session, store);
    }

    [Fact]
    public async Task With_dress_up_not_shown_the_job_outfit_is_worn_and_what_was_tried_on_is_kept()
    {
        var (session, _) = await Dressed();
        await using var _ = session;
        session.NewCharacter("Human");
        session.WearNothing("head");
        Assert.False(session.Outfit()!.ContainsKey("head"));

        session.DressUpShown = false;

        Assert.Equal(session.JobOutfit(), session.Outfit());
        Assert.True(session.HasDressUp);
        Assert.False(new EditorSession(TestForks.Store()).DressUpShown);
    }

    [Fact]
    public async Task The_wardrobe_holds_each_wearable_item_under_every_slot_it_fits()
    {
        var (session, _) = await Dressed();
        await using var _ = session;
        var wardrobe = session.Content!.Wardrobe;

        Assert.Equal(["ClothingHeadCap", "ClothingHeadPaper", "ClothingNeckScarf"], wardrobe.Items("head"));
        Assert.Equal(["ClothingNeckScarf"], wardrobe.Items("neck"));
        Assert.Equal(["ClothingBackpackTall"], wardrobe.Items("back"));
        Assert.Equal(["ClothingUniformClerk", "ClothingUniformGrey"], wardrobe.Items("jumpsuit"));
        Assert.Equal(["ClothingEyesGlasses"], wardrobe.Items("eyes"));
        // Not the abstract hat, nor the wrench, which is not clothing.
        Assert.DoesNotContain("ClothingHeadBase", wardrobe.Entities());
        Assert.DoesNotContain("ToolWrench", wardrobe.Entities());
    }

    [Fact]
    public async Task Only_slots_the_body_draws_are_offered_with_items_by_name()
    {
        var (session, _) = await Dressed();
        await using var _ = session;

        // The body has no layer for glasses.
        Assert.Equal(["head", "jumpsuit", "neck", "back"], session.WardrobeSlots());
        Assert.Equal(["cap", "long scarf", "paper hat"], session.Wearables("head").Select(i => i.Name));
    }

    [Fact]
    public async Task The_fork_loads_without_the_clothing_it_only_lists()
    {
        var (session, store) = await Dressed();
        await using var _ = session;

        Assert.Empty(store.Fetches);
        Assert.Contains("Clothing/Head/cap.rsi/meta.json", session.Content!.TextureFiles.Keys);
        Assert.Equal(
            ["Clothing/Back/tall.rsi/meta.json", "Clothing/Eyes/glasses.rsi/meta.json", "Clothing/Head/paper.rsi/meta.json",
             "Clothing/Neck/scarf.rsi/meta.json", "Clothing/Uniforms/grey.rsi/meta.json"],
            session.Content.WardrobeFiles.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Trying_an_item_on_downloads_its_pictures_once_and_draws_and_credits_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, store) = await Dressed();
        await using var _ = session;
        Assert.False(session.Draws("ClothingNeckScarf", "neck"));

        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);

        Assert.Equal(["Resources/Textures/Clothing/Neck/scarf.rsi/meta.json"], Assert.Single(store.Fetches));
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["neck"]);
        Assert.True(session.Draws("ClothingNeckScarf", "neck"));
        Assert.Contains(session.Credits(), c => c.Rsi == "Clothing/Neck/scarf.rsi" && c.License == "CC-BY-SA-3.0");
        Assert.True(session.HasDressUp);

        // The same item on the head needs nothing more.
        Assert.Empty(session.MissingSprites("ClothingNeckScarf"));
        await session.WearAsync("head", "ClothingNeckScarf", session.DressUpCleared, ct);
        Assert.Single(store.Fetches);
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["head"]);
    }

    [Fact]
    public async Task A_download_that_fails_leaves_the_slot_as_it_was()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, store) = await Dressed();
        await using var _ = session;
        store.FetchFails = new HttpRequestException("No network.");

        await Assert.ThrowsAsync<HttpRequestException>(() => session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, ct));

        Assert.Equal("ClothingHeadCap", session.Outfit()!["head"]);
        Assert.False(session.HasDressUp);
        Assert.Empty(store.Fetches);

        store.FetchFails = null;
        await session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, ct);
        Assert.Equal("ClothingHeadPaper", session.Outfit()!["head"]);
        // A hat with no picture for the head draws nothing there.
        Assert.False(session.Draws("ClothingHeadPaper", "head"));
    }

    [Fact]
    public async Task Nothing_from_the_job_and_reset_change_only_the_preview()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await Dressed();
        await using var _ = session;
        var before = session.Export();

        session.WearNothing("head");
        await session.WearAsync("back", "ClothingBackpackTall", session.DressUpCleared, ct);
        Assert.False(session.Outfit()!.ContainsKey("head"));
        Assert.Equal("ClothingBackpackTall", session.Outfit()!["back"]);
        Assert.Equal("ClothingHeadCap", session.JobOutfit()["head"]);
        Assert.False(session.JobOutfit().ContainsKey("back"));

        session.WearFromJob("head");
        Assert.Equal("ClothingHeadCap", session.Outfit()!["head"]);
        session.ResetDressUp();
        Assert.False(session.HasDressUp);
        Assert.Equal(session.JobOutfit(), session.Outfit());

        Assert.Equal(before, session.Export());
    }

    [Fact]
    public async Task The_file_and_the_loadout_rules_ignore_dress_up()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await Dressed();
        await using var _ = session;
        var file = session.File!.ToYaml();

        await session.WearAsync("jumpsuit", "ClothingUniformGrey", session.DressUpCleared, ct);
        session.WearNothing("head");
        session.ShowClothes = false;
        Assert.Null(session.Outfit());
        session.ShowClothes = true;

        Assert.Equal(file, session.File!.ToYaml());
        Assert.False(session.ChangedBySwitch);
        var gear = session.GearAtSpawn()!;
        Assert.Equal("ClothingUniformClerk", gear.Worn["jumpsuit"].Entity);
        Assert.Equal("ClothingHeadCap", gear.Worn["head"].Entity);
        Assert.Equal("Clerk", session.TraitContext().Job);
    }

    [Fact]
    public async Task Dress_up_stays_through_edits_species_and_jobs()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await Dressed();
        await using var _ = session;
        await session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);

        session.Edit(f => f.Name = "Wren Holloway");
        session.ChangeSpecies("Vox");
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["neck"]);

        // A job with no gear still wears what was tried on.
        session.PreviewJob = "Porter";
        Assert.Equal(new Dictionary<string, string> { ["neck"] = "ClothingNeckScarf" }, session.Outfit());
        session.PreviewJob = "Clerk";
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["neck"]);
    }

    [Fact]
    public async Task A_Vox_wears_the_Vox_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await Dressed();
        await using var _ = session;
        session.ChangeSpecies("Vox");

        await session.WearAsync("jumpsuit", "ClothingUniformGrey", session.DressUpCleared, ct);

        var layer = session.Renderer!.Layers(session.Look!, session.Outfit()).Single(l => l.Key == "jumpsuit-0");
        Assert.Equal("equipped-INNERCLOTHING-vox", layer.Sprite.State);
    }

    [Fact]
    public async Task Opening_a_file_a_new_or_random_character_or_a_fork_clears_dress_up()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await Dressed();
        await using var _ = session;
        var text = session.Export();

        foreach (var clear in new Func<Task>[]
        {
            () => Task.FromResult(session.Open(text)),
            () =>
            {
                session.NewCharacter("Human");
                return Task.CompletedTask;
            },
            () => Task.FromResult(session.Randomize(Characters.RandomParts.Name, new Random(1))),
            () => session.LoadForkAsync(TestForks.W, update: false, ct: ct),
        })
        {
            session.WearNothing("head");
            Assert.True(session.HasDressUp);
            await clear();
            Assert.False(session.HasDressUp);
            Assert.Equal("ClothingHeadCap", session.Outfit()!["head"]);
        }
    }

    [Fact]
    public async Task A_later_pick_in_the_slot_wins_over_a_download_that_ends_late()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, store) = await Dressed();
        await using var _ = session;
        var hold = store.FetchHold = new TaskCompletionSource();

        var hat = session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, ct);
        session.WearNothing("head");
        hold.SetResult();
        Assert.Equal(WearResult.Dropped, await hat);

        Assert.False(session.Outfit()!.ContainsKey("head"));
    }

    [Fact]
    public async Task A_cancelled_pick_that_ends_late_is_not_put_on()
    {
        var (session, store) = await Dressed();
        await using var _ = session;
        var hold = store.FetchHold = new TaskCompletionSource();
        using var cancel = new CancellationTokenSource();

        var hat = session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, cancel.Token);
        await cancel.CancelAsync();
        hold.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hat);
        Assert.Equal("ClothingHeadCap", session.Outfit()!["head"]);
    }

    [Fact]
    public async Task Downloads_in_different_slots_each_finish()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, store) = await Dressed();
        await using var _ = session;
        var hold = store.FetchHold = new TaskCompletionSource();

        var hat = session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, ct);
        var scarf = session.WearAsync("neck", "ClothingNeckScarf", session.DressUpCleared, ct);
        hold.SetResult();
        await Task.WhenAll(hat, scarf);

        Assert.Equal("ClothingHeadPaper", session.Outfit()!["head"]);
        Assert.Equal("ClothingNeckScarf", session.Outfit()!["neck"]);
        Assert.Equal(2, store.Fetches.Count);
        Assert.False(store.Overlapped);
    }

    [Fact]
    public async Task Store_work_stops_a_wardrobe_download_and_waits_for_it_to_end()
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var work in new Func<EditorSession, Task>[]
        {
            session => session.LoadForkAsync(TestForks.W, update: true, ct: ct),
            session => session.RemoveForkAsync(TestForks.A, ct),
            session => session.CleanUpStoreAsync(ct),
        })
        {
            var (session, store) = await Dressed();
            await using var _ = session;
            var hold = store.FetchHold = new TaskCompletionSource();
            var hat = session.WearAsync("head", "ClothingHeadPaper", session.DressUpCleared, ct);

            var started = work(session);
            await Task.Delay(50, ct);
            Assert.False(started.IsCompleted);

            hold.SetResult();
            await started;
            Assert.Equal(WearResult.Stopped, await hat);
            Assert.False(store.Overlapped);
        }
    }

    [Fact]
    public async Task A_pick_made_before_dress_up_was_cleared_is_dropped()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, store) = await Dressed();
        await using var _ = session;
        var round = session.DressUpCleared;
        var token = session.DressUpToken;

        session.Randomize(Characters.RandomParts.Name, new Random(1));

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(WearResult.Dropped, await session.WearAsync("head", "ClothingHeadPaper", round, ct));
        Assert.False(session.HasDressUp);
        Assert.Empty(store.Fetches);
    }
}
