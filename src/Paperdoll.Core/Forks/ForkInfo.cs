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
/// <param name="Supported">
/// For forks on the old appearance model, whether Paperdoll has been checked against the fork and
/// can edit its characters. Forks on the new model can always be edited.
/// </param>
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

    /// <summary>The job players fall back to when they pick none (<c>SharedGameTicker.FallbackOverflowJob</c>).</summary>
    public string FallbackJob { get; init; } = "Passenger";

    /// <summary>
    /// The longest description the game keeps (<c>ic.flavor_text_length</c>, or older forks'
    /// <c>MaxDescLength</c>): 512 upstream, 1024 in Euphoria, 2048 in Wayfarer and Triad.
    /// </summary>
    public int MaxFlavorTextLength { get; init; } = 512;
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

    /// <summary>
    /// Einstein Engines' height and width, as Goob and its forks have them: drawn at the
    /// character's <c>width</c> across and <c>height</c> up, each clamped to the species' range.
    /// The lobby also keeps the two within the species' <c>sizeRatio</c> of each other and shows
    /// centimetres and weight.
    /// </summary>
    HeightAndWidth,
}
