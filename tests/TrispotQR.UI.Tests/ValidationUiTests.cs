using Avalonia.Headless.XUnit;
using TrispotQR.Core.Payloads;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The red highlighting, end to end through the real window.
///
/// TrispotQR.ViewModels.Tests.EditorValidationTests proves the editors know what is wrong.
/// What is left, and what only a rendered tree can answer, is whether that reaches the
/// screen: whether the box at fault is the one that turns red, and whether the message is
/// legible once it does.
///
/// Ported from TrispotQR.Tests.ValidationUiTests, minus the two theme-contrast cases
/// (TheMessage_IsReadableInBothThemes, AFailedForm_Renders): Avalonia has no theming until
/// Phase 2e, and asserting one theme while calling it two would be worse than leaving the
/// gap open. Those stay in the WPF suite.
/// </summary>
public class ValidationUiTests
{
    /// <summary>
    /// A deliberately wrong value for each content type, paired with the field that should
    /// be blamed for it. Every type is here so a new one cannot be added without deciding
    /// what its bad input looks like. Copied verbatim from the WPF suite: this table encodes
    /// real knowledge about the validation rules, not anything specific to WPF.
    /// </summary>
    public static TheoryData<string, string> BadInput => new()
    {
        { "Link", nameof(LinkEditor.Address) },
        { "Wi-Fi", nameof(WifiEditor.Password) },
        { "Email", nameof(EmailEditor.Address) },
        { "Phone", nameof(PhoneEditor.Number) },
        { "Text message", nameof(SmsEditor.Number) },
        { "Contact card", nameof(ContactEditor.Email) },
    };

    [AvaloniaTheory]
    [MemberData(nameof(BadInput))]
    public void TheFieldAtFault_IsTheOneMarked(string title, string expectedField)
    {
        var marked = UiHarness.WithWindow(session =>
        {
            var editor = session.Model.ContentEditors.Single(e => e.Title == title);
            session.Model.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            DispatcherPump.Drain();

            return UiHarness.VisibleBoxes(session.Window)
                .Where(b => b.Classes.Contains(":error"))
                .Select(b => b.FieldName)
                .ToList();
        });

        Assert.Equal([expectedField], marked);
    }

    [AvaloniaTheory]
    [MemberData(nameof(BadInput))]
    public void TheMessage_AppearsUnderThatField(string title, string expectedField)
    {
        var shown = UiHarness.WithWindow(session =>
        {
            var editor = session.Model.ContentEditors.Single(e => e.Title == title);
            session.Model.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            DispatcherPump.Drain();

            return UiHarness.VisibleBoxes(session.Window)
                .Where(b => b.ShownError is not null)
                .Select(b => (b.FieldName, b.ShownError))
                .ToList();
        });

        var one = Assert.Single(shown);
        Assert.Equal(expectedField, one.FieldName);
        Assert.False(string.IsNullOrWhiteSpace(one.ShownError));
    }

    /// <summary>
    /// Correcting the input has to clear the marking. A red border that never goes away is
    /// worse than none, because it stops meaning anything.
    /// </summary>
    [AvaloniaFact]
    public void FixingTheInput_ClearsTheMarking()
    {
        var (broken, fixedUp) = UiHarness.WithWindow(session =>
        {
            var editor = session.Model.ContentEditors.OfType<EmailEditor>().Single();
            session.Model.SelectedContent = editor;

            editor.Address = "nonsense";
            DispatcherPump.Drain();
            var before = UiHarness.VisibleBoxes(session.Window).Count(b => b.Classes.Contains(":error"));

            editor.Address = "someone@example.org";
            DispatcherPump.Drain();
            var after = UiHarness.VisibleBoxes(session.Window).Count(b => b.Classes.Contains(":error"));

            return (before, after);
        });

        Assert.Equal(1, broken);
        Assert.Equal(0, fixedUp);
    }

    /// <summary>
    /// A warning marks the field but must not block the save, which is the whole difference
    /// between the two severities.
    /// </summary>
    [AvaloniaFact]
    public void AWarning_MarksTheFieldWithoutBlockingTheSave()
    {
        var (marked, canExport) = UiHarness.WithWindow(session =>
        {
            var wifi = session.Model.ContentEditors.OfType<WifiEditor>().Single();
            session.Model.SelectedContent = wifi;
            wifi.Ssid = "Guest";
            wifi.Security = WifiSecurity.Wep;
            wifi.Password = "abcdefg";
            session.Model.RefreshNow();
            DispatcherPump.Drain();

            var box = UiHarness.VisibleBoxes(session.Window)
                .Single(b => b.FieldName == nameof(WifiEditor.Password));
            return (box.Classes.Contains(":warning"), session.Model.CanExport);
        });

        Assert.True(marked, "the odd WEP key was not marked at all");
        Assert.True(canExport, "a warning blocked the save, which only an error should do");
    }

    /// <summary>
    /// The state the app opens in. Required fields report themselves straight away rather
    /// than waiting to be visited, which is a deliberate choice: the cost is that a pristine
    /// form is already marked, and this is where that shows up if it is ever reconsidered.
    ///
    /// No content type is selected here, deliberately: the harness hands out a fresh
    /// MainViewModel over a temporary settings directory, so this reproduces the real
    /// first-launch default (ContentEditors[0], the plain-text editor) rather than
    /// whatever content type this machine's own settings.json last left selected.
    /// </summary>
    [AvaloniaFact]
    public void TheOpeningForm_AlreadyShowsWhatIsMissing()
    {
        var shown = UiHarness.WithWindow(session =>
        {
            DispatcherPump.Drain();
            return UiHarness.VisibleBoxes(session.Window).Select(b => b.ShownError).ToList();
        });

        Assert.Equal(["Enter the text to put in the code."], shown);
    }

    /// <summary>Gives the content type enough valid input that only the broken field is wrong.</summary>
    private static void Fill(ContentEditor editor)
    {
        switch (editor)
        {
            case LinkEditor link:
                link.Address = "example.org";
                break;
            case WifiEditor wifi:
                wifi.Ssid = "Guest";
                wifi.Password = "longenough";
                break;
            case EmailEditor email:
                email.Address = "someone@example.org";
                break;
            case PhoneEditor phone:
                phone.Number = "212 555 0134";
                break;
            case SmsEditor sms:
                sms.Number = "212 555 0134";
                break;
            case ContactEditor contact:
                contact.FirstName = "Alex";
                break;
        }
    }

    /// <summary>Breaks exactly one field, the one each case expects to be blamed.</summary>
    private static void Break(ContentEditor editor)
    {
        switch (editor)
        {
            case LinkEditor link:
                link.Address = "not a website";
                break;
            case WifiEditor wifi:
                wifi.Password = "short";
                break;
            case EmailEditor email:
                email.Address = "nonsense";
                break;
            case PhoneEditor phone:
                phone.Number = "212 555 CALL";
                break;
            case SmsEditor sms:
                sms.Number = "abc";
                break;
            case ContactEditor contact:
                contact.Email = "not-an-address";
                break;
        }
    }
}
