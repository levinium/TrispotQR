using System.Diagnostics;
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class ReleaseNotesTests
{
    private static NoteSpan Plain(string text) => new(text, NoteSpanStyle.Plain);

    private static NoteSpan Bold(string text) => new(text, NoteSpanStyle.Bold);

    private static NoteSpan Code(string text) => new(text, NoteSpanStyle.Code);

    private static IReadOnlyList<NoteSpan> SpansOfOnlyParagraph(string markdown) =>
        Assert.IsType<NoteParagraph>(Assert.Single(ReleaseNotes.Parse(markdown))).Spans;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t\r\n  ")]
    public void NothingToReadGivesNoBlocks(string? markdown)
    {
        Assert.Empty(ReleaseNotes.Parse(markdown));
    }

    [Fact]
    public void AHashAndASpaceMakeAHeading()
    {
        var heading = Assert.IsType<NoteHeading>(Assert.Single(ReleaseNotes.Parse("## What's new")));

        Assert.Equal([Plain("What's new")], heading.Spans);
    }

    [Fact]
    public void TrailingHashesAreRemovedFromAHeading()
    {
        var heading = Assert.IsType<NoteHeading>(Assert.Single(ReleaseNotes.Parse("### Title ###")));

        Assert.Equal([Plain("Title")], heading.Spans);
    }

    [Fact]
    public void AHashThatEndsAWordStaysInAHeading()
    {
        var heading = Assert.IsType<NoteHeading>(Assert.Single(ReleaseNotes.Parse("## Using C#")));

        Assert.Equal([Plain("Using C#")], heading.Spans);
    }

    [Fact]
    public void AHashWithNoSpaceIsParagraphText()
    {
        Assert.Equal([Plain("#NoSpace")], SpansOfOnlyParagraph("#NoSpace"));
    }

    [Fact]
    public void ConsecutiveLinesJoinIntoOneParagraph()
    {
        Assert.Equal([Plain("first line second line")], SpansOfOnlyParagraph("first line\nsecond line"));
    }

    [Fact]
    public void ABlankLineSeparatesParagraphs()
    {
        var blocks = ReleaseNotes.Parse("first\n\nsecond");

        Assert.Equal(2, blocks.Count);
        Assert.Equal([Plain("first")], Assert.IsType<NoteParagraph>(blocks[0]).Spans);
        Assert.Equal([Plain("second")], Assert.IsType<NoteParagraph>(blocks[1]).Spans);
    }

    [Fact]
    public void ConsecutiveBulletsFormOneList()
    {
        var list = Assert.IsType<NoteBulletList>(Assert.Single(ReleaseNotes.Parse("- one\n- two\n* three")));

        Assert.Equal(3, list.Items.Count);
        Assert.Equal([Plain("one")], list.Items[0]);
        Assert.Equal([Plain("two")], list.Items[1]);
        Assert.Equal([Plain("three")], list.Items[2]);
    }

    [Fact]
    public void ABlankLineBetweenBulletsMakesTwoLists()
    {
        var blocks = ReleaseNotes.Parse("- one\n\n+ two");

        Assert.Equal(2, blocks.Count);
        Assert.Equal([Plain("one")], Assert.Single(Assert.IsType<NoteBulletList>(blocks[0]).Items));
        Assert.Equal([Plain("two")], Assert.Single(Assert.IsType<NoteBulletList>(blocks[1]).Items));
    }

    [Fact]
    public void AnIndentedLineContinuesItsBullet()
    {
        var list = Assert.IsType<NoteBulletList>(Assert.Single(ReleaseNotes.Parse("- one\n  carries on\n- two")));

        Assert.Equal(2, list.Items.Count);
        Assert.Equal([Plain("one carries on")], list.Items[0]);
        Assert.Equal([Plain("two")], list.Items[1]);
    }

    [Fact]
    public void AnUnindentedLineAfterABulletIsANewParagraph()
    {
        var blocks = ReleaseNotes.Parse("- one\nafter");

        Assert.Equal(2, blocks.Count);
        Assert.IsType<NoteBulletList>(blocks[0]);
        Assert.Equal([Plain("after")], Assert.IsType<NoteParagraph>(blocks[1]).Spans);
    }

    [Fact]
    public void AHeadingDirectlyAfterParagraphTextStartsANewBlock()
    {
        var blocks = ReleaseNotes.Parse("some text\n## Heading");

        Assert.Equal(2, blocks.Count);
        Assert.Equal([Plain("some text")], Assert.IsType<NoteParagraph>(blocks[0]).Spans);
        Assert.Equal([Plain("Heading")], Assert.IsType<NoteHeading>(blocks[1]).Spans);
    }

    [Fact]
    public void ABulletDirectlyAfterParagraphTextStartsANewBlock()
    {
        var blocks = ReleaseNotes.Parse("some text\n- item");

        Assert.Equal(2, blocks.Count);
        Assert.IsType<NoteParagraph>(blocks[0]);
        Assert.IsType<NoteBulletList>(blocks[1]);
    }

    [Fact]
    public void DoubleStarsMakeBold()
    {
        Assert.Equal(
            [Bold("Logos are saved with a style."), Plain(" Saving a favorite")],
            SpansOfOnlyParagraph("**Logos are saved with a style.** Saving a favorite"));
    }

    [Fact]
    public void DoubleUnderscoresMakeBold()
    {
        Assert.Equal([Plain("a "), Bold("strong"), Plain(" word")], SpansOfOnlyParagraph("a __strong__ word"));
    }

    [Fact]
    public void BackticksMakeCode()
    {
        Assert.Equal(
            [Plain("Run "), Code("TrispotQR.exe"), Plain(" now")],
            SpansOfOnlyParagraph("Run `TrispotQR.exe` now"));
    }

    [Fact]
    public void NothingInsideCodeIsInterpreted()
    {
        Assert.Equal([Code("**not bold**")], SpansOfOnlyParagraph("`**not bold**`"));
    }

    [Fact]
    public void ALinkBecomesItsLabel()
    {
        Assert.Equal([Plain("the release page")], SpansOfOnlyParagraph("[the release page](https://example.org)"));
    }

    [Fact]
    public void BracketsBeforeALinkStayLiteral()
    {
        Assert.Equal([Plain("[x] done and link")], SpansOfOnlyParagraph("[x] done and [link](u)"));
    }

    [Fact]
    public void AnEmptyHeadingIsDropped()
    {
        Assert.Empty(ReleaseNotes.Parse("## "));
    }

    [Fact]
    public void AnEmptyBulletIsDropped()
    {
        var list = Assert.IsType<NoteBulletList>(Assert.Single(ReleaseNotes.Parse("- \n- one")));

        Assert.Equal([Plain("one")], Assert.Single(list.Items));
    }

    [Fact]
    public void AListOfOnlyEmptyBulletsIsDropped()
    {
        Assert.Empty(ReleaseNotes.Parse("- \n-  "));
    }

    [Theory]
    [InlineData(" _a")]
    [InlineData("*a")]
    [InlineData("`a")]
    [InlineData("[a")]
    [InlineData("[a](")]
    [InlineData("**a")]
    public void APathologicalBodyParsesQuickly(string unit)
    {
        // The view model parses on the UI thread, so a hostile body at the feed's length cap must
        // not freeze the popup. The bound is generous so a slow CI runner does not flake.
        var body = string.Concat(Enumerable.Repeat(unit, ReleaseFeed.MaxNotesLength / unit.Length + 1))[..ReleaseFeed.MaxNotesLength];
        ReleaseNotes.Parse(unit);

        var clock = Stopwatch.StartNew();
        ReleaseNotes.Parse(body);
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 200, $"Parsing took {clock.ElapsedMilliseconds} ms.");
    }

    [Theory]
    [InlineData("*emphasis*")]
    [InlineData("_emphasis_")]
    public void ItalicBecomesPlainText(string markdown)
    {
        Assert.Equal([Plain("emphasis")], SpansOfOnlyParagraph(markdown));
    }

    [Fact]
    public void UnderscoresInsideAWordAreNotItalic()
    {
        Assert.Equal([Plain("snake_case_name")], SpansOfOnlyParagraph("snake_case_name"));
    }

    [Theory]
    [InlineData("2 * 3 = 6")]
    [InlineData("a ** b")]
    [InlineData("an ` unclosed")]
    public void AMarkerWithNoPartnerIsLiteral(string markdown)
    {
        Assert.Equal([Plain(markdown)], SpansOfOnlyParagraph(markdown));
    }

    [Fact]
    public void WindowsLineEndingsParseTheSameAsUnix()
    {
        const string unix = "## Heading\n\nfirst\nsecond\n\n- one\n  more\n- two";

        Assert.Equal(Describe(ReleaseNotes.Parse(unix)), Describe(ReleaseNotes.Parse(unix.Replace("\n", "\r\n"))));
    }

    [Fact]
    public void TheRealVersionOnePointOneNotesParseIntoTheirSections()
    {
        const string notes = """
            ## Download

            **TrispotQR-v1.1.0-win-x64.zip** (45 MB) is the one to take. Unzip it anywhere and run `TrispotQR.exe`. Nothing to install, no admin prompt, and the .NET runtime is inside the file. You need 64-bit Windows and nothing else.

            **TrispotQR-v1.1.0-win-x64-framework-dependent.zip** (13 MB) is smaller but needs the .NET 10 runtime already installed. Keep its files together in one folder.

            Upgrading from 1.0.0 is a straight replacement. Saved styles and settings live outside the app and carry over untouched.

            ## What's new

            **Logos are saved with a style.** Saving a favorite used to drop its logo, because the only thing it could record was where the image sat on disk. The app now keeps its own copy, so a saved style still has its logo after the original file is renamed, moved, or left on another machine. Applying a style brings its logo with it.

            **The color picker offers colors you're already using.** Two new rows show the colors in the current code and the ones you picked recently, so matching one element to another no longer means copying a hex code.

            **Favorites live in the style strip.** Saving a style is a card at the end of the row, and each saved style has a small remove button.

            **Logo and corner colors moved out of Advanced options** into "How it looks", next to the code color they belong with.

            **Built on Avalonia instead of WPF.** It looks and works the same. The change is what makes Mac and Linux versions possible. Those aren't released yet, but the app now builds and passes its tests on all three systems.

            ## Fixes

            - Copy now works in Word, Outlook and Excel, and confirms that it did. Before, it showed no confirmation and those apps pasted nothing.
            - The copy confirmation is readable in dark mode.
            - Color swatch outlines are easier to see, especially in dark mode.
            - US spelling throughout.
            """;

        var blocks = ReleaseNotes.Parse(notes);

        Assert.Collection(
            blocks,
            b => Assert.Equal([Plain("Download")], Assert.IsType<NoteHeading>(b).Spans),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.Equal([Plain("What's new")], Assert.IsType<NoteHeading>(b).Spans),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.IsType<NoteParagraph>(b),
            b => Assert.Equal([Plain("Fixes")], Assert.IsType<NoteHeading>(b).Spans),
            b => Assert.Equal(4, Assert.IsType<NoteBulletList>(b).Items.Count));

        Assert.Equal(Bold("TrispotQR-v1.1.0-win-x64.zip"), Assert.IsType<NoteParagraph>(blocks[1]).Spans[0]);
    }

    /// <summary>Records compare their lists by reference, so tests compare this rendering instead.</summary>
    private static string Describe(IReadOnlyList<NoteBlock> blocks) =>
        string.Join("|", blocks.Select(b => b switch
        {
            NoteHeading h => "H:" + Describe(h.Spans),
            NoteParagraph p => "P:" + Describe(p.Spans),
            NoteBulletList l => "L:" + string.Join(";", l.Items.Select(Describe)),
            _ => "?",
        }));

    private static string Describe(IReadOnlyList<NoteSpan> spans) =>
        string.Join(",", spans.Select(s => $"{s.Style}({s.Text})"));
}
