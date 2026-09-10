using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The About window, and the gear menu that is the only way to reach it or the settings.
///
/// Nothing here asserts a version number. AppInfo reads the entry assembly, which under this
/// runner is the test host rather than TrispotQR.Desktop, so the number it reports is the
/// runner's. What can be asserted is the shape of it and what the app does with it, and those
/// are the two things a wrong version would actually break.
/// </summary>
public class AboutWindowTests
{
    [AvaloniaFact]
    public void TheProductNameIsWhatTheAppIsCalledEverywhereElse()
    {
        // Spelled with the space, and capitalised the way the window title and the WPF build
        // both say it. The About window and the title bar read this rather than each carrying
        // their own copy, so a change here is a change in both.
        Assert.Equal("Trispot QR", AppInfo.ProductName);
    }

    [AvaloniaFact]
    public void TheVersionReadsAsThreeNumbers()
    {
        // A shape, not a value. InformationalVersion can carry build metadata after a '+',
        // and a four part AssemblyVersion would otherwise reach the window as "1.0.0.0";
        // either would be shown to the user exactly as read.
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppInfo.Version);
    }

    [AvaloniaFact]
    public void TheDisplayVersionIsTheVersionWithAVInFrontOfIt()
    {
        Assert.Equal($"v{AppInfo.Version}", AppInfo.DisplayVersion);
    }

    [AvaloniaFact]
    public void TheAboutWindowShowsTheProductNameAndTheVersion()
    {
        // Shown non-modally and driven directly. ShowDialog would block on a dispatcher frame
        // with nothing in the suite able to dismiss it, and hang the whole run.
        var window = new AboutWindow();
        window.Show();
        DispatcherPump.Drain();

        try
        {
            var texts = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text)
                .ToList();

            Assert.Contains(AppInfo.ProductName, texts);
            Assert.Contains(AppInfo.DisplayVersion, texts);
        }
        finally
        {
            window.Close();
            DispatcherPump.Drain();
        }
    }

    [AvaloniaFact]
    public void TheGearMenuOffersSettingsAndAboutAndSettingsReallyOpensThem()
    {
        var dialogs = new RecordingDialogs();

        UiHarness.WithWindow(
            session =>
            {
                var gear = session.Window.FindControl<Button>("GearButton")
                    ?? throw new InvalidOperationException("MainWindow no longer has a GearButton.");

                // A real click at the rendered button, which is the whole gesture under test:
                // the gear has no Click handler of its own, and opening on a plain left press
                // is Button's own behaviour with a Flyout attached.
                ClickInWindow(session.Window, gear, "GearButton");

                // Menu items are not realised until the menu is open, so this search finds
                // nothing at all if the click did not open it.
                var items = session.Window.GetVisualDescendants().OfType<MenuItem>().ToList();
                var headers = items.Select(i => i.Header as string).ToList();

                Assert.Contains(headers, h => h is not null && h.StartsWith("Settings", StringComparison.Ordinal));
                Assert.Contains(headers, h => h is not null && h.Contains("About", StringComparison.Ordinal));

                var settings = items.Single(i => (i.Header as string)?.StartsWith("Settings", StringComparison.Ordinal) == true);
                ClickInWindow(session.Window, settings, "the Settings menu item");

                // The item being there proves nothing on its own. This is the settings window
                // being asked for, through the same service the view model always reaches it by.
                Assert.Equal(1, dialogs.EditSettingsCalls);
            },
            dialogs);
    }

    /// <summary>
    /// A real click at the rendered control, having first checked it is somewhere a click can
    /// land: a control laid out past the edge of the window swallows the gesture silently
    /// rather than failing.
    /// </summary>
    private static void ClickInWindow(Window window, Control control, string what)
    {
        var point = UiHarness.At(control, 0.5, 0.5);

        Assert.True(
            point.X >= 0 && point.Y >= 0 && point.X < window.Bounds.Width && point.Y < window.Bounds.Height,
            $"{what} is laid out outside the window, where a click goes nowhere");

        UiHarness.Click(window, point);
    }

    /// <summary>
    /// Counts the settings window being asked for without opening one. Returning null is what
    /// a cancelled settings window returns, so the view model takes no further action.
    /// </summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public int EditSettingsCalls { get; private set; }

        public AppSettings? EditSettings(AppSettings current)
        {
            EditSettingsCalls++;
            return null;
        }

        public string? AskForImage(string? directory) =>
            throw new InvalidOperationException("no gear menu test chooses an image");

        public string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory) =>
            throw new InvalidOperationException("no gear menu test saves a file");

        public string? AskForText(string title, string prompt, string initialValue) =>
            throw new InvalidOperationException("no gear menu test asks for text");

        public bool Confirm(string title, string message) =>
            throw new InvalidOperationException("no gear menu test confirms anything");

        public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) =>
            throw new InvalidOperationException("no gear menu test confirms a risk");

        public void ShowError(string title, string message) =>
            throw new InvalidOperationException($"the gear menu reported an error it should not have: {message}");

        public void ShowInformation(string title, string message) =>
            throw new InvalidOperationException($"the gear menu said something it should not have: {message}");
    }
}
