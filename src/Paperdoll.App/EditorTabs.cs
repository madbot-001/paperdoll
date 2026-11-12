using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;

namespace Paperdoll.App;

/// <summary>The tabs of the editor, in the order of the lobby's character screen.</summary>
public enum EditorTab { Appearance, Jobs, Antags, Traits, Markings, Loadout, Allergies, Records, Description, Credits, Messages, Source }

/// <summary>Which tabs a loaded fork shows, and how each is laid out.</summary>
public static class EditorTabs
{
    public static string Title(EditorTab tab) => tab.ToString();

    /// <summary>Tabs that are not about the character: the credits, the messages and the source.</summary>
    public static bool IsUtility(EditorTab tab) => tab is EditorTab.Credits or EditorTab.Messages or EditorTab.Source;

    /// <summary>Tabs with many items, which get a list on the left.</summary>
    public static bool HasSideList(EditorTab tab) =>
        tab is EditorTab.Traits or EditorTab.Markings or EditorTab.Loadout or EditorTab.Records;

    /// <summary>The tabs the session's fork has, in strip order. The session must have a fork loaded.</summary>
    public static IReadOnlyList<EditorTab> Visible(EditorSession session)
    {
        var fork = session.Fork ?? throw new InvalidOperationException("No fork is loaded.");
        var content = session.Content ?? throw new InvalidOperationException("No fork is loaded.");
        return Enum.GetValues<EditorTab>().Where(tab => Shows(session, fork, content, tab)).ToList();
    }

    private static bool Shows(EditorSession session, ForkInfo fork, ForkContent content, EditorTab tab) => tab switch
    {
        EditorTab.Antags => content.Outfits.Antags.Values.Any(a => a.SetPreference),
        EditorTab.Traits => content.Traits.Traits.Count > 0,
        EditorTab.Loadout => session.DressedJob() != null,
        EditorTab.Allergies => fork.Extras.HasFlag(ProfileExtras.Allergies),
        EditorTab.Records => fork.Extras.HasFlag(ProfileExtras.Records),
        _ => true,
    };
}
