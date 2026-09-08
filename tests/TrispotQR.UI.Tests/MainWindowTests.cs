using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.UI;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

public class MainWindowTests
{
    /// <summary>
    /// Opens the window and selects the plain-text editor.
    ///
    /// Selecting it explicitly matters: MainViewModel restores the last-used content type from
    /// the real settings file, and MainWindow is the composition root so no test can hand it a
    /// different store. Assuming plain text is selected would make these tests pass or fail
    /// depending on what the developer last did in the shipping app.
    ///
    /// Deliberately never closed. MainWindow.OnClosing calls MainViewModel.SaveSession, which
    /// writes to the developer's real %APPDATA%\TrispotQR\settings.json (MainWindow is the
    /// composition root, so it always builds the default, real AppSettingsStore -- there is no
    /// way for a test to hand it a different one without changing MainViewModel, which this task
    /// is not allowed to do). Calling Close() here would silently overwrite that file with
    /// whatever this test run happened to leave the view model holding.
    /// </summary>
    private static (MainWindow Window, MainViewModel Model, PlainTextEditor Editor) Open()
    {
        var window = new MainWindow();
        window.Show();

        var model = Assert.IsType<MainViewModel>(window.DataContext);
        var editor = model.ContentEditors.OfType<PlainTextEditor>().Single();
        model.SelectedContent = editor;

        return (window, model, editor);
    }

    private static Button FindButton(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));

    /// <summary>
    /// The realised content editor, scoped to exactly what the "what goes in the code"
    /// ContentControl produced -- not the whole window. Searching the whole window would also
    /// catch FluentTheme's ComboBox control template, which carries its own internal
    /// PART_EditableTextBox even though the content-type picker is never actually editable;
    /// found empirically when a window-wide TextBox search kept turning up two boxes instead
    /// of one. MainWindow.axaml names this ContentControl "ContentEditorHost" for exactly this
    /// reason.
    /// </summary>
    private static ContentControl ContentHost(Window window) =>
        window.FindControl<ContentControl>("ContentEditorHost")
            ?? throw new InvalidOperationException("MainWindow no longer has a ContentEditorHost.");

    /// <summary>
    /// The form-level message TextBlock, named in MainWindow.axaml for exactly this: a test
    /// needs to reach this one TextBlock specifically, not merely the one that happens to hold
    /// a given piece of text at the moment it runs.
    /// </summary>
    private static TextBlock FormMessage(Window window) =>
        window.FindControl<TextBlock>("FormMessageText")
            ?? throw new InvalidOperationException("MainWindow no longer has a FormMessageText.");

    [AvaloniaFact]
    public void OpensAtTheSizeTheLastSessionLeftBehind()
    {
        // OnClosing has always written Width and Height into settings.json; nothing read them
        // back, so every launch reverted to the 1000x700 hardcoded in MainWindow.axaml. The
        // WPF app restores them from the same file, and the two share it, so a user switching
        // between them saw the Avalonia one silently discard the size the other kept.
        //
        // Asserted against LoadedSettings rather than a literal because MainWindow is the
        // composition root and builds the real settings store: there is no fixture to point at
        // a temporary file without changing MainViewModel. The XAML's 1000x700 is not the
        // stored default (1180x800), so a regression that dropped the restore shows up here on
        // any machine that has never run the app, CI included.
        var (window, model, _) = Open();
        var settings = model.LoadedSettings;

        Assert.Equal(settings.WindowWidth, window.Width);
        Assert.Equal(settings.WindowHeight, window.Height);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOnceThereIsContent()
    {
        var (window, model, editor) = Open();

        editor.Text = "https://www.emanuelnyc.org";

        // RefreshNow skips the debounce timer, which is what the window uses in normal running.
        // Waiting on a real 150ms tick here would make the test slow and flaky for no gain.
        model.RefreshNow();
        DispatcherPump.Drain();

        Assert.NotNull(model.PreviewDrawing);

        var preview = window.GetVisualDescendants().OfType<QrPreview>().Single();
        Assert.NotNull(preview.Drawing);
    }

    [AvaloniaFact]
    public void CannotExportWithNoContent()
    {
        var (window, model, editor) = Open();

        editor.Text = string.Empty;
        model.RefreshNow();
        DispatcherPump.Drain();

        Assert.False(model.CanExport);
        Assert.False(model.SavePngCommand.CanExecute(null));

        // Through the actual bound control too, not just the view model. A Button wired to a
        // Command tracks CanExecute through IsEffectivelyEnabled (and the ":disabled"
        // pseudoclass) rather than through the plain IsEnabled property -- confirmed
        // empirically: IsEnabled stayed true here even with CanExecute false, since nothing in
        // this window sets IsEnabled directly. IsEffectivelyEnabled is what actually gates
        // whether a click reaches the command and what the disabled visual state reflects, so
        // it is the one worth asserting.
        Assert.False(FindButton(window, "Save PNG").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void CanExportOnceThereIsContent()
    {
        var (window, model, editor) = Open();

        editor.Text = "https://www.emanuelnyc.org";
        model.RefreshNow();
        DispatcherPump.Drain();

        Assert.True(model.CanExport);
        Assert.True(model.SavePngCommand.CanExecute(null));
        Assert.True(FindButton(window, "Save PNG").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void RealizesThePlainTextEditorsOwnBoxRatherThanAnyOtherTemplate()
    {
        // Regression coverage for the exact failure mode the brief warns about: a DataTemplate
        // registered for the ContentEditor base type ahead of the PlainTextEditor-specific one
        // would swallow it silently, and every one of the other MainWindowTests would keep
        // passing regardless -- none of them look at what the ContentControl actually realised.
        // Reproduced empirically: reordering MainWindow.axaml's DataTemplates so the base-type
        // catch-all comes first turned this test red while leaving the rest of the suite green.
        var (window, _, editor) = Open();
        DispatcherPump.Drain();

        var box = Assert.Single(ContentHost(window).GetVisualDescendants().OfType<FieldBox>());

        // The realised child's DataContext is the bound Content instance itself (standard
        // ContentPresenter behaviour), so this also confirms it is bound to this editor and
        // not some other control that merely happens to be the only FieldBox in the host.
        Assert.Same(editor, box.DataContext);

        // A live round-trip through the real bound property, not just a type check: if some
        // other template had won, there would be no FieldBox here to receive this at all.
        editor.Text = "round-trip";
        DispatcherPump.Drain();
        Assert.Equal("round-trip", box.Text);
    }

    [AvaloniaFact]
    public void RealizesTheLinkEditorsOwnBoxRatherThanAnyOtherTemplate()
    {
        // PlainTextEditor and LinkEditor are the two templates ordered first in
        // MainWindow.axaml's DataTemplates; this and the test above are what would catch either
        // one being shadowed by a template registered ahead of it.
        var (window, model, _) = Open();
        var link = model.ContentEditors.OfType<LinkEditor>().Single();
        model.SelectedContent = link;
        DispatcherPump.Drain();

        var box = Assert.Single(ContentHost(window).GetVisualDescendants().OfType<FieldBox>());
        Assert.Same(link, box.DataContext);

        link.Address = "example.org";
        DispatcherPump.Drain();
        Assert.Equal("example.org", box.Text);
    }

    [AvaloniaFact]
    public void EveryContentTypeRealizesRealFieldsRatherThanAPlaceholder()
    {
        // Iterating the collection rather than naming the seven types: a content type added
        // later fails here instead of quietly rendering an empty panel.
        var window = new MainWindow();
        window.Show();
        var model = Assert.IsType<MainViewModel>(window.DataContext);

        foreach (var editor in model.ContentEditors)
        {
            model.SelectedContent = editor;
            DispatcherPump.Drain();

            var boxes = ContentHost(window).GetVisualDescendants().OfType<FieldBox>().ToList();
            Assert.True(boxes.Count > 0, $"{editor.Title} rendered no fields");
            Assert.All(boxes, b => Assert.False(string.IsNullOrEmpty(b.FieldName)));
        }
    }

    [AvaloniaFact]
    public void AnInformationalNoteIsNotShownAsAProblem()
    {
        // The link editor's note is guidance, not a fault. Colouring it red would tell the
        // user something is wrong when nothing is.
        var (window, model, _) = Open();
        var link = model.ContentEditors.OfType<LinkEditor>().Single();
        model.SelectedContent = link;
        link.Address = "www.emanuelnyc.org";
        DispatcherPump.Drain();

        Assert.False(link.FormMessageIsProblem);
        Assert.False(FormMessage(window).Classes.Contains("problem"));
    }

    [AvaloniaFact]
    public void AGenuineFormProblemColoursTheMessageInTheDangerBrush()
    {
        // The discrimination case for the test above: an empty contact card fails
        // ContactEditor's form-level "needs a name" rule, so FormMessageIsProblem is true and
        // this must render differently. Checking only the class string would pass even if the
        // Classes.problem="{Binding ...}" binding syntax silently failed to reach the class at
        // all, or if TextBlock.problem's Setter never actually applied -- Phase 2b's style
        // selector shipped exactly that failure once, with every test still green. Reading the
        // rendered Foreground through the same resource lookup FieldBoxTests uses is what rules
        // both out.
        var (window, model, _) = Open();
        var contact = model.ContentEditors.OfType<ContactEditor>().Single();
        model.SelectedContent = contact;
        DispatcherPump.Drain();

        Assert.True(contact.FormMessageIsProblem);
        var message = FormMessage(window);
        Assert.True(message.Classes.Contains("problem"));

        Avalonia.Application.Current!.TryGetResource(
            "DangerBrush", Avalonia.Styling.ThemeVariant.Default, out var expected);
        var actual = message.Foreground as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(actual);
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)expected!).Color, actual!.Color);
    }

    [AvaloniaFact]
    public void TheFormMessageIsHiddenWhenThereIsNothingToSay()
    {
        // PlainTextEditor never sets a Note and never reports a Form-level issue, so with the
        // default empty Text its FormMessage is null. A binding that fails open -- rendering an
        // empty line instead of collapsing it -- would look, to a user, like nothing at all and
        // pass unnoticed; this is what StringConverters.IsNotNullOrEmpty is there to prevent.
        var (window, _, editor) = Open();
        DispatcherPump.Drain();

        Assert.True(string.IsNullOrEmpty(editor.FormMessage));
        Assert.False(FormMessage(window).IsVisible);
    }

    [AvaloniaFact]
    public void TheWindowLaysOutWithoutThrowing()
    {
        // NOT a catch-all for binding or template-selection mistakes: Avalonia logs a mismatched
        // DataTemplate or a failed binding rather than throwing, so this would not have caught,
        // for example, the base-type-template-shadows-a-derived-one regression that
        // RealizesTheActualPlainTextBoxRatherThanThePlaceholder exists to catch (confirmed: that
        // regression left this test green). What this does prove is that a full render and
        // layout pass over the real window -- not just a constructed-but-never-shown one --
        // completes without an exception, which is a real, if narrower, guarantee.
        var (window, model, editor) = Open();

        editor.Text = "test";
        model.RefreshNow();
        DispatcherPump.Drain();

        Assert.NotNull(window.CaptureRenderedFrame());
    }
}
