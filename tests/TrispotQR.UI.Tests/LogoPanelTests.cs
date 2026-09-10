using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TrispotQR.Core.Export;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Converters;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The logo controls at the foot of the advanced options: the two buttons that choose and
/// remove an image, and the three controls that only make sense once there is one.
///
/// Every test here opens the advanced expander first. Its content is not in the visual tree at
/// all until it is, so a test that skipped that would search an empty panel and pass on nothing.
///
/// Choosing an image goes through <see cref="StubDialogs"/> rather than the real service. The
/// headless platform has no file picker, so the real AvaloniaDialogService can only ever report
/// a cancelled dialog, and the half of the panel that appears once a logo is chosen would be
/// unreachable. That the real service asks the platform without deadlocking is proved separately
/// in <see cref="DialogServiceTests"/>.
/// </summary>
public class LogoPanelTests : IDisposable
{
    private readonly string _directory;

    public LogoPanelTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-logo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"MainWindow has no {name}.");

    /// <summary>
    /// Opens the advanced section and waits for it to lay itself out, so what follows reads
    /// controls that are really on screen rather than ones that were never realised.
    /// </summary>
    private static void OpenAdvanced(UiHarness.Session session)
    {
        Named<Expander>(session.Window, "AdvancedOptions").IsExpanded = true;

        Assert.True(
            DispatcherPump.DrainUntil(() => Named<Button>(session.Window, "ChooseLogoButton").Bounds.Width > 0),
            "the advanced options never laid themselves out, so the logo panel was never realised");
    }

    /// <summary>
    /// Content, drawn. ShowRaiseEcc is re-raised when a render finishes rather than from the Ecc
    /// setter, so nothing bound to it moves until there is something to render -- which in the
    /// running app there always is by the time anyone reaches the advanced options.
    /// </summary>
    private static void GiveItSomethingToDraw(UiHarness.Session session)
    {
        var editor = session.Model.ContentEditors.OfType<PlainTextEditor>().Single();
        session.Model.SelectedContent = editor;
        editor.Text = "https://www.emanuelnyc.org";

        // RefreshNow skips the debounce the app uses in normal running; waiting out a real tick
        // would make this slow for no gain.
        session.Model.RefreshNow();
        DispatcherPump.Drain();
    }

    /// <summary>
    /// A real, readable image file on disk. What is drawn in it does not matter -- only that
    /// TrispotQR.Core.Validation.ImageSize.Read can open it, since a file it cannot read is
    /// refused by ChooseLogo and would leave the panel exactly as it was.
    /// </summary>
    private string WriteTestLogo()
    {
        var path = Path.Combine(_directory, "logo.png");

        var encoded = QrEncoder.Encode("logo", EccLevel.Medium);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, QrStyle.Default);
        PngExporter.Save(SkiaRasterizer.Render(drawing, 128), path);

        return path;
    }

    [AvaloniaFact]
    public void TheChooseImageButtonReachesTheCommandThatOpensThePicker()
    {
        // Not executed: the end-to-end path needs a file picker the headless platform does not
        // have. What is provable here is that the button reaches the command at all, which is
        // the part that was missing before this task -- the Avalonia window offered no way in.
        UiHarness.WithWindow(session =>
        {
            OpenAdvanced(session);

            var button = Named<Button>(session.Window, "ChooseLogoButton");

            Assert.Same(session.Model.ChooseLogoCommand, button.Command);
            Assert.True(button.IsEffectivelyEnabled);
        });
    }

    [AvaloniaFact]
    public void TheLogoSettingsAppearOnlyOnceAnImageHasBeenChosen()
    {
        // Size, shape and the error-correction prompt are meaningless with no logo in the code,
        // and three dead controls sitting under a "None chosen" label is what the WPF window
        // avoids by collapsing the whole group.
        var dialogs = new StubDialogs();

        UiHarness.WithWindow(
            session =>
            {
                OpenAdvanced(session);
                var detail = Named<StackPanel>(session.Window, "LogoDetail");
                var remove = Named<Button>(session.Window, "ClearLogoButton");

                Assert.False(session.Model.HasLogo);
                Assert.False(detail.IsVisible, "the logo settings are on screen before there is a logo");
                Assert.False(remove.IsEffectivelyEnabled, "there is nothing to remove and Remove is live");

                dialogs.NextImage = WriteTestLogo();
                session.Model.ChooseLogoCommand.Execute(null);
                DispatcherPump.Drain();

                Assert.True(session.Model.HasLogo);
                Assert.True(detail.IsVisible, "a logo was chosen and its settings never appeared");
                Assert.True(remove.IsEffectivelyEnabled, "there is a logo and no way to take it off");

                // The other direction too, driven through the button rather than the model: a
                // one-way binding evaluated once at startup would satisfy the assertion above,
                // and a Remove button wired to nothing would never be noticed at all.
                Assert.Same(session.Model.ClearLogoCommand, remove.Command);
                remove.Command!.Execute(null);
                DispatcherPump.Drain();

                Assert.False(detail.IsVisible, "the logo was removed and its settings stayed on screen");
            },
            dialogs);
    }

    [AvaloniaFact]
    public void TheLogoSizeSliderCoversTheRangeTheRendererWasTestedAgainst()
    {
        // 0.05 to 0.40. Below that the logo is invisible; above it the scannability check starts
        // refusing the code, so a slider that went further would offer sizes nothing can use.
        var dialogs = new StubDialogs();

        UiHarness.WithWindow(
            session =>
            {
                OpenAdvanced(session);

                dialogs.NextImage = WriteTestLogo();
                session.Model.ChooseLogoCommand.Execute(null);
                DispatcherPump.Drain();

                var slider = Named<Slider>(session.Window, "LogoSizeSlider");

                Assert.Equal(0.05, slider.Minimum, 3);
                Assert.Equal(0.40, slider.Maximum, 3);

                // The range is only the logo's range if the slider is actually the logo's. A
                // slider bound to something else, or to nothing, would satisfy the two above.
                Assert.Equal(session.Model.LogoSize, slider.Value, 3);

                slider.Value = 0.3;
                DispatcherPump.Drain();

                Assert.Equal(0.3, session.Model.LogoSize, 3);
            },
            dialogs);
    }

    [AvaloniaFact]
    public void TheShapeBoxOffersEveryPunchShapeAndTheChoiceReachesTheCode()
    {
        var dialogs = new StubDialogs();

        UiHarness.WithWindow(
            session =>
            {
                OpenAdvanced(session);

                dialogs.NextImage = WriteTestLogo();
                session.Model.ChooseLogoCommand.Execute(null);
                DispatcherPump.Drain();

                var box = Named<ComboBox>(session.Window, "LogoPunchShapeBox");

                Assert.Same(session.Model.LogoPunchShapes, box.ItemsSource);
                Assert.Equal(session.Model.LogoPunchShape, box.SelectedItem);

                // A shape that is not the one already in effect, so the assertion below cannot
                // pass on a box that changed nothing.
                var wanted = session.Model.LogoPunchShapes.First(s => s != session.Model.LogoPunchShape);
                box.SelectedItem = wanted;
                DispatcherPump.Drain();

                Assert.Equal(wanted, session.Model.LogoPunchShape);
            },
            dialogs);
    }

    [AvaloniaFact]
    public void TheRaiseErrorCorrectionButtonOffersItselfOnlyWhileTheCodeCouldBeStronger()
    {
        // Choosing a logo already raises error correction to EccLevel.High, the strongest level
        // there is, so the prompt starts hidden and only comes back if the user lowers it again
        // -- which is exactly when a code with a hole punched in the middle of it needs
        // something said about it.
        var dialogs = new StubDialogs();

        UiHarness.WithWindow(
            session =>
            {
                GiveItSomethingToDraw(session);
                OpenAdvanced(session);

                dialogs.NextImage = WriteTestLogo();
                session.Model.ChooseLogoCommand.Execute(null);
                DispatcherPump.Drain();

                var button = Named<Button>(session.Window, "RaiseEccButton");

                Assert.False(session.Model.ShowRaiseEcc);
                Assert.False(button.IsVisible, "the code is already at the top level and the prompt is still up");

                session.Model.Ecc = EccLevel.Medium;
                session.Model.RefreshNow();
                DispatcherPump.Drain();

                Assert.True(session.Model.ShowRaiseEcc);
                Assert.True(button.IsVisible, "error correction was lowered under a logo and nothing offered to raise it");
                Assert.Same(session.Model.RaiseEccCommand, button.Command);
            },
            dialogs);
    }

    [AvaloniaFact]
    public void TheRaiseErrorCorrectionButtonNamesTheLevelTheDropdownCallsIt()
    {
        // These diverged once and it made the button read as broken. RaiseEccCommand sets
        // EccLevel.High, which the "Error correction" dropdown a few controls above labels
        // "Highest (needed for logos)" -- while giving the name "High" to the *lower* Quartile.
        // A button saying "raise it to High" therefore sent anyone who followed its wording to
        // Quartile, after which ShowRaiseEcc was still true and the prompt was still on screen.
        //
        // Asserted against the converter rather than a literal, so the button follows the app's
        // vocabulary if the wording is ever revised rather than pinning today's words twice.
        UiHarness.WithWindow(session =>
        {
            OpenAdvanced(session);

            var label = new FriendlyNameConverter()
                .Convert(EccLevel.High, typeof(string), null, CultureInfo.InvariantCulture);
            var word = Assert.IsType<string>(label).Split(' ')[0];

            var content = Assert.IsType<string>(Named<Button>(session.Window, "RaiseEccButton").Content);

            Assert.EndsWith(word, content, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Answers the one question these tests ask of the outside world and nothing else. Every
    /// other member throws rather than returning a polite default: a command that reached one
    /// would be doing something no test here means it to, and a silent stub would hide that.
    /// </summary>
    private sealed class StubDialogs : IDialogService
    {
        public string? NextImage { get; set; }

        public string? AskForImage(string? directory) => NextImage;

        public string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory) =>
            throw new InvalidOperationException("no logo test saves a file");

        public string? AskForText(string title, string prompt, string initialValue) =>
            throw new InvalidOperationException("no logo test asks for text");

        public bool Confirm(string title, string message) =>
            throw new InvalidOperationException("no logo test confirms anything");

        public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) =>
            throw new InvalidOperationException("no logo test confirms a risk");

        public void ShowError(string title, string message) =>
            throw new InvalidOperationException($"the logo panel reported an error it should not have: {message}");

        public void ShowInformation(string title, string message) =>
            throw new InvalidOperationException($"the logo panel said something it should not have: {message}");

        public AppSettings? EditSettings(AppSettings current) =>
            throw new InvalidOperationException("no logo test opens the settings window");
    }
}
