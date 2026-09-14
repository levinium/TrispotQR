using System.Text;

namespace TrispotQR.Core.Updates;

/// <summary>How a run of release-note text is shown.</summary>
public enum NoteSpanStyle
{
    Plain,
    Bold,
    Code,
}

/// <summary>A run of text in one style.</summary>
public sealed record NoteSpan(string Text, NoteSpanStyle Style);

/// <summary>One block of release notes: a heading, a paragraph or a bullet list.</summary>
public abstract record NoteBlock;

public sealed record NoteHeading(IReadOnlyList<NoteSpan> Spans) : NoteBlock;

public sealed record NoteParagraph(IReadOnlyList<NoteSpan> Spans) : NoteBlock;

public sealed record NoteBulletList(IReadOnlyList<IReadOnlyList<NoteSpan>> Items) : NoteBlock;

/// <summary>
/// Turns a release's markdown into blocks the app can lay out itself.
///
/// Only a small subset is understood: headings, paragraphs, bullet lists, bold and code. That is
/// everything the project's own notes use, and a markdown dependency to cover the rest is not worth
/// carrying for one popup. Anything outside the subset degrades to readable plain text rather than
/// failing, so a release written differently still shows its words.
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<NoteBlock> Parse(string? markdown)
    {
        var blocks = new List<NoteBlock>();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return blocks;
        }

        var paragraph = new List<string>();
        List<string>? items = null;

        // A block or item with no text left after inline parsing ("## ", "- ", a lone "``") is dropped
        // here, so no consumer draws a blank heading or an empty bullet.
        void EndParagraph()
        {
            if (paragraph.Count > 0)
            {
                var spans = Inline(string.Join(' ', paragraph));
                if (spans.Count > 0)
                {
                    blocks.Add(new NoteParagraph(spans));
                }

                paragraph.Clear();
            }
        }

        void EndList()
        {
            if (items is not null)
            {
                var parsed = items.Select(Inline).Where(spans => spans.Count > 0).ToList();
                if (parsed.Count > 0)
                {
                    blocks.Add(new NoteBulletList(parsed));
                }

                items = null;
            }
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                EndParagraph();
                EndList();
                continue;
            }

            var indent = Indent(line);
            var rest = indent <= 3 ? line.TrimStart() : null;

            if (rest is not null && HeadingText(rest) is { } heading)
            {
                EndParagraph();
                EndList();
                if (Inline(heading) is { Count: > 0 } spans)
                {
                    blocks.Add(new NoteHeading(spans));
                }
            }
            else if (rest is not null && rest.Length >= 2 && rest[0] is '-' or '*' or '+' && rest[1] == ' ')
            {
                EndParagraph();
                (items ??= []).Add(rest[2..].Trim());
            }
            else if (items is not null && indent >= 2)
            {
                items[^1] = items[^1].Length == 0 ? line.Trim() : items[^1] + " " + line.Trim();
            }
            else
            {
                EndList();
                paragraph.Add(line.Trim());
            }
        }

        EndParagraph();
        EndList();

        return blocks;
    }

    /// <summary>Leading indentation in columns, counting a tab as four so it reads as indented.</summary>
    private static int Indent(string line)
    {
        var columns = 0;

        foreach (var c in line)
        {
            if (c == ' ')
            {
                columns++;
            }
            else if (c == '\t')
            {
                columns += 4;
            }
            else
            {
                break;
            }
        }

        return columns;
    }

    /// <summary>
    /// The text of a heading line, or null when the line is not one. A closing run of hashes is only
    /// removed when a space separates it from the text, so a heading such as "Using C#" keeps its hash.
    /// </summary>
    private static string? HeadingText(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is < 1 or > 6 || level >= line.Length || line[level] != ' ')
        {
            return null;
        }

        var text = line[(level + 1)..].Trim();
        var stripped = text.TrimEnd('#');

        if (stripped.Length == 0 || stripped[^1] == ' ')
        {
            text = stripped.Trim();
        }

        return text;
    }

    /// <summary>
    /// Splits one block's text into styled spans.
    ///
    /// Kept linear, because the notes are parsed on the UI thread and a hostile body at the feed's length
    /// cap would otherwise freeze the popup. Whether a position can close a marker does not depend on
    /// where the marker opened, and openers are met left to right, so once a search for a closer comes
    /// up empty every later search of that kind would too. Each kind remembers where it failed, a found
    /// closer moves the scan past everything it searched, and so no stretch of text is searched twice.
    /// </summary>
    private static IReadOnlyList<NoteSpan> Inline(string text)
    {
        var spans = new Spans();
        var noCloserFrom = new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
        var noParenFrom = int.MaxValue;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '`')
            {
                // A failed search means no backtick remains, so it cannot repeat.
                var close = text.IndexOf('`', i + 1);
                if (close > 0)
                {
                    spans.Add(text.AsSpan((i + 1)..close), NoteSpanStyle.Code);
                    i = close + 1;
                    continue;
                }
            }
            else if (c is '*' or '_' && i + 1 < text.Length && text[i + 1] == c)
            {
                var close = Closer(text, i, 2, noCloserFrom);
                if (close > 0)
                {
                    spans.Add(text.AsSpan((i + 2)..close), NoteSpanStyle.Bold);
                    i = close + 2;
                }
                else
                {
                    // Both markers are literal, or the second would be mistaken for an opener.
                    spans.Add(text.AsSpan(i, 2), NoteSpanStyle.Plain);
                    i += 2;
                }

                continue;
            }
            else if (c is '*' or '_')
            {
                var close = Closer(text, i, 1, noCloserFrom);
                if (close > 0)
                {
                    spans.Add(text.AsSpan((i + 1)..close), NoteSpanStyle.Plain);
                    i = close + 1;
                    continue;
                }
            }
            else if (c == '[')
            {
                // The label runs to the next bracket of either kind. A label holding a bracket is not
                // a link, so in "[x] done and [link](u)" the first bracket stays literal and the real
                // link is found when the scan reaches its own opening bracket.
                var labelEnd = text.IndexOfAny(['[', ']'], i + 1);
                if (labelEnd > 0 && text[labelEnd] == ']' && labelEnd + 1 < text.Length && text[labelEnd + 1] == '('
                    && labelEnd + 2 < noParenFrom)
                {
                    var urlEnd = text.IndexOf(')', labelEnd + 2);
                    if (urlEnd > 0)
                    {
                        spans.Add(text.AsSpan((i + 1)..labelEnd), NoteSpanStyle.Plain);
                        i = urlEnd + 1;
                        continue;
                    }

                    noParenFrom = labelEnd + 2;
                }
            }

            spans.Add(text.AsSpan(i, 1), NoteSpanStyle.Plain);
            i++;
        }

        return spans.ToList();
    }

    /// <summary>
    /// Where the emphasis marker of <paramref name="width"/> characters opening at
    /// <paramref name="open"/> closes, or -1 when it does not. An opener must be followed by text and a
    /// closer preceded by it, so "2 * 3" is arithmetic. Underscores must also sit at a word's edge, so
    /// snake_case_name is a name. <paramref name="noCloserFrom"/> holds, per marker kind, the position
    /// from which an earlier search already found nothing.
    /// </summary>
    private static int Closer(string text, int open, int width, int[] noCloserFrom)
    {
        var marker = text[open];
        var underscore = marker == '_';
        var start = open + width;
        var kind = (underscore ? 2 : 0) + width - 1;

        if (start >= text.Length || char.IsWhiteSpace(text[start])
            || (underscore && open > 0 && char.IsLetterOrDigit(text[open - 1]))
            || start + 1 >= noCloserFrom[kind])
        {
            return -1;
        }

        for (var j = start + 1; j + width <= text.Length; j++)
        {
            if (!Run(text, j, marker, width) || char.IsWhiteSpace(text[j - 1]))
            {
                continue;
            }

            var after = j + width;
            if (underscore && after < text.Length && char.IsLetterOrDigit(text[after]))
            {
                continue;
            }

            return j;
        }

        noCloserFrom[kind] = start + 1;
        return -1;
    }

    /// <summary>True when exactly <paramref name="width"/> copies of the marker start at the index.</summary>
    private static bool Run(string text, int index, char marker, int width)
    {
        for (var k = 0; k < width; k++)
        {
            if (text[index + k] != marker)
            {
                return false;
            }
        }

        var after = index + width;
        return (after >= text.Length || text[after] != marker) && text[index - 1] != marker;
    }

    /// <summary>
    /// Collects spans, dropping empty text and merging neighbours of the same style. The span being
    /// built stays in a buffer, because plain text arrives a character at a time and joining strings
    /// on every character would copy the whole run each time.
    /// </summary>
    private sealed class Spans
    {
        private readonly List<NoteSpan> _done = [];
        private readonly StringBuilder _text = new();
        private NoteSpanStyle _style;

        public void Add(ReadOnlySpan<char> text, NoteSpanStyle style)
        {
            if (text.IsEmpty)
            {
                return;
            }

            if (_style != style)
            {
                Flush();
                _style = style;
            }

            _text.Append(text);
        }

        public List<NoteSpan> ToList()
        {
            Flush();
            return _done;
        }

        private void Flush()
        {
            if (_text.Length > 0)
            {
                _done.Add(new NoteSpan(_text.ToString(), _style));
                _text.Clear();
            }
        }
    }
}
