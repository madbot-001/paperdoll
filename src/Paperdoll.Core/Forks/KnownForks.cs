namespace Paperdoll.Core.Forks;

/// <summary>
/// The public, English-language forks surveyed on 2026-09-25 (docs/FORKS.md).
/// </summary>
public static class KnownForks
{
    public static IReadOnlyList<ForkInfo> All { get; } =
    [
        new("upstream", "Wizard's Den (upstream)", "space-wizards/space-station-14", "master", AppearanceModel.New, false, ["wizards", "wizards-testing"]),
        new("deltav", "Delta-V", "DeltaV-Station/Delta-v", "master", AppearanceModel.New, false, ["delta-v"]) { HiddenSpecies = ["Motorkind"], NameRule = Profiles.NameRule.AccentedLatin, SizeRule = SizeRule.SpeciesScaleTimesHeight, TraitRules = Traits.TraitRules.DeltaV },
        new("euphoria", "Euphoria", "Floof-Station/Panta-Rhei", "master", AppearanceModel.New, false, ["euphoria"]) { HiddenSpecies = ["Motorkind"], NameRule = Profiles.NameRule.AccentedLatin, SizeRule = SizeRule.SpeciesScaleTimesHeight, TraitRules = Traits.TraitRules.DeltaV },
        new("trauma", "Trauma", "Trauma-Station/Trauma-Station", "master", AppearanceModel.New, false, ["Trauma"]),
        new("carpmosia", "Carpmosia", "carpmosia/carpmosia", "dev", AppearanceModel.New, false, ["carpmosia"]),
        new("floof", "Floof", "Floof-Station-SS14/Floof-Station", "master", AppearanceModel.New, false, ["floof-ss14", "floof-station-nova"]) { TraitRules = Traits.TraitRules.DeltaV },
        new("forky", "Forky (Funky's successor)", "funky-station/forky-station", "master", AppearanceModel.New, false, []),
        new("starlight", "Starlight", "ss14Starlight/space-station-14", "starlight-dev", AppearanceModel.Old, false, ["starlight"]),
        new("sol", "Sol's Descendants", "North-Western-Development/space-station-14", "sol-dev", AppearanceModel.Old, false, ["sol"]),
        new("rmc", "RMC-14", "RMC-14/RMC-14", "master", AppearanceModel.Old, false, ["rmc14"]),
        new("goob", "Goob", "Goob-Station/Goob-Station", "master", AppearanceModel.Old, false, ["GoobLRP", "Goob-Station"]),
        new("omu", "Omu", "ProjectOmu/OmuStation", "master", AppearanceModel.Old, false, ["OmuStation"]),
        new("ratbite", "RatBite", "RatBite-Station-14/Rat_Bite_Station_14", "master", AppearanceModel.Old, false, ["Rat"]),
        new("funky", "Funky", "funky-station/funky-station", "master", AppearanceModel.Old, false, ["funkystation"]),
        new("misfits", "Misfits", "Misfit-Sanctuary/nuclear-14", "master", AppearanceModel.Old, false, ["master"]),
        new("frontier", "Frontier", "new-frontiers-14/frontier-station-14", "master", AppearanceModel.Old, false, ["Frontier"]),
        new("monolith", "Monolith", "Monolith-Station/Monolith", "main", AppearanceModel.Old, false, ["monolith"]),
        new("triad", "Triad", "Triad-Sector/Triad_Sector", "main", AppearanceModel.Old, false, ["Triad"]),
        new("lonestar", "Lone Star", "LoneStarSS14/lonestar-frontier", "master", AppearanceModel.Old, false, ["lonestar"]),
        new("wayfarer", "Wayfarer", "project-wayfarer/wayfarer-14", "master", AppearanceModel.Old, false, ["wayfarer14"]),
        new("impstation", "Impstation", "impstation/imp-station-14", "master", AppearanceModel.Old, false, []),
        new("ee", "Einstein Engines", "Simple-Station/Einstein-Engines", "master", AppearanceModel.Old, false, []),
    ];

    public static ForkInfo? Find(string id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The fork whose servers report this <c>fork_id</c>, if any.</summary>
    public static ForkInfo? FindByServerForkId(string forkId) =>
        All.FirstOrDefault(f => f.ServerForkIds.Contains(forkId, StringComparer.Ordinal));
}
