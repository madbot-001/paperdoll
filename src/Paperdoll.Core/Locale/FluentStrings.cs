using System.Text;
using System.Text.RegularExpressions;

namespace Paperdoll.Core.Locale;

/// <summary>
/// Message texts from the game's Fluent (<c>.ftl</c>) locale files, by message id. Only what
/// Paperdoll needs: plain messages, including values continued on indented lines. Terms
/// (<c>-name</c>), attributes (<c>.name</c>) and comments are skipped; placeables such as
/// <c>{ $count }</c> are kept as written.
/// </summary>
public sealed partial class FluentStrings
{
    private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal);

    public int Count => _messages.Count;

    public string? this[string id] => _messages.GetValueOrDefault(id);

    /// <summary>The message's text, or the id itself when the fork has no such message.</summary>
    public string Get(string id) => _messages.GetValueOrDefault(id) ?? id;

    public void Add(string text)
    {
        string? id = null;
        var value = new StringBuilder();

        // Many of the forks' files start with a byte order mark, which would hide the first message.
        foreach (var raw in text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length > 0 && !char.IsWhiteSpace(raw[0]))
            {
                Finish();
                var match = MessageStart().Match(line);
                if (match.Success)
                {
                    id = match.Groups[1].Value;
                    value.Append(match.Groups[2].Value.Trim());
                }
            }
            else if (id != null && line.Length > 0)
            {
                var content = line.Trim();
                if (content.StartsWith('.'))
                {
                    // An attribute: the message's own value, if any, has ended.
                    Finish();
                    continue;
                }
                if (value.Length > 0)
                    value.Append('\n');
                value.Append(content);
            }
        }
        Finish();

        void Finish()
        {
            if (id != null && value.Length > 0)
                _messages[id] = value.ToString();
            id = null;
            value.Clear();
        }
    }

    // "message-id = value"; terms start with "-" and do not match.
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9_-]*)\s*=(.*)$")]
    private static partial Regex MessageStart();
}
