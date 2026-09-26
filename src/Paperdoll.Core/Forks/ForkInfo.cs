namespace Paperdoll.Core.Forks;

/// <summary>How a fork stores a character's appearance. See docs/FORKS.md.</summary>
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
}
