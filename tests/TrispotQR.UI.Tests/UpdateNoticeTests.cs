using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Updates;
using TrispotQR.UI.Services;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// Whether a release that was found actually reaches the screen. The check is tested elsewhere;
/// this is the wiring, which fails silently: a working check and a notice nobody sees look the
/// same from outside.
///
/// Every updater here reports an update. A check that finds nothing opens a real modal message,
/// which nothing in a headless run can dismiss; that path is covered in MainViewModelTests.
/// </summary>
public class UpdateNoticeTests
{
    private static T Named<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"MainWindow has no {name}.");

    [AvaloniaFact]
    public void TheNoticeIsHiddenUntilThereIsSomethingToSay()
    {
        UiHarness.WithWindow(session =>
        {
            Assert.False(Named<Border>(session.Window, "UpdateNotice").IsVisible);
        });
    }

    [AvaloniaFact]
    public void OpeningTheWindowRunsTheStartupCheckOnItsOwn()
    {
        // Nothing here asks for a check. Every other test in this class reaches the notice the
        // same way, so without the window's Opened handler they would all fail too, but this is
        // the one that says why.
        var updater = new AlwaysNewer();

        UiHarness.WithWindow(
            session =>
            {
                Assert.Equal(1, updater.Checks);
                Assert.True(Named<Border>(session.Window, "UpdateNotice").IsVisible);
            },
            updater: updater);
    }

    [AvaloniaFact]
    public void TheWindowGivesItsOwnModelAnUpdater()
    {
        // The composition root. Asking only whether an updater is there, which touches no
        // network: the window's own model is swapped out before it opens, so it never checks.
        UiHarness.WithWindow(session => Assert.True(session.RestoredModel.CanCheckForUpdates));
    }

    [AvaloniaFact]
    public void AReadyUpdateInstallsWhenTheWindowCloses()
    {
        var updater = new AlwaysNewer();

        UiHarness.WithWindow(
            session =>
            {
                Named<Button>(session.Window, "UpdatePrimaryButton").Command!.Execute(null);
                DispatcherPump.Drain();
                Assert.Equal(UpdateNoticeState.Ready, session.Model.UpdateState);

                // Safe to close: the harness's model is the data context, and closing saves
                // through that model into this test's temporary directory.
                session.Window.Close();
                DispatcherPump.Drain();

                Assert.Equal([false], updater.Applies);
            },
            updater: updater);
    }

    [AvaloniaFact]
    public void RestartNowInstallsAndClosesTheWindow()
    {
        var updater = new AlwaysNewer();

        UiHarness.WithWindow(
            session =>
            {
                var primary = Named<Button>(session.Window, "UpdatePrimaryButton");
                primary.Command!.Execute(null);
                DispatcherPump.Drain();
                Assert.Equal("Restart now", primary.Content);

                primary.Command!.Execute(null);
                DispatcherPump.Drain();

                // Once, with a restart, and not a second time as the window closes.
                Assert.Equal([true], updater.Applies);
                Assert.False(session.Window.IsVisible);
            },
            updater: updater);
    }

    [AvaloniaFact]
    public void AFoundReleaseShowsTheNoticeNamingTheVersion()
    {
        UiHarness.WithWindow(
            session =>
            {
                Assert.True(Named<Border>(session.Window, "UpdateNotice").IsVisible);
                Assert.Contains("9.9.9", Named<TextBlock>(session.Window, "UpdateHeadlineText").Text);
                Assert.Equal("Update now", Named<Button>(session.Window, "UpdatePrimaryButton").Content);
                Assert.Equal("You're on v1.2.0.", Named<TextBlock>(session.Window, "UpdateDetailText").Text);

                // The dismiss button shows only a glyph, so its word lives in the tooltip and
                // the accessible name.
                var dismiss = Named<Button>(session.Window, "DismissUpdateButton");
                Assert.Equal("Later", ToolTip.GetTip(dismiss));
                Assert.Equal("Later", AutomationProperties.GetName(dismiss));
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void TheNoticeIsPaintedFromThePaletteInBothThemes()
    {
        // The accent marks the card's one strip, its icon and its one primary action. The card's
        // own edge stays the soft border, which is what keeps the notice calm on the page.
        try
        {
            UiHarness.WithWindow(
                session =>
                {
                    var window = session.Window;
                    var notice = Named<Border>(window, "UpdateNotice");
                    var strip = Named<Border>(window, "UpdateAccentStrip");
                    var icon = Named<Avalonia.Controls.Shapes.Path>(window, "UpdateIcon");
                    var primary = Named<Button>(window, "UpdatePrimaryButton");

                    foreach (var (theme, variant) in new[]
                    {
                        (AppTheme.Light, ThemeVariant.Light),
                        (AppTheme.Dark, ThemeVariant.Dark),
                    })
                    {
                        ThemeSwitcher.Apply(theme);
                        DispatcherPump.Drain();

                        var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().First();

                        Assert.Equal(ColourOf("AccentBrush", variant), (strip.Background as ISolidColorBrush)?.Color);
                        Assert.Equal(ColourOf("AccentBrush", variant), (icon.Fill as ISolidColorBrush)?.Color);
                        Assert.Equal(ColourOf("BorderBrush", variant), (notice.BorderBrush as ISolidColorBrush)?.Color);
                        Assert.Equal(ColourOf("AccentBrush", variant), (presenter.Background as ISolidColorBrush)?.Color);
                        Assert.Equal(ColourOf("OnAccentBrush", variant), (presenter.Foreground as ISolidColorBrush)?.Color);
                    }
                },
                updater: new AlwaysNewer());
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    private static Color ColourOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    [AvaloniaFact]
    public void TheNoticeStaysCompact()
    {
        // The user asked for the card to be vertically smaller once the actions moved up beside the
        // text. Pinned as a ceiling rather than an exact size, so font metrics can vary a little
        // across machines without failing, while a regression back to a separate button row cannot
        // hide: that layout measured about 118 px.
        UiHarness.WithWindow(
            session =>
            {
                DispatcherPump.Drain();
                var notice = Named<Border>(session.Window, "UpdateNotice");

                Assert.True(notice.IsVisible);
                Assert.True(notice.Bounds.Height <= 80, $"the notice is {notice.Bounds.Height:0} px tall");

                // The user found the primary button "strangely big" beside the 12pt text sharing its
                // line. Kept close to that line's height: a full-size Fluent button is about 32 px.
                var primary = Named<Button>(session.Window, "UpdatePrimaryButton");
                Assert.True(primary.Bounds.Height <= 26, $"the Update now button is {primary.Bounds.Height:0} px tall");
                Assert.Equal(12, primary.FontSize);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void TheDownloadIconIsCenteredInItsCircle()
    {
        // Reported by eye: the arrow sat left of center. A glyph taller than it is wide, drawn Uniform
        // into a square box, is placed at the box's left edge. So check both halves of the fix: the
        // glyph fills its own box, and that box is centered in the circle.
        UiHarness.WithWindow(
            session =>
            {
                DispatcherPump.Drain();
                var icon = Named<Avalonia.Controls.Shapes.Path>(session.Window, "UpdateIcon");
                var circle = (Control)icon.Parent!;

                var drawn = icon.RenderedGeometry!.Bounds;
                Assert.True(Math.Abs(drawn.Width - icon.Bounds.Width) < 0.75,
                    $"glyph {drawn.Width:0.00} wide in a {icon.Bounds.Width:0.00} box");
                Assert.True(Math.Abs(drawn.Height - icon.Bounds.Height) < 0.75,
                    $"glyph {drawn.Height:0.00} tall in a {icon.Bounds.Height:0.00} box");

                var centerX = icon.Bounds.X + (icon.Bounds.Width / 2);
                var centerY = icon.Bounds.Y + (icon.Bounds.Height / 2);
                Assert.True(Math.Abs(centerX - (circle.Bounds.Width / 2)) < 0.75, $"icon center x {centerX:0.00} in a {circle.Bounds.Width:0} circle");
                Assert.True(Math.Abs(centerY - (circle.Bounds.Height / 2)) < 0.75, $"icon center y {centerY:0.00} in a {circle.Bounds.Height:0} circle");
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void UpdateNowStagesAndTheButtonBecomesRestart()
    {
        UiHarness.WithWindow(
            session =>
            {
                var primary = Named<Button>(session.Window, "UpdatePrimaryButton");
                primary.Command!.Execute(null);
                DispatcherPump.Drain();

                Assert.Equal("Restart now", primary.Content);
                Assert.False(Named<Button>(session.Window, "DismissUpdateButton").IsVisible);
                Assert.Contains("close", Named<TextBlock>(session.Window, "UpdateDetailText").Text);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void LaterHidesTheNotice()
    {
        UiHarness.WithWindow(
            session =>
            {
                Named<Button>(session.Window, "DismissUpdateButton").Command!.Execute(null);
                DispatcherPump.Drain();

                Assert.False(Named<Border>(session.Window, "UpdateNotice").IsVisible);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void TheGearMenuOffersACheckAndItRuns()
    {
        var updater = new AlwaysNewer();

        UiHarness.WithWindow(
            session =>
            {
                // Relative, not absolute: opening the window already ran the startup check,
                // which is its own wiring working, not this entry's.
                var before = updater.Checks;

                var gear = Named<Button>(session.Window, "GearButton");
                UiHarness.Click(session.Window, UiHarness.At(gear, 0.5, 0.5));
                DispatcherPump.Drain();

                var item = Assert.Single(
                    session.Window.GetVisualDescendants().OfType<MenuItem>(),
                    i => (i.Header as string) == "Check for updates");
                UiHarness.Click(session.Window, UiHarness.At(item, 0.5, 0.5));
                DispatcherPump.Drain();

                Assert.Equal(before + 1, updater.Checks);
                Assert.True(Named<Border>(session.Window, "UpdateNotice").IsVisible);
            },
            updater: updater);
    }

    private sealed class AlwaysNewer : IUpdater
    {
        public int Checks { get; private set; }

        /// <summary>The restart flag of every Apply, in order.</summary>
        public List<bool> Applies { get; } = [];

        public string CurrentVersion => "1.2.0";

        public Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return Task.FromResult(UpdateDecision.For("1.2.0", new ReleaseInfo("v9.9.9", "https://example.org/releases/v9.9.9", Assets: [])));
        }

        public InstallResult CanInstall() => new(InstallOutcome.Staged);

        public Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default) =>
            Task.FromResult(new InstallResult(InstallOutcome.Staged));

        public bool Apply(bool restart)
        {
            Applies.Add(restart);
            return true;
        }

        public void OpenReleasePage(string? url)
        {
        }
    }
}
