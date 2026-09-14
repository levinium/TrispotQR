using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrispotQR.Core.Updates;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The window What's new opens: the notes laid out in the app, and the three ways out of it.
/// Every answer is reached through a real click on the rendered button, because a handler that
/// exists but was never attached looks the same from a direct call.
/// </summary>
public class ReleaseNotesWindowTests
{
    private static readonly IReadOnlyList<NoteBlock> SomeNotes = ReleaseNotes.Parse("## Fixes\n\n- Something was fixed.");

    [AvaloniaFact]
    public void ClosingTheWindowWithoutChoosingReturnsClose()
    {
        // Stands in for Escape and the title bar close, neither of which runs a button handler.
        var (dialog, task) = Show(SomeNotes, "Update now");

        dialog.Close();

        Assert.Equal(ReleaseNotesChoice.Close, DispatcherWait.For(task, TimeSpan.FromSeconds(5)));
    }

    [AvaloniaFact]
    public void TheCloseButtonReturnsClose()
    {
        var (dialog, task) = Show(SomeNotes, "Update now");

        UiHarness.Click(dialog, UiHarness.At(Field<Button>(dialog, "CloseButton"), 0.5, 0.5));

        Assert.Equal(ReleaseNotesChoice.Close, DispatcherWait.For(task, TimeSpan.FromSeconds(5)));
    }

    [AvaloniaFact]
    public void ThePrimaryButtonReturnsPrimaryAndShowsItsLabel()
    {
        var (dialog, task) = Show(SomeNotes, "Update now");
        var primary = Field<Button>(dialog, "PrimaryButton");

        Assert.True(primary.IsVisible);
        Assert.Equal("Update now", primary.Content);

        UiHarness.Click(dialog, UiHarness.At(primary, 0.5, 0.5));

        Assert.Equal(ReleaseNotesChoice.Primary, DispatcherWait.For(task, TimeSpan.FromSeconds(5)));
    }

    [AvaloniaFact]
    public void TheViewOnGitHubLinkReturnsViewOnline()
    {
        var (dialog, task) = Show(SomeNotes, "Update now");

        UiHarness.Click(dialog, UiHarness.At(Field<Button>(dialog, "ViewOnlineButton"), 0.5, 0.5));

        Assert.Equal(ReleaseNotesChoice.ViewOnline, DispatcherWait.For(task, TimeSpan.FromSeconds(5)));
    }

    [AvaloniaFact]
    public void WithNoPrimaryActionThePrimaryButtonIsHidden()
    {
        var (dialog, task) = Show(SomeNotes, primaryLabel: null);

        Assert.False(Field<Button>(dialog, "PrimaryButton").IsVisible);

        dialog.Close();
        DispatcherWait.For(task, TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public void HeadingsParagraphsAndBulletsAreRendered()
    {
        var notes = ReleaseNotes.Parse("## Fixes\n\nIntro with **bold** and `code`.\n\n- One\n- Two");
        var (dialog, task) = Show(notes, "Update now");

        var heading = Assert.Single(WithClass<SelectableTextBlock>(dialog, "note-heading"));
        Assert.Equal("Fixes", TextOf(heading));

        var paragraph = Assert.Single(WithClass<SelectableTextBlock>(dialog, "note-paragraph"));
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeight.SemiBold);
        Assert.Contains(runs, r => r.Text == "code" && (r.FontFamily.Name.Contains("Mono") || r.FontFamily.Name.Contains("Consolas")));

        var list = Assert.Single(WithClass<Control>(dialog, "note-list"));
        Assert.Equal(2, list.GetVisualDescendants().OfType<Control>().Count(c => c.Classes.Contains("note-item")));

        dialog.Close();
        DispatcherWait.For(task, TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public void EmptyNotesSayThereAreNone()
    {
        var (dialog, task) = Show([], primaryLabel: null);

        var empty = Assert.Single(WithClass<TextBlock>(dialog, "note-empty"));
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal("No notes were published for this release.", empty.Text);

        // The notes may be missing from the feed and still be on the release page.
        Assert.True(Field<Button>(dialog, "ViewOnlineButton").IsEffectivelyVisible);

        dialog.Close();
        DispatcherWait.For(task, TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public void LongNotesScrollInsideTheWindow()
    {
        var notes = Enumerable.Range(1, 200)
            .Select(i => (NoteBlock)new NoteParagraph([new NoteSpan($"Paragraph {i} of a very long release.", NoteSpanStyle.Plain)]))
            .ToList();
        var (dialog, task) = Show(notes, "Update now");
        DispatcherPump.Drain();

        Assert.True(dialog.Bounds.Height <= 640, $"the window is {dialog.Bounds.Height:0} px tall");

        var scroller = Field<ScrollViewer>(dialog, "NotesScroller");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
            $"extent {scroller.Extent.Height:0}, viewport {scroller.Viewport.Height:0}");

        // Its whole height, top edge to bottom edge, inside the window.
        var closeButton = Field<Button>(dialog, "CloseButton");
        var top = UiHarness.At(closeButton, 0.5, 0).Y;
        var bottom = UiHarness.At(closeButton, 0.5, 1).Y;
        Assert.True(closeButton.IsEffectivelyVisible);
        Assert.True(top >= 0 && bottom <= dialog.Bounds.Height, $"Close spans y {top:0} to {bottom:0} in a {dialog.Bounds.Height:0} px window");

        dialog.Close();
        DispatcherWait.For(task, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Opens the window on a shown owner and pumps until it is there, for the same reason
    /// MessageWindowTests does: ShowDialog adds the child to OwnedWindows a frame or two late.
    /// </summary>
    private static (ReleaseNotesWindow Dialog, Task<ReleaseNotesChoice> Task) Show(IReadOnlyList<NoteBlock> notes, string? primaryLabel)
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();

        var task = ReleaseNotesWindow.ShowAsync(owner, "What's new in Trispot QR 9.9.9", notes, primaryLabel);

        ReleaseNotesWindow? dialog = null;
        DispatcherPump.DrainUntil(() => (dialog = owner.OwnedWindows.OfType<ReleaseNotesWindow>().SingleOrDefault()) is not null);

        Assert.NotNull(dialog);
        DispatcherPump.Drain();
        return (dialog, task);
    }

    /// <summary>A named element's field, reached by reflection as MessageWindowTests does, since it is internal to the UI assembly.</summary>
    private static T Field<T>(ReleaseNotesWindow dialog, string name) where T : Control
    {
        var field = typeof(ReleaseNotesWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ReleaseNotesWindow no longer has a {name} field.");
        return (T)field.GetValue(dialog)!;
    }

    private static IEnumerable<T> WithClass<T>(Window window, string className) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(c => c.Classes.Contains(className));

    internal static string TextOf(SelectableTextBlock block) =>
        block.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.OfType<Run>().Select(r => r.Text))
            : block.Text ?? string.Empty;
}
