using System.Text.RegularExpressions;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// Which characters a fork allows in names (with the game's default <c>ic.restricted_names</c>
/// on); others are removed.
/// </summary>
public sealed class NameRule(string description, Regex disallowed)
{
    public string Description { get; } = description;
    private Regex Disallowed { get; } = disallowed;

    /// <summary>Upstream: plain letters, digits, spaces, apostrophes and hyphens.</summary>
    public static readonly NameRule Upstream = new(
        "A-Z, a-z, 0-9, space, ' and -",
        new Regex(@"[^A-Za-z0-9 '\-]"));

    /// <summary>
    /// Delta-V and forks like it: also accented Latin letters (Latin-1 and Latin Extended-A),
    /// full stops and commas.
    /// </summary>
    public static readonly NameRule AccentedLatin = new(
        "A-Z, a-z, 0-9, accented Latin letters, space, ', ., , and -",
        new Regex(@"[^0-9A-Za-zÀ-ÖØ-öø-ÿĀ-ſ '.,\-]"));

    public string RemoveDisallowed(string name) => Disallowed.Replace(name, string.Empty);
}
