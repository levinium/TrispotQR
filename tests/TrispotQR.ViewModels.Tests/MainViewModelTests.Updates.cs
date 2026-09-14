using TrispotQR.Core.Presets;
using TrispotQR.Core.Updates;
using TrispotQR.ViewModels;

namespace TrispotQR.ViewModels.Tests;

public partial class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ReleaseUrl = "https://example.org/releases/v1.3.0";

    private static UpdateVerdict Newer() =>
        UpdateDecision.For("1.2.0", new ReleaseInfo("v1.3.0", ReleaseUrl, Assets: []));

    private static UpdateVerdict NewerWithNotes(string? notes) =>
        UpdateDecision.For("1.2.0", new ReleaseInfo("v1.3.0", ReleaseUrl, Assets: [], Notes: notes));

    // The fake updater runs 1.2.0, so "current" is a feed whose latest release is that same version.
    private static UpdateVerdict Current() =>
        UpdateDecision.For("1.2.0", new ReleaseInfo("v1.2.0", "https://example.org/releases/v1.2.0", Assets: []));

    private MainViewModel CreateWithUpdater(FakeUpdater updater, AppSettings? settings = null)
    {
        if (settings is not null)
        {
            new AppSettingsStore(_directory).Save(settings);
        }

        return new MainViewModel(
            _dialogs, new FakeUiTimer(), new FakeImageClipboard(), new PresetStore(_directory),
            new AppSettingsStore(_directory), updater, () => Now);
    }

    [Fact]
    public async Task Updates_NothingIsShownWhenTheBuildIsCurrent()
    {
        // Which is nearly always. A line permanently on screen saying there is no news is worse
        // than no line.
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Current() });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.False(vm.IsUpdateNoticeVisible);
        Assert.Equal(UpdateNoticeState.None, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_AFoundReleaseIsNamedInTheNotice()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.True(vm.IsUpdateNoticeVisible);
        Assert.Contains("1.3.0", vm.UpdateHeadline);
        Assert.Equal("Update now", vm.UpdatePrimaryLabel);
        Assert.Equal("You're on v1.2.0.", vm.UpdateDetail);
        Assert.True(vm.CanDismissUpdate);
    }

    [Fact]
    public async Task Updates_AnUnansweredCheckShowsNothingAndIsTriedAgainNextLaunch()
    {
        var updater = new FakeUpdater { Verdict = UpdateDecision.For("1.2.0", null) };
        var vm = CreateWithUpdater(updater);

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.False(vm.IsUpdateNoticeVisible);

        // Recording an unanswered check would silence the next day's attempt for no reason.
        Assert.Null(new AppSettingsStore(_directory).Load().LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Updates_WithTheSettingOffNothingIsAsked()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { CheckForUpdates = false });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(0, updater.Checks);
        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_ACheckWithinTheLastDayIsNotRepeated()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { LastUpdateCheckUtc = Now.AddHours(-2) });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task Updates_ADueCheckRunsAndRecordsWhenItRan()
    {
        var updater = new FakeUpdater { Verdict = Current() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { LastUpdateCheckUtc = Now.AddDays(-2) });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(1, updater.Checks);
        Assert.Equal(Now, new AppSettingsStore(_directory).Load().LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Updates_AskingFromTheMenuWorksWithTheSettingOffAndSaysWhatItFound()
    {
        // The setting governs what the app does unasked. A person asking is a different thing.
        var updater = new FakeUpdater { Verdict = Current() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { CheckForUpdates = false });

        await vm.CheckForUpdatesNowAsync();

        Assert.Equal(1, updater.Checks);
        Assert.Contains(_dialogs.Informations, m => m.Contains("latest version") && m.Contains("v1.2.0"));
    }

    [Fact]
    public async Task Updates_AnUnansweredMenuCheckSaysSoRatherThanClaimingUpToDate()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = UpdateDecision.For("1.2.0", null) });

        await vm.CheckForUpdatesNowAsync();

        Assert.Contains(_dialogs.Informations, m => m.Contains("Could not check"));
    }

    [Fact]
    public async Task Updates_WhereItCannotInstallTheButtonOpensTheReleasePageInstead()
    {
        var updater = new FakeUpdater
        {
            Verdict = Newer(),
            Readiness = new InstallResult(InstallOutcome.NotWritable, "Trispot QR is installed where it cannot update itself."),
        };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal("Download", vm.UpdatePrimaryLabel);
        Assert.Contains("cannot update itself", vm.UpdateDetail);

        vm.UpdatePrimaryCommand.Execute(null);

        Assert.Equal([ReleaseUrl], updater.OpenedUrls);
        Assert.Equal(0, updater.Stages);
    }

    [Fact]
    public async Task Updates_UpdateNowDownloadsReportsProgressAndBecomesReady()
    {
        var updater = new FakeUpdater { Verdict = Newer(), ProgressToReport = 0.42 };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        await vm.StageUpdateAsync();

        Assert.Equal(1, updater.Stages);
        Assert.Equal(0.42, vm.UpdateProgress);
        Assert.Equal(UpdateNoticeState.Ready, vm.UpdateState);
        Assert.Equal("Restart now", vm.UpdatePrimaryLabel);
        Assert.False(vm.CanDismissUpdate);
        Assert.Contains("close", vm.UpdateDetail);
    }

    [Fact]
    public async Task Updates_AFailedDownloadSaysWhyAndInstallsNothing()
    {
        var updater = new FakeUpdater
        {
            Verdict = Newer(),
            StageResult = new InstallResult(InstallOutcome.VerificationFailed, "The download did not match the published checksum, so it was discarded."),
        };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        await vm.StageUpdateAsync();

        Assert.Equal(UpdateNoticeState.Failed, vm.UpdateState);
        Assert.Contains("checksum", vm.UpdateDetail);
        Assert.Equal("Try again", vm.UpdatePrimaryLabel);

        vm.ApplyStagedUpdateOnExit();
        Assert.Empty(updater.Applies);
    }

    [Fact]
    public async Task Updates_RestartNowInstallsAndAsksTheWindowToClose()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater);
        var asked = 0;
        vm.RestartRequested += (_, _) => asked++;
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.RestartToUpdate();

        Assert.Equal([true], updater.Applies);
        Assert.Equal(1, asked);

        // Installed already, so closing must not try a second time.
        vm.ApplyStagedUpdateOnExit();
        Assert.Equal([true], updater.Applies);
    }

    [Fact]
    public async Task Updates_ARestartThatCannotInstallStaysOpenAndSaysSo()
    {
        var updater = new FakeUpdater { Verdict = Newer(), ApplyResult = false };
        var vm = CreateWithUpdater(updater);
        var asked = 0;
        vm.RestartRequested += (_, _) => asked++;
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.RestartToUpdate();

        Assert.Equal(0, asked);
        Assert.Equal(UpdateNoticeState.Failed, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_AReadyUpdateInstallsWhenTheAppClosesWithoutRestarting()
    {
        // Nobody should lose what they are typing to get the new version.
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.ApplyStagedUpdateOnExit();

        Assert.Equal([false], updater.Applies);
    }

    [Fact]
    public async Task Updates_LaterHidesTheNotice()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });
        await vm.CheckForUpdatesAtStartupAsync();

        vm.DismissUpdateCommand.Execute(null);

        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_ADownloadThatFinishesAfterCancelIsNeitherReadyNorInstalled()
    {
        // Cancellation can be noticed too late, after the file already verified. The user said
        // stop, so that result must not bring the notice back or install on close.
        var pending = new TaskCompletionSource<InstallResult>();
        var updater = new FakeUpdater { Verdict = Newer(), StageWith = _ => pending.Task };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        var staging = vm.StageUpdateAsync();
        vm.DismissUpdateCommand.Execute(null);
        pending.SetResult(new InstallResult(InstallOutcome.Staged));
        await staging;

        Assert.Equal(UpdateNoticeState.None, vm.UpdateState);
        vm.ApplyStagedUpdateOnExit();
        Assert.Empty(updater.Applies);
    }

    [Fact]
    public async Task Updates_ACancelledDownloadFinishingLateLeavesTheNextDownloadAlone()
    {
        var downloads = new Queue<TaskCompletionSource<InstallResult>>([new(), new()]);
        var first = downloads.Peek();
        var updater = new FakeUpdater { Verdict = Newer(), StageWith = _ => downloads.Dequeue().Task };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        var firstStaging = vm.StageUpdateAsync();
        vm.DismissUpdateCommand.Execute(null);

        await vm.CheckForUpdatesNowAsync();
        _ = vm.StageUpdateAsync();

        first.SetResult(new InstallResult(InstallOutcome.Canceled));
        await firstStaging;

        Assert.Equal(UpdateNoticeState.Downloading, vm.UpdateState);

        vm.DismissUpdateCommand.Execute(null);
        Assert.True(updater.StageTokens[1].IsCancellationRequested);
    }

    [Fact]
    public async Task Updates_ACheckAnsweringLateLeavesADownloadInProgressAlone()
    {
        // A menu check asked while the notice was Available, answering only after Update now was
        // clicked. Resetting to Available would orphan the download and offer it a second time.
        var pendingStage = new TaskCompletionSource<InstallResult>();
        var updater = new FakeUpdater { Verdict = Newer(), StageWith = _ => pendingStage.Task };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        var pendingCheck = new TaskCompletionSource<UpdateVerdict>();
        updater.CheckWith = () => pendingCheck.Task;
        var lateCheck = vm.CheckForUpdatesNowAsync();

        var staging = vm.StageUpdateAsync();
        Assert.Equal(UpdateNoticeState.Downloading, vm.UpdateState);

        pendingCheck.SetResult(Newer());
        await lateCheck;

        Assert.Equal(UpdateNoticeState.Downloading, vm.UpdateState);
        Assert.Equal(Now, new AppSettingsStore(_directory).Load().LastUpdateCheckUtc);

        pendingStage.SetResult(new InstallResult(InstallOutcome.Staged));
        await staging;
        Assert.Equal(UpdateNoticeState.Ready, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_TurningTheSettingOffClearsTheNotice()
    {
        // Otherwise the notice stays after being told to stop looking, the opposite of what the
        // switch says it does.
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });
        await vm.CheckForUpdatesAtStartupAsync();

        _dialogs.NextSettings = AppSettings.Default with { CheckForUpdates = false };
        vm.OpenSettings();

        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_WithoutAnUpdaterThereIsNothingToCheck()
    {
        var vm = Create();

        await vm.CheckForUpdatesAtStartupAsync();
        await vm.CheckForUpdatesNowAsync();

        Assert.False(vm.CanCheckForUpdates);
        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_WhatsNewShowsTheNotesInsteadOfOpeningTheBrowser()
    {
        var updater = new FakeUpdater { Verdict = NewerWithNotes("## Fixes\n\n- One") };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        vm.WhatsNewCommand.Execute(null);

        var shown = Assert.Single(_dialogs.ReleaseNotesShown);
        Assert.Equal("What's new in Trispot QR 1.3.0", shown.Title);
        Assert.Collection(shown.Notes,
            block => Assert.Equal("Fixes", Assert.Single(Assert.IsType<NoteHeading>(block).Spans).Text),
            block => Assert.Equal("One", Assert.Single(Assert.Single(Assert.IsType<NoteBulletList>(block).Items)).Text));
        Assert.Equal("Update now", shown.PrimaryLabel);
        Assert.Empty(updater.OpenedUrls);
    }

    [Fact]
    public async Task Updates_WhatsNewWithNoPublishedNotesStillOpensTheWindow()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = NewerWithNotes(null) });
        await vm.CheckForUpdatesAtStartupAsync();

        vm.WhatsNewCommand.Execute(null);

        var shown = Assert.Single(_dialogs.ReleaseNotesShown);
        Assert.Empty(shown.Notes);
    }

    [Fact]
    public async Task Updates_UpdateNowFromTheNotesStartsTheDownload()
    {
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One") };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Primary;

        vm.WhatsNewCommand.Execute(null);

        Assert.Equal(1, updater.Stages);
    }

    [Fact]
    public async Task Updates_DownloadFromTheNotesOpensTheReleasePageWhenTheFolderIsReadOnly()
    {
        var updater = new FakeUpdater
        {
            Verdict = NewerWithNotes("- One"),
            Readiness = new InstallResult(InstallOutcome.NotWritable, "Trispot QR is installed where it cannot update itself."),
        };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Primary;

        vm.WhatsNewCommand.Execute(null);

        Assert.Equal("Download", Assert.Single(_dialogs.ReleaseNotesShown).PrimaryLabel);
        Assert.Equal([ReleaseUrl], updater.OpenedUrls);
        Assert.Equal(0, updater.Stages);
    }

    [Fact]
    public async Task Updates_ViewOnGitHubFromTheNotesOpensTheReleasePage()
    {
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One") };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.ViewOnline;

        vm.WhatsNewCommand.Execute(null);

        Assert.Equal([ReleaseUrl], updater.OpenedUrls);
    }

    [Fact]
    public async Task Updates_ClosingTheNotesDoesNothing()
    {
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One") };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Close;

        vm.WhatsNewCommand.Execute(null);

        Assert.Equal(0, updater.Stages);
        Assert.Empty(updater.OpenedUrls);
        Assert.Equal(UpdateNoticeState.Available, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_WhileDownloadingTheNotesOfferNoPrimaryAction()
    {
        // A Primary answer with nothing offered must not start a second download.
        var pending = new TaskCompletionSource<InstallResult>();
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One"), StageWith = _ => pending.Task };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        var staging = vm.StageUpdateAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Primary;

        vm.WhatsNewCommand.Execute(null);

        Assert.Null(Assert.Single(_dialogs.ReleaseNotesShown).PrimaryLabel);
        Assert.Equal(1, updater.Stages);
        Assert.Equal(UpdateNoticeState.Downloading, vm.UpdateState);

        pending.SetResult(new InstallResult(InstallOutcome.Staged));
        await staging;
    }

    [Fact]
    public async Task Updates_PrimaryFromTheNotesIsIgnoredWhenTheOfferChangedWhileTheyWereOpen()
    {
        // The notes opened mid-download with no button, and the download finished while they were
        // up. A Primary answer must not restart the app from a window that never offered it.
        var pending = new TaskCompletionSource<InstallResult>();
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One"), StageWith = _ => pending.Task };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        var staging = vm.StageUpdateAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Primary;
        _dialogs.OnShowReleaseNotes = () =>
        {
            // The download's continuation is posted to xUnit's context, which runs on other
            // threads, so waiting here is deterministic and cannot deadlock.
            pending.SetResult(new InstallResult(InstallOutcome.Staged));
            staging.Wait();
            Assert.Equal(UpdateNoticeState.Ready, vm.UpdateState);
        };

        vm.WhatsNewCommand.Execute(null);

        Assert.Empty(updater.Applies);
        Assert.Equal(UpdateNoticeState.Ready, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_RestartNowFromTheNotesRestartsWhenReady()
    {
        var updater = new FakeUpdater { Verdict = NewerWithNotes("- One") };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();
        _dialogs.ReleaseNotesAnswer = ReleaseNotesChoice.Primary;

        vm.WhatsNewCommand.Execute(null);

        Assert.Equal("Restart now", Assert.Single(_dialogs.ReleaseNotesShown).PrimaryLabel);
        Assert.Equal([true], updater.Applies);
    }

    private sealed class FakeUpdater : IUpdater
    {
        public UpdateVerdict Verdict { get; init; }

        public InstallResult Readiness { get; init; } = new(InstallOutcome.Staged);

        public InstallResult StageResult { get; init; } = new(InstallOutcome.Staged);

        public bool ApplyResult { get; init; } = true;

        public double? ProgressToReport { get; init; }

        /// <summary>When set, holds each download open until the test completes the task it hands back.</summary>
        public Func<CancellationToken, Task<InstallResult>>? StageWith { get; init; }

        public List<CancellationToken> StageTokens { get; } = [];

        /// <summary>When set, the next checks answer with the task this hands back instead of <see cref="Verdict"/>.</summary>
        public Func<Task<UpdateVerdict>>? CheckWith { get; set; }

        public int Checks { get; private set; }

        public int Stages { get; private set; }

        public List<bool> Applies { get; } = [];

        public List<string?> OpenedUrls { get; } = [];

        /// <summary>The version <see cref="Newer"/> offers an update from, and <see cref="Current"/> is up to date on.</summary>
        public string CurrentVersion => "1.2.0";

        public Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return CheckWith is { } check ? check() : Task.FromResult(Verdict);
        }

        public InstallResult CanInstall() => Readiness;

        public Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
        {
            Stages++;
            StageTokens.Add(ct);
            if (ProgressToReport is { } p)
            {
                progress?.Report(p);
            }

            return StageWith is { } stage ? stage(ct) : Task.FromResult(StageResult);
        }

        public bool Apply(bool restart)
        {
            Applies.Add(restart);
            return ApplyResult;
        }

        public void OpenReleasePage(string? url) => OpenedUrls.Add(url);
    }
}
