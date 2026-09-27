namespace Paperdoll.Core.Forks;

/// <summary>
/// Public, English-language forks, as of 2026-09-25.
/// </summary>
public static class KnownForks
{
    public static IReadOnlyList<ForkInfo> All { get; } =
    [
        new("upstream", "Wizard's Den (upstream)", "space-wizards/space-station-14", "master", AppearanceModel.New, false, ["wizards", "wizards-testing"]),
        new("deltav", "Delta-V", "DeltaV-Station/Delta-v", "master", AppearanceModel.New, false, ["delta-v"]) { HiddenSpecies = ["Motorkind"], NameRule = Profiles.NameRule.AccentedLatin, SizeRule = SizeRule.SpeciesScaleTimesHeight, TraitRules = Traits.TraitRules.DeltaV, Extras = ProfileExtras.Records },
        new("euphoria", "Euphoria", "Floof-Station/Panta-Rhei", "master", AppearanceModel.New, false, ["euphoria"]) { HiddenSpecies = ["Motorkind"], NameRule = Profiles.NameRule.AccentedLatin, SizeRule = SizeRule.SpeciesScaleTimesHeight, TraitRules = Traits.TraitRules.DeltaV,
            DefaultHeights = (0.7f, 1.25f), Extras = ProfileExtras.CustomSpeciesName | ProfileExtras.Records | ProfileExtras.Allergies | ProfileExtras.ItemCustomization,
            MaxFlavorTextLength = 1024 },
        new("trauma", "Trauma", "Trauma-Station/Trauma-Station", "master", AppearanceModel.New, false, ["Trauma"]) { FallbackJob = "DClass" },
        new("carpmosia", "Carpmosia", "carpmosia/carpmosia", "dev", AppearanceModel.New, false, ["carpmosia"]),
        new("floof", "Floof", "Floof-Station-SS14/Floof-Station", "master", AppearanceModel.New, false, ["floof-ss14", "floof-station-nova"]) { TraitRules = Traits.TraitRules.DeltaV },
        new("forky", "Forky (Funky's successor)", "funky-station/forky-station", "master", AppearanceModel.New, false, []),
        new("starlight", "Starlight", "ss14Starlight/space-station-14", "starlight-dev", AppearanceModel.Old, true, ["starlight"]) { FallbackJob = "Assistant" },
        new("sol", "Sol's Descendants", "North-Western-Development/space-station-14", "sol-dev", AppearanceModel.Old, true, ["sol"]) { FallbackJob = "Assistant" },
        new("rmc", "RMC-14", "RMC-14/RMC-14", "master", AppearanceModel.Old, false, ["rmc14"]),
        new("goob", "Goob", "Goob-Station/Goob-Station", "master", AppearanceModel.Old, true, ["GoobLRP", "Goob-Station"]) { SizeRule = SizeRule.HeightAndWidth },
        new("omu", "Omu", "ProjectOmu/OmuStation", "master", AppearanceModel.Old, true, ["OmuStation"]) { SizeRule = SizeRule.HeightAndWidth },
        new("ratbite", "RatBite", "RatBite-Station-14/Rat_Bite_Station_14", "master", AppearanceModel.Old, true, ["Rat"]) { SizeRule = SizeRule.HeightAndWidth },
        new("funky", "Funky", "funky-station/funky-station", "master", AppearanceModel.Old, true, ["funkystation"]),
        new("misfits", "Misfits", "Misfit-Sanctuary/nuclear-14", "master", AppearanceModel.Old, false, ["master"]),
        new("frontier", "Frontier", "new-frontiers-14/frontier-station-14", "master", AppearanceModel.Old, true, ["Frontier"]) { FallbackJob = "Contractor" },
        new("monolith", "Monolith", "Monolith-Station/Monolith", "main", AppearanceModel.Old, true, ["monolith"]) { FallbackJob = "Contractor" },
        new("triad", "Triad", "Triad-Sector/Triad_Sector", "main", AppearanceModel.Old, true, ["Triad"]) { FallbackJob = "Contractor", MaxFlavorTextLength = 2048 },
        new("lonestar", "Lone Star", "LoneStarSS14/lonestar-frontier", "master", AppearanceModel.Old, true, ["lonestar"]) { FallbackJob = "Contractor" },
        new("wayfarer", "Wayfarer", "project-wayfarer/wayfarer-14", "master", AppearanceModel.Old, true, ["wayfarer14"]) { FallbackJob = "Wayfarer", MaxFlavorTextLength = 2048 },
        new("impstation", "Impstation", "impstation/imp-station-14", "master", AppearanceModel.Old, true, []),
        new("ee", "Einstein Engines", "Simple-Station/Einstein-Engines", "master", AppearanceModel.Old, false, []),
    ];

    public static ForkInfo? Find(string id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The fork whose servers report this <c>fork_id</c>, if any.</summary>
    public static ForkInfo? FindByServerForkId(string forkId) =>
        All.FirstOrDefault(f => f.ServerForkIds.Contains(forkId, StringComparer.Ordinal));
}
