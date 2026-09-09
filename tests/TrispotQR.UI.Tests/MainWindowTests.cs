using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Payloads;
using TrispotQR.UI;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

public class MainWindowTests
{
    private static Button FindButton(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));

    /// <summary>
    /// The form-level message TextBlock, named in MainWindow.axaml for exactly this: a test
    /// needs to reach this one TextBlock specifically, not merely the one that happens to hold
    /// a given piece of text at the moment it runs.
    /// </summary>
    private static TextBlock FormMessage(Window window) =>
        window.FindControl<TextBlock>("FormMessageText")
            ?? throw new InvalidOperationException("MainWindow no longer has a FormMessageText.");

    private static T Editor<T>(MainViewModel model) where T : ContentEditor
    {
        var editor = model.ContentEditors.OfType<T>().Single();
        model.SelectedContent = editor;
        DispatcherPump.Drain();
        return editor;
    }

    [AvaloniaFact]
    public void OpensAtTheSizeTheLastSessionLeftBehind()
    {
        // OnClosing has always written Width and Height into settings.json; nothing read them
        // back, so every launch reverted to the 1000x700 hardcoded in MainWindow.axaml. The
        // WPF app restores them from the same file, and the two share it, so a user switching
        // between them saw the Avalonia one silently discard the size the other kept.
        //
        // Asserted against the restored model's LoadedSettings rather than a literal because
        // MainWindow is the composition root and builds the real settings store: there is no
        // fixture to point it at a temporary file without changing MainViewModel. The XAML's
        // 1000x700 is not the stored default (1180x800), so a regression that dropped the
        // restore shows up here on any machine that has never run the app, CI included. The
        // harness records the size before resizing the window for layout, which is why this
        // reads RestoredSize rather than the window's current one.
        UiHarness.WithWindow(session =>
        {
            var settings = session.RestoredModel.LoadedSettings;

            Assert.Equal(settings.WindowWidth, session.RestoredSize.Width);
            Assert.Equal(settings.WindowHeight, session.RestoredSize.Height);
        });
    }

    [AvaloniaFact]
    public void ShowsAPreviewOnceThereIsContent()
    {
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);
            editor.Text = "https://www.emanuelnyc.org";

            // RefreshNow skips the debounce timer, which is what the window uses in normal
            // running. Waiting on a real 150ms tick here would make the test slow and flaky for
            // no gain.
            session.Model.RefreshNow();
            DispatcherPump.Drain();

            Assert.NotNull(session.Model.PreviewDrawing);

            var preview = session.Window.GetVisualDescendants().OfType<QrPreview>().Single();
            Assert.NotNull(preview.Drawing);
        });
    }

    [AvaloniaFact]
    public void CannotExportWithNoContent()
    {
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);
            editor.Text = string.Empty;
            session.Model.RefreshNow();
            DispatcherPump.Drain();

            Assert.False(session.Model.CanExport);
            Assert.False(session.Model.SavePngCommand.CanExecute(null));

            // Through the actual bound control too, not just the view model. A Button wired to
            // a Command tracks CanExecute through IsEffectivelyEnabled (and the ":disabled"
            // pseudoclass) rather than through the plain IsEnabled property -- confirmed
            // empirically: IsEnabled stayed true here even with CanExecute false, since nothing
            // in this window sets IsEnabled directly. IsEffectivelyEnabled is what actually
            // gates whether a click reaches the command and what the disabled visual state
            // reflects, so it is the one worth asserting.
            Assert.False(FindButton(session.Window, "Save PNG").IsEffectivelyEnabled);
        });
    }

    [AvaloniaFact]
    public void CanExportOnceThereIsContent()
    {
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);
            editor.Text = "https://www.emanuelnyc.org";
            session.Model.RefreshNow();
            DispatcherPump.Drain();

            Assert.True(session.Model.CanExport);
            Assert.True(session.Model.SavePngCommand.CanExecute(null));
            Assert.True(FindButton(session.Window, "Save PNG").IsEffectivelyEnabled);
        });
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
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);

            var box = Assert.Single(UiHarness.AllBoxes(session.Window));

            // The realised child's DataContext is the bound Content instance itself (standard
            // ContentPresenter behaviour), so this also confirms it is bound to this editor and
            // not some other control that merely happens to be the only FieldBox in the host.
            Assert.Same(editor, box.DataContext);

            // A live round-trip through the real bound property, not just a type check: if some
            // other template had won, there would be no FieldBox here to receive this at all.
            editor.Text = "round-trip";
            DispatcherPump.Drain();
            Assert.Equal("round-trip", box.Text);
        });
    }

    [AvaloniaFact]
    public void RealizesTheLinkEditorsOwnBoxRatherThanAnyOtherTemplate()
    {
        // PlainTextEditor and LinkEditor are the two templates ordered first in
        // MainWindow.axaml's DataTemplates; this and the test above are what would catch either
        // one being shadowed by a template registered ahead of it.
        UiHarness.WithWindow(session =>
        {
            var link = Editor<LinkEditor>(session.Model);

            var box = Assert.Single(UiHarness.AllBoxes(session.Window));
            Assert.Same(link, box.DataContext);

            link.Address = "example.org";
            DispatcherPump.Drain();
            Assert.Equal("example.org", box.Text);
        });
    }

    [AvaloniaFact]
    public void EveryContentTypeRealizesRealFieldsRatherThanAPlaceholder()
    {
        // Iterating the collection rather than naming the seven types: a content type added
        // later fails here instead of quietly rendering an empty panel.
        UiHarness.WithWindow(session =>
        {
            foreach (var editor in session.Model.ContentEditors)
            {
                session.Model.SelectedContent = editor;
                DispatcherPump.Drain();

                var boxes = UiHarness.AllBoxes(session.Window).ToList();
                Assert.True(boxes.Count > 0, $"{editor.Title} rendered no fields");
                Assert.All(boxes, b => Assert.False(string.IsNullOrEmpty(b.FieldName)));
            }
        });
    }

    /// <summary>
    /// What every field in every form is actually called, and which ones are optional.
    ///
    /// The wording is taken from the WPF original (src/TrispotQR.App/MainWindow.xaml), which is
    /// the shipping app and therefore the source of truth for it. Until this existed, the
    /// fifteen labelled call sites in the Avalonia window were checked only for a non-empty
    /// FieldName: swapping "First name" and "Last name", or dropping IsOptional from an
    /// optional field, compiled and passed. Label, IsOptional and InputHeight were asserted
    /// only on synthetic boxes built by FieldBoxTests, which cannot see a call site at all.
    ///
    /// Ordered by FieldName on both sides rather than by position in the tree, so a layout
    /// change that reorders the fields does not fail this. A caption swapped between two boxes
    /// still fails, because it is the FieldName-to-caption pairing that changes.
    /// </summary>
    private static readonly Dictionary<string, (string Field, string Label, bool Optional, double Height)[]> Captions =
        new()
        {
            ["Plain text"] = [("Text", "", false, 92)],
            ["Link"] = [("Address", "Web address", false, 0)],
            ["Wi-Fi"] = [("Password", "Password", false, 0), ("Ssid", "Network name", false, 0)],
            ["Email"] =
            [
                ("Address", "To", false, 0),
                ("Body", "Message", true, 60),
                ("Subject", "Subject", true, 0),
            ],
            ["Phone"] = [("Number", "Phone number", false, 0)],
            ["Text message"] = [("Number", "Phone number", false, 0), ("Text", "Message", true, 60)],
            ["Contact card"] =
            [
                ("Email", "Email", true, 0),
                ("FirstName", "First name", false, 0),
                ("JobTitle", "Job title", true, 0),
                ("LastName", "Last name", false, 0),
                ("Organization", "Organisation", false, 0),
                ("Phone", "Phone", true, 0),
                ("Website", "Website", true, 0),
            ],
        };

    [AvaloniaFact]
    public void EveryFieldCarriesTheCaptionTheShippingAppGivesIt()
    {
        UiHarness.WithWindow(session =>
        {
            // Every content type in one window rather than seven, and driven off the live
            // collection rather than the table's own keys, so a content type added later
            // reports a missing row here instead of quietly going uncovered.
            foreach (var editor in session.Model.ContentEditors)
            {
                session.Model.SelectedContent = editor;
                DispatcherPump.Drain();

                Assert.True(Captions.ContainsKey(editor.Title), $"{editor.Title} has no expected captions.");

                // Hidden boxes included: the Wi-Fi password box is a real, labelled field that
                // happens to be collapsed for an open network, and its caption still has to be
                // right for the moment it appears.
                var actual = UiHarness.AllBoxes(session.Window)
                    .Select(b => (Field: b.FieldName, b.Label, Optional: b.IsOptional, Height: b.InputHeight))
                    .OrderBy(f => f.Field, StringComparer.Ordinal)
                    .ToArray();

                Assert.Equal(Captions[editor.Title].OrderBy(f => f.Field, StringComparer.Ordinal), actual);
            }
        });
    }

    [AvaloniaFact]
    public void NoFieldCarriesSpacingOfItsOwn()
    {
        // The gap between fields belongs to the container that arranges them -- StackPanel
        // Spacing, Grid RowSpacing -- not to each box. Nine of the seventeen call sites used to
        // hand-write Margin="0,10,0,0", which is precisely the kind of treatment FieldBox
        // exists to own: all nine happened to be correct, and the tenth, added later by someone
        // who did not know to look, would not have been. This is what makes that impossible to
        // get wrong rather than merely currently right.
        UiHarness.WithWindow(session =>
        {
            foreach (var editor in session.Model.ContentEditors)
            {
                session.Model.SelectedContent = editor;
                DispatcherPump.Drain();

                foreach (var box in UiHarness.AllBoxes(session.Window))
                {
                    Assert.True(
                        box.Margin == default,
                        $"{editor.Title}/{box.FieldName} sets its own Margin ({box.Margin}); "
                            + "spacing belongs to the container.");
                }
            }
        });
    }

    [AvaloniaFact]
    public void TypingIntoAFieldReachesTheEditorBehindIt()
    {
        // The one thing sixty-odd UI tests could not tell you. Everything else in this suite
        // writes to the view model and reads the rendered tree, which is the opposite
        // direction: if FieldBox's inner TextBox were bound one-way, typing in any of the
        // seventeen boxes would do nothing at all and every one of those tests would stay
        // green. Confirmed by making that binding one-way and watching only this test fail.
        //
        // Real key input rather than assigning TextBox.Text, so the whole path is exercised:
        // focus, the headless input backend, TextInput, and the binding back out.
        UiHarness.WithWindow(session =>
        {
            var link = Editor<LinkEditor>(session.Model);
            var input = UiHarness.Input(Assert.Single(UiHarness.AllBoxes(session.Window)));

            input.Focus();
            DispatcherPump.Drain();
            input.Clear();

            session.Window.KeyTextInput("emanuelnyc.org");
            DispatcherPump.Drain();

            Assert.Equal("emanuelnyc.org", input.Text);
            Assert.Equal("emanuelnyc.org", link.Address);
        });
    }

    [AvaloniaFact]
    public void TheWifiSecurityDropdownSaysWhatEachOptionMeans()
    {
        // The dropdown holds enum values, and without an ItemTemplate a ComboBox renders them
        // by ToString(): "Wpa", "Wep", "None". The chosen wording is doing work -- "WEP (old)"
        // is telling the user their network is on an obsolete standard, and "No password" says
        // what None actually means for them -- and the WPF build has always said it, so the two
        // must not disagree about what a setting is called.
        //
        // Asserted on the text the ComboBox actually renders, not on the converter in
        // isolation: a converter that was written but never wired up passes an isolated test
        // and still ships the raw identifiers.
        UiHarness.WithWindow(session =>
        {
            var wifi = Editor<WifiEditor>(session.Model);
            var combo = UiHarness.ContentHost(session.Window)
                .GetVisualDescendants().OfType<ComboBox>().Single();

            var shown = new List<string?>();
            foreach (var option in wifi.SecurityOptions)
            {
                wifi.Security = option;
                DispatcherPump.Drain();

                // The selection box renders through the same ItemTemplate the dropdown list
                // does, so reading it covers all three without opening a popup into a separate
                // visual root the window-scoped search cannot reach.
                shown.Add(combo.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text)
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t)));
            }

            Assert.Equal(["WPA / WPA2", "WEP (old)", "No password"], shown);
        });
    }

    [AvaloniaFact]
    public void TheWifiPasswordBoxDisappearsForAnOpenNetwork()
    {
        // An open network has no password to type, so asking for one would be nonsense. The
        // IsVisible="{Binding NeedsPassword}" binding that hides it had no coverage at all:
        // deleting it left every test green while the app asked people to key in a password
        // that goes nowhere.
        UiHarness.WithWindow(session =>
        {
            var wifi = Editor<WifiEditor>(session.Model);

            var whenSecured = UiHarness.VisibleBoxes(session.Window).Select(b => b.FieldName).ToList();

            wifi.Security = WifiSecurity.None;
            DispatcherPump.Drain();
            var whenOpen = UiHarness.VisibleBoxes(session.Window).Select(b => b.FieldName).ToList();

            Assert.Equal(["Ssid", "Password"], whenSecured);
            Assert.Equal(["Ssid"], whenOpen);
        });
    }

    [AvaloniaFact]
    public void AnInformationalNoteIsNotShownAsAProblem()
    {
        // The link editor's note is guidance, not a fault. Colouring it red would tell the
        // user something is wrong when nothing is.
        UiHarness.WithWindow(session =>
        {
            var link = Editor<LinkEditor>(session.Model);
            link.Address = "www.emanuelnyc.org";
            DispatcherPump.Drain();

            Assert.False(link.FormMessageIsProblem);
            Assert.False(FormMessage(session.Window).Classes.Contains("problem"));
        });
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
        UiHarness.WithWindow(session =>
        {
            var contact = Editor<ContactEditor>(session.Model);

            Assert.True(contact.FormMessageIsProblem);
            var message = FormMessage(session.Window);
            Assert.True(message.Classes.Contains("problem"));

            Avalonia.Application.Current!.TryGetResource(
                "DangerBrush", Avalonia.Styling.ThemeVariant.Default, out var expected);
            var actual = message.Foreground as Avalonia.Media.ISolidColorBrush;
            Assert.NotNull(actual);
            Assert.Equal(((Avalonia.Media.ISolidColorBrush)expected!).Color, actual!.Color);
        });
    }

    [AvaloniaFact]
    public void TheFormMessageIsHiddenWhenThereIsNothingToSay()
    {
        // PlainTextEditor never sets a Note and never reports a Form-level issue, so with the
        // default empty Text its FormMessage is null. A binding that fails open -- rendering an
        // empty line instead of collapsing it -- would look, to a user, like nothing at all and
        // pass unnoticed; this is what StringConverters.IsNotNullOrEmpty is there to prevent.
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);

            Assert.True(string.IsNullOrEmpty(editor.FormMessage));
            Assert.False(FormMessage(session.Window).IsVisible);
        });
    }

    [AvaloniaFact]
    public void TheWindowLaysOutWithoutThrowing()
    {
        // NOT a catch-all for binding or template-selection mistakes: Avalonia logs a mismatched
        // DataTemplate or a failed binding rather than throwing, so this would not have caught,
        // for example, the base-type-template-shadows-a-derived-one regression that
        // RealizesThePlainTextEditorsOwnBoxRatherThanAnyOtherTemplate exists to catch
        // (confirmed: that regression left this test green). What this does prove is that a full
        // render and layout pass over the real window -- not just a constructed-but-never-shown
        // one -- completes without an exception, which is a real, if narrower, guarantee.
        UiHarness.WithWindow(session =>
        {
            var editor = Editor<PlainTextEditor>(session.Model);
            editor.Text = "test";
            session.Model.RefreshNow();
            DispatcherPump.Drain();

            Assert.NotNull(session.Window.CaptureRenderedFrame());
        });
    }
}
