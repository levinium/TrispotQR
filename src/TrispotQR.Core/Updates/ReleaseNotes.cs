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

        void EndParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new NoteParagraph(Inline(string.Join(' ', paragraph))));
                paragraph.Clear();
            }
        }

        void EndList()
        {
            if (items is not null)
            {
                blocks.Add(new NoteBulletList(items.Select(Inline).ToList()));
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
                blocks.Add(new NoteHeading(Inline(heading)));
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

    /// <summary>Splits one block's text into styled spans.</summary>
    private static IReadOnlyList<NoteSpan> Inline(string text)
    {
        var spans = new List<NoteSpan>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > 0)
                {
                    Add(spans, text[(i + 1)..close], NoteSpanStyle.Code);
                    i = close + 1;
                    continue;
                }
            }
            else if (c is '*' or '_' && i + 1 < text.Length && text[i + 1] == c)
            {
                var close = Closer(text, i, 2);
                if (close > 0)
                {
                    Add(spans, text[(i + 2)..close], NoteSpanStyle.Bold);
                    i = close + 2;
                }
                else
                {
                    // Both markers are literal, or the second would be mistaken for an opener.
                    Add(spans, text.Substring(i, 2), NoteSpanStyle.Plain);
                    i += 2;
                }

                continue;
            }
            else if (c is '*' or '_')
            {
                var close = Closer(text, i, 1);
                if (close > 0)
                {
                    Add(spans, text[(i + 1)..close], NoteSpanStyle.Plain);
                    i = close + 1;
                    continue;
                }
            }
            else if (c == '[')
            {
                var labelEnd = text.IndexOf("](", i + 1, StringComparison.Ordinal);
                var urlEnd = labelEnd > 0 ? text.IndexOf(')', labelEnd + 2) : -1;
                if (urlEnd > 0)
                {
                    Add(spans, text[(i + 1)..labelEnd], NoteSpanStyle.Plain);
                    i = urlEnd + 1;
                    continue;
                }
            }

            Add(spans, c.ToString(), NoteSpanStyle.Plain);
            i++;
        }

        return spans;
    }

    /// <summary>
    /// Where the emphasis marker of <paramref name="width"/> characters opening at
    /// <paramref name="open"/> closes, or -1 when it does not. An opener must be followed by text and a
    /// closer preceded by it, so "2 * 3" is arithmetic. Underscores must also sit at a word's edge, so
    /// snake_case_name is a name.
    /// </summary>
    private static int Closer(string text, int open, int width)
    {
        var marker = text[open];
        var underscore = marker == '_';
        var start = open + width;

        if (start >= text.Length || char.IsWhiteSpace(text[start])
            || (underscore && open > 0 && char.IsLetterOrDigit(text[open - 1])))
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

    /// <summary>Appends a span, dropping empty text and merging with a neighbour of the same style.</summary>
    private static void Add(List<NoteSpan> spans, string text, NoteSpanStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1].Style == style)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + text };
        }
        else
        {
            spans.Add(new NoteSpan(text, style));
        }
    }
}
