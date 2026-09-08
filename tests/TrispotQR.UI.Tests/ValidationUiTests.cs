using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Controls;
using TrispotQR.UI.Services;
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
        var marked = WithWindow((model, window) =>
        {
            var editor = model.ContentEditors.Single(e => e.Title == title);
            model.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            DispatcherPump.Drain();

            return Boxes(window)
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
        var shown = WithWindow((model, window) =>
        {
            var editor = model.ContentEditors.Single(e => e.Title == title);
            model.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            DispatcherPump.Drain();

            return Boxes(window)
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
        var (broken, fixedUp) = WithWindow((model, window) =>
        {
            var editor = model.ContentEditors.OfType<EmailEditor>().Single();
            model.SelectedContent = editor;

            editor.Address = "nonsense";
            DispatcherPump.Drain();
            var before = Boxes(window).Count(b => b.Classes.Contains(":error"));

            editor.Address = "someone@example.org";
            DispatcherPump.Drain();
            var after = Boxes(window).Count(b => b.Classes.Contains(":error"));

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
        var (marked, canExport) = WithWindow((model, window) =>
        {
            var wifi = model.ContentEditors.OfType<WifiEditor>().Single();
            model.SelectedContent = wifi;
            wifi.Ssid = "Guest";
            wifi.Security = WifiSecurity.Wep;
            wifi.Password = "abcdefg";
            model.RefreshNow();
            DispatcherPump.Drain();

            var box = Boxes(window).Single(b => b.FieldName == nameof(WifiEditor.Password));
            return (box.Classes.Contains(":warning"), model.CanExport);
        });

        Assert.True(marked, "the odd WEP key was not marked at all");
        Assert.True(canExport, "a warning blocked the save, which only an error should do");
    }

    /// <summary>
    /// The state the app opens in. Required fields report themselves straight away rather
    /// than waiting to be visited, which is a deliberate choice: the cost is that a pristine
    /// form is already marked, and this is where that shows up if it is ever reconsidered.
    ///
    /// No content type is selected here, deliberately: WithWindow hands out a fresh
    /// MainViewModel over a temporary settings directory, so this reproduces the real
    /// first-launch default (ContentEditors[0], the plain-text editor) rather than
    /// whatever content type this machine's own settings.json last left selected.
    /// </summary>
    [AvaloniaFact]
    public void TheOpeningForm_AlreadyShowsWhatIsMissing()
    {
        var shown = WithWindow((_, window) =>
        {
            DispatcherPump.Drain();
            return Boxes(window).Select(b => b.ShownError).ToList();
        });

        Assert.Equal(["Enter the text to put in the code."], shown);
    }

    /// <summary>
    /// The visible field boxes realised under the content editor host. Scoped to that one
    /// ContentControl rather than the whole window for the same reason MainWindowTests
    /// scopes its own searches there: nothing else in the window is a FieldBox, but scoping
    /// keeps this resilient to that changing. Filtered to IsVisible because a FieldBox can be
    /// in the tree but hidden -- the Wi-Fi editor's password box when Security is None -- and
    /// a hidden box's pseudo-classes are not something a user can see.
    /// </summary>
    private static IEnumerable<FieldBox> Boxes(Window window) =>
        ContentHost(window).GetVisualDescendants().OfType<FieldBox>().Where(b => b.IsVisible);

    private static ContentControl ContentHost(Window window) =>
        window.FindControl<ContentControl>("ContentEditorHost")
            ?? throw new InvalidOperationException("MainWindow no longer has a ContentEditorHost.");

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

    /// <summary>
    /// Builds the real window and hands its view model to <paramref name="work"/>, over a
    /// temporary preset and settings directory so a run never reads or overwrites the
    /// developer's real %APPDATA%\TrispotQR files, and so the content type this test opens on
    /// is always the fresh-install default rather than whatever a previous run of the app on
    /// this machine last left selected.
    ///
    /// Same shape as MainWindowTests.Open(): MainWindow is the composition root and builds
    /// its own real, %APPDATA%-backed MainViewModel in its constructor, so this replaces
    /// DataContext with a second MainViewModel built here against the temporary stores. The
    /// window's compiled XAML -- Resources and DataTemplates included -- stays exactly what
    /// ships, which is what makes this a real rendered tree rather than a stand-in for one.
    /// The window is deliberately never closed, for the same reason Open() never closes one:
    /// OnClosing calls SaveSession on the *real* model MainWindow built for itself (the one
    /// this replaces), which still points at %APPDATA%, not at this temporary directory.
    /// </summary>
    private static T WithWindow<T>(Func<MainViewModel, Window, T> work)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var window = new MainWindow();

            var model = new MainViewModel(
                new AvaloniaDialogService(window),
                new AvaloniaUiTimer(),
                new AvaloniaImageClipboard(window),
                new PresetStore(directory),
                new AppSettingsStore(directory));

            window.DataContext = model;
            window.Width = 1180;
            window.Height = 900;
            window.Show();
            DispatcherPump.Drain();

            return work(model, window);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
