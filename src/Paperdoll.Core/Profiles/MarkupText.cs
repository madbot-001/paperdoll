// The reading rules follow RobustToolbox's markup parser
// (Robust.Shared/Utility/FormattedMessage.MarkupParser.cs), Copyright (c) 2019 Space Station 14
// Contributors, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Text;

namespace Paperdoll.Core.Profiles;

/// <summary>
/// Text as the game reads it where it removes markup (<c>FormattedMessage.RemoveMarkupOrThrow</c>,
/// for descriptions and Euphoria's custom species names). Tags such as <c>[color=red]</c> go,
/// escapes such as <c>\[</c> give their character, a <c>[</c> that starts no tag makes the game
/// refuse the text (and so the whole character), and a backslash before anything but
/// <c>\ [ ] /</c> makes it stop reading there, dropping the rest.
/// </summary>
public static class MarkupText
{
    /// <summary>What the game keeps of a text, and where it refused it or stopped reading (-1 for neither).</summary>
    public readonly record struct Reading(string Kept, int RefusedAt, int StoppedAt);

    public static Reading Read(string text)
    {
        var kept = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '\\' when i + 1 < text.Length && text[i + 1] is '\\' or '[' or ']' or '/':
                    kept.Append(text[i + 1]);
                    i += 2;
                    break;
                case '\\':
                    return new Reading(kept.ToString(), -1, i);
                case '[':
                    var end = TagEnd(text, i + 1);
                    if (end < 0)
                        return new Reading(kept.ToString(), i, -1);
                    i = end;
                    break;
                default:
                    kept.Append(text[i++]);
                    break;
            }
        }
        return new Reading(kept.ToString(), -1, -1);
    }

    /// <summary>
    /// The text as the game keeps it, however often it checks it again: markup removed, a stray
    /// <c>[</c> turned into <c>(</c> (and the <c>]</c> closing it into <c>)</c>) so the game does not
    /// refuse it, and backslashes the game would stop at dropped.
    /// </summary>
    /// <remarks>
    /// The game saves what it kept, and reads that again next time: an escaped <c>\[</c> it kept as
    /// <c>[</c> would then be refused, so this goes on until nothing changes.
    /// </remarks>
    public static string Stable(string text)
    {
        text = EscapedBrackets(text);
        while (true)
        {
            var reading = Read(text);
            if (reading.RefusedAt >= 0)
                text = Bracket(text, reading.RefusedAt);
            else if (reading.StoppedAt >= 0)
                text = text.Remove(reading.StoppedAt, 1);
            else if (reading.Kept == text)
                return text;
            else
                text = reading.Kept;
        }
    }

    // Brackets escaped to be shown as brackets cannot stay so: the game keeps them bare, and next
    // time reads them as a tag or refuses them. They become parentheses, which it keeps.
    private static string EscapedBrackets(string text)
    {
        if (!text.Contains('\\'))
            return text;
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '[' or ']' or '\\')
            {
                result.Append(text[i + 1] switch { '[' => "(", ']' => ")", _ => "\\\\" });
                i++;
            }
            else
                result.Append(text[i]);
        }
        return result.ToString();
    }

    private static string Bracket(string text, int open)
    {
        var chars = text.ToCharArray();
        chars[open] = '(';
        for (var i = open + 1; i < chars.Length && chars[i] != '['; i++)
        {
            if (chars[i] == ']')
            {
                chars[i] = ')';
                break;
            }
        }
        return new string(chars);
    }

    // A tag after its "[": "/name]" closing, or "name", an optional "=value", more such
    // attributes, then "]" or "/]". The index after it, or -1 when it is not a tag.
    private static int TagEnd(string text, int i)
    {
        if (i < text.Length && text[i] == '/')
        {
            i = SkipSpace(text, i + 1);
            if (!Identifier(text, ref i))
                return -1;
            i = SkipSpace(text, i);
            return i < text.Length && text[i] == ']' ? i + 1 : -1;
        }

        i = SkipSpace(text, i);
        if (!KeyValue(text, ref i))
            return -1;
        while (i < text.Length && char.IsLetter(text[i]))
        {
            if (!KeyValue(text, ref i))
                return -1;
        }
        if (i < text.Length && text[i] == '/')
        {
            i = SkipSpace(text, i + 1);
            return i < text.Length && text[i] == ']' ? i + 1 : -1;
        }
        return i < text.Length && text[i] == ']' ? i + 1 : -1;
    }

    // "name", then optionally "= value", with spaces allowed around each.
    private static bool KeyValue(string text, ref int i)
    {
        if (!Identifier(text, ref i))
            return false;
        i = SkipSpace(text, i);
        if (i < text.Length && text[i] == '=')
        {
            i = SkipSpace(text, i + 1);
            if (!Value(text, ref i))
                return false;
        }
        i = SkipSpace(text, i);
        return true;
    }

    // A quoted string, a colour (a name or #hex), or a whole number.
    private static bool Value(string text, ref int i)
    {
        if (i >= text.Length)
            return false;
        if (text[i] == '"')
        {
            for (i++; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '[' or ']' or '"' or '/')
                    i++;
                else if (text[i] == '"')
                {
                    i++;
                    return true;
                }
            }
            return false;
        }
        if (text[i] == '#' || char.IsLetter(text[i]))
        {
            for (i++; i < text.Length && (text[i] == '#' || char.IsAsciiLetterOrDigit(text[i])); i++)
            {
            }
            return true;
        }
        if (text[i] is '+' or '-')
            i++;
        var digits = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        return i > digits;
    }

    private static bool Identifier(string text, ref int i)
    {
        if (i >= text.Length || !char.IsLetter(text[i]))
            return false;
        for (i++; i < text.Length && char.IsLetterOrDigit(text[i]); i++)
        {
        }
        return true;
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }
}
