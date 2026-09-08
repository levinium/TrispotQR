using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

    /// <summary>
    /// Runs the dispatcher enough times for a ContentPresenter to realise its DataTemplate's
    /// child (the TextBox or placeholder TextBlock does not exist in the visual tree until
    /// that happens) and for a bound Button's IsEffectivelyEnabled to catch up with the latest
    /// CanExecuteChanged. Bounded and repeated rather than a single call, the same way
    /// MessageWindowTests' ClickConfirmButton forces layout before reading a control's
    /// on-screen state.
    /// </summary>
    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
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

    [AvaloniaFact]
    public void OpensWithAViewModelAttached()
    {
        var (_, model, editor) = Open();

        Assert.NotEmpty(model.ContentEditors);
        Assert.Same(editor, model.SelectedContent);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOnceThereIsContent()
    {
        var (window, model, editor) = Open();

        editor.Text = "https://www.emanuelnyc.org";

        // RefreshNow skips the debounce timer, which is what the window uses in normal running.
        // Waiting on a real 150ms tick here would make the test slow and flaky for no gain.
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

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
        Pump();

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
        Pump();

        Assert.True(model.CanExport);
        Assert.True(model.SavePngCommand.CanExecute(null));
        Assert.True(FindButton(window, "Save PNG").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void RealizesTheActualPlainTextBoxRatherThanThePlaceholder()
    {
        // Regression coverage for the exact failure mode the brief warns about: a DataTemplate
        // registered for the ContentEditor base type ahead of the PlainTextEditor-specific one
        // would swallow it silently, and every one of the other MainWindowTests would keep
        // passing regardless -- none of them look at what the ContentControl actually realised.
        // Reproduced empirically: reordering MainWindow.axaml's DataTemplates so the base-type
        // catch-all comes first turned this test red while leaving the rest of the suite green.
        var (window, _, editor) = Open();
        Pump();

        var textBox = Assert.Single(ContentHost(window).GetVisualDescendants().OfType<TextBox>());

        // The realised child's DataContext is the bound Content instance itself (standard
        // ContentPresenter behaviour), so this also confirms it is bound to this editor and
        // not some other control that merely happens to be the only TextBox in the host.
        Assert.Same(editor, textBox.DataContext);

        // A live round-trip through the real bound property, not just a type check: if the
        // catch-all placeholder template had won, there would be no TextBox here to receive
        // this at all.
        editor.Text = "round-trip";
        Pump();
        Assert.Equal("round-trip", textBox.Text);
    }

    [AvaloniaFact]
    public void RealizesTheActualLinkTextBoxRatherThanThePlaceholder()
    {
        var (window, model, _) = Open();
        var link = model.ContentEditors.OfType<LinkEditor>().Single();
        model.SelectedContent = link;
        Pump();

        var textBox = Assert.Single(ContentHost(window).GetVisualDescendants().OfType<TextBox>());
        Assert.Same(link, textBox.DataContext);

        link.Address = "example.org";
        Pump();
        Assert.Equal("example.org", textBox.Text);
    }

    [AvaloniaFact]
    public void FallsBackToThePlaceholderForAContentTypeWithNoSpecificTemplateYet()
    {
        // Proves the fallback path is actually reachable, not merely unreachable-and-therefore-
        // untested: Wi-Fi has no dedicated DataTemplate yet (Phase 2c), so it must resolve
        // through the ContentEditor catch-all rather than accidentally reusing PlainText's or
        // Link's TextBox template.
        var (window, model, _) = Open();
        var wifi = model.ContentEditors.OfType<WifiEditor>().Single();
        model.SelectedContent = wifi;
        Pump();

        var host = ContentHost(window);
        Assert.Empty(host.GetVisualDescendants().OfType<TextBox>());

        var placeholder = host.GetVisualDescendants().OfType<TextBlock>().Single(t =>
            t.Text == "The fields for this content type arrive in the next phase. Plain text and Link work today.");
        Assert.Same(wifi, placeholder.DataContext);
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
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
    }
}
