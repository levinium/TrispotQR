using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Updates;
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
                Assert.Equal("Later", Named<Button>(session.Window, "DismissUpdateButton").Content);
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
