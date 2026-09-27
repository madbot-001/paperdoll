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
        var unclosedFrom = int.MaxValue;
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
                    var end = TagEnd(text, i + 1, ref unclosedFrom);
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
            text = Repaired(text);
            var kept = Read(text).Kept;
            if (kept == text)
                return text;
            text = kept;
        }
    }

    // Reads as the game does, but mends each place it would refuse or stop at and reads on, in
    // one pass: mending one place and reading again from the start would take hours on a text
    // made of many of them.
    private static string Repaired(string text)
    {
        var chars = text.ToCharArray();
        var result = new StringBuilder(chars.Length);
        var unclosedFrom = int.MaxValue;
        var i = 0;
        while (i < chars.Length)
        {
            switch (chars[i])
            {
                case '\\' when i + 1 < chars.Length && chars[i + 1] is '\\' or '[' or ']' or '/':
                    result.Append(chars, i, 2);
                    i += 2;
                    break;
                case '\\':
                    // The game would stop reading here: dropped.
                    i++;
                    break;
                case '[':
                    var end = TagEnd(chars, i + 1, ref unclosedFrom);
                    if (end < 0)
                    {
                        // The game would refuse it: made a parenthesis, which is read next.
                        Bracket(chars, i);
                        break;
                    }
                    result.Append(chars, i, end - i);
                    i = end;
                    break;
                default:
                    result.Append(chars[i++]);
                    break;
            }
        }
        return result.ToString();
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

    private static void Bracket(char[] chars, int open)
    {
        chars[open] = '(';
        for (var i = open + 1; i < chars.Length && chars[i] != '['; i++)
        {
            if (chars[i] == ']')
            {
                chars[i] = ')';
                break;
            }
        }
    }

    // A tag after its "[": "/name]" closing, or "name", an optional "=value", more such
    // attributes, then "]" or "/]". The index after it, or -1 when it is not a tag.
    private static int TagEnd(ReadOnlySpan<char> text, int i, ref int unclosedFrom)
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
        if (!KeyValue(text, ref i, ref unclosedFrom))
            return -1;
        while (i < text.Length && char.IsLetter(text[i]))
        {
            if (!KeyValue(text, ref i, ref unclosedFrom))
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
    private static bool KeyValue(ReadOnlySpan<char> text, ref int i, ref int unclosedFrom)
    {
        if (!Identifier(text, ref i))
            return false;
        i = SkipSpace(text, i);
        if (i < text.Length && text[i] == '=')
        {
            i = SkipSpace(text, i + 1);
            if (!Value(text, ref i, ref unclosedFrom))
                return false;
        }
        i = SkipSpace(text, i);
        return true;
    }

    // A quoted string, a colour (a name or #hex), or a whole number.
    private static bool Value(ReadOnlySpan<char> text, ref int i, ref int unclosedFrom)
    {
        if (i >= text.Length)
            return false;
        if (text[i] == '"')
        {
            // A quote left open runs to the end. Any later one opens after a quote that one
            // passed over as escaped, so it reads the same rest and is left open too: known at
            // once, or text made of many would take hours.
            if (i >= unclosedFrom)
                return false;
            var start = i;
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
            unclosedFrom = start;
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

    private static bool Identifier(ReadOnlySpan<char> text, ref int i)
    {
        if (i >= text.Length || !char.IsLetter(text[i]))
            return false;
        for (i++; i < text.Length && char.IsLetterOrDigit(text[i]); i++)
        {
        }
        return true;
    }

    private static int SkipSpace(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }
}
