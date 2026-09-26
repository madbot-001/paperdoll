namespace Paperdoll.Core.Forks;

/// <summary>How a fork stores a character's appearance.</summary>
public enum AppearanceModel
{
    /// <summary>Hair and facial hair in their own fields, markings in one flat list.</summary>
    Old,

    /// <summary>Markings grouped by body organ, then by layer (upstream since 2026-01-20).</summary>
    New,
}

/// <summary>A fork Paperdoll knows how to find.</summary>
/// <param name="Id">Paperdoll's own name for the fork.</param>
/// <param name="Name">Display name.</param>
/// <param name="Repository">GitHub <c>owner/name</c>.</param>
/// <param name="Branch">Branch the fork's servers are built from.</param>
/// <param name="Model">Appearance model at the time of the survey.</param>
/// <param name="Supported">Whether Paperdoll can edit this fork's characters yet.</param>
/// <param name="ServerForkIds">
/// Values of <c>build.fork_id</c> that this fork's servers report on their <c>/info</c> page.
/// </param>
public sealed record ForkInfo(
    string Id,
    string Name,
    string Repository,
    string Branch,
    AppearanceModel Model,
    bool Supported,
    IReadOnlyList<string> ServerForkIds)
{
    /// <summary>
    /// Round-start species the fork hides by default through a server setting (Delta-V's
    /// <c>species.hidden</c>), so <c>roundStart: true</c> alone would list them wrongly.
    /// </summary>
    public IReadOnlyList<string> HiddenSpecies { get; init; } = [];

    /// <summary>Which characters names may contain.</summary>
    public Profiles.NameRule NameRule { get; init; } = Profiles.NameRule.Upstream;

    public SizeRule SizeRule { get; init; } = SizeRule.None;

    /// <summary>Which trait system the fork has, and its overall limits.</summary>
    public Traits.TraitRules TraitRules { get; init; } = Traits.TraitRules.Upstream;

    /// <summary>
    /// The height range of species that name none (<c>SpeciesPrototype.MinHeight</c> and
    /// <c>MaxHeight</c>): 0.8 to 1.2 in Delta-V, 0.7 to 1.25 in Euphoria.
    /// </summary>
    public (float Min, float Max) DefaultHeights { get; init; } = (0.8f, 1.2f);

    public ProfileExtras Extras { get; init; } = ProfileExtras.None;
}

/// <summary>Character fields some forks add to the saved profile.</summary>
[Flags]
public enum ProfileExtras
{
    None = 0,

    /// <summary>Euphoria's <c>customspeciename</c>, shown instead of the species' name.</summary>
    CustomSpeciesName = 1,

    /// <summary>Character records from Cosmatic Drift (<c>cosmaticDriftCharacterRecords</c>), in Delta-V and Euphoria.</summary>
    Records = 2,

    /// <summary>Allergies to reagents (<c>cosmaticDriftAllergies</c>), in Euphoria.</summary>
    Allergies = 4,

    /// <summary>A name, description and colour for each chosen loadout item (Floof Station's, in Euphoria).</summary>
    ItemCustomization = 8,
}

/// <summary>How a fork sizes characters on screen.</summary>
public enum SizeRule
{
    /// <summary>Everyone is drawn at the sprite's own size (upstream).</summary>
    None,

    /// <summary>
    /// Delta-V and forks built on it: drawn at the species' <c>baseScale</c> times the character's
    /// height (<c>cosmaticDriftCharacterHeight</c>), which is clamped to the species'
    /// <c>minHeight</c> to <c>maxHeight</c> and rounded to two decimals.
    /// </summary>
    SpeciesScaleTimesHeight,
}
