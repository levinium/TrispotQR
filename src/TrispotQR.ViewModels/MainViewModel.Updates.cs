using TrispotQR.Core.Updates;

namespace TrispotQR.ViewModels;

/// <summary>What the update notice is showing.</summary>
public enum UpdateNoticeState
{
    None,
    Available,
    Downloading,
    Ready,
    Failed,
}

/// <summary>
/// The update notice: whether to look, what was found, and moving from "available" through
/// "downloading" to "ready". Everything here is idle unless an <see cref="IUpdater"/> was
/// supplied, which only the Avalonia app does.
/// </summary>
public sealed partial class MainViewModel
{
    private IUpdater? _updater;
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;
    private UpdateNoticeState _updateState;
    private UpdateVerdict _verdict;
    private bool _canInstallInPlace;
    private string? _installBlockedReason;
    private string? _updateFailure;
    private double _updateProgress;
    private CancellationTokenSource? _download;

    /// <summary>Asked for by the window so it can close normally and save the session.</summary>
    public event EventHandler? RestartRequested;

    public RelayCommand UpdatePrimaryCommand { get; private set; } = null!;

    public RelayCommand WhatsNewCommand { get; private set; } = null!;

    public RelayCommand DismissUpdateCommand { get; private set; } = null!;

    public RelayCommand CheckForUpdatesCommand { get; private set; } = null!;

    public bool CanCheckForUpdates => _updater is not null;

    public UpdateNoticeState UpdateState
    {
        get => _updateState;
        private set
        {
            if (!SetField(ref _updateState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsUpdateNoticeVisible));
            OnPropertyChanged(nameof(IsUpdateDownloading));
            OnPropertyChanged(nameof(CanDismissUpdate));
            RaiseUpdateText();
            UpdatePrimaryCommand.RaiseCanExecuteChanged();
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Only when there is something to say. Nothing is ever shown to report no news.</summary>
    public bool IsUpdateNoticeVisible => UpdateState != UpdateNoticeState.None;

    public bool IsUpdateDownloading => UpdateState == UpdateNoticeState.Downloading;

    /// <summary>
    /// Later while available or failed, Cancel while downloading. A ready update has no Later:
    /// it installs itself when the app closes anyway.
    /// </summary>
    public bool CanDismissUpdate => UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Failed or UpdateNoticeState.Downloading;

    public double UpdateProgress
    {
        get => _updateProgress;
        private set
        {
            if (SetField(ref _updateProgress, value))
            {
                OnPropertyChanged(nameof(UpdateHeadline));
            }
        }
    }

    public string UpdateHeadline => UpdateState switch
    {
        UpdateNoticeState.Available => $"Trispot QR {_verdict.Version} is available.",
        UpdateNoticeState.Downloading => $"Downloading Trispot QR {_verdict.Version} ({UpdateProgress * 100:0}%)",
        UpdateNoticeState.Ready => $"Trispot QR {_verdict.Version} is ready.",
        UpdateNoticeState.Failed => $"Trispot QR {_verdict.Version} could not be installed.",
        _ => string.Empty,
    };

    public string UpdateDetail => UpdateState switch
    {
        UpdateNoticeState.Available when _canInstallInPlace => $"You're on v{_updater?.CurrentVersion}.",
        UpdateNoticeState.Available when !_canInstallInPlace => _installBlockedReason ?? string.Empty,
        UpdateNoticeState.Ready => "Restart now, or it installs when you close Trispot QR. Restarting clears what is typed in the box; saved styles and settings carry over.",
        UpdateNoticeState.Failed => _updateFailure ?? string.Empty,
        _ => string.Empty,
    };

    public string UpdatePrimaryLabel => UpdateState switch
    {
        UpdateNoticeState.Available => _canInstallInPlace ? "Update now" : "Download",
        UpdateNoticeState.Ready => "Restart now",
        UpdateNoticeState.Failed => _canInstallInPlace ? "Try again" : "Download",
        _ => string.Empty,
    };

    /// <summary>The same button stops a download in progress, and says so.</summary>
    public string UpdateDismissLabel => UpdateState == UpdateNoticeState.Downloading ? "Cancel" : "Later";

    private void InitializeUpdates(IUpdater? updater, Func<DateTimeOffset>? clock)
    {
        _updater = updater;
        _clock = clock ?? _clock;

        UpdatePrimaryCommand = new RelayCommand(OnUpdatePrimary,
            () => UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Ready or UpdateNoticeState.Failed);
        WhatsNewCommand = new RelayCommand(ShowWhatsNew);
        DismissUpdateCommand = new RelayCommand(DismissUpdate);
        CheckForUpdatesCommand = new RelayCommand(
            () => _ = CheckForUpdatesNowAsync(),
            () => _updater is not null && UpdateState is not (UpdateNoticeState.Downloading or UpdateNoticeState.Ready));
    }

    /// <summary>At launch: only when the setting is on and a day has passed since the last answer.</summary>
    public async Task CheckForUpdatesAtStartupAsync()
    {
        if (_updater is null || !_settings.CheckForUpdates || !UpdateSchedule.IsDue(_settings.LastUpdateCheckUtc, _clock()))
        {
            return;
        }

        await RunCheckAsync(sayWhatWasFound: false);
    }

    /// <summary>From the gear menu: ignores the setting and the schedule, and always reports.</summary>
    public async Task CheckForUpdatesNowAsync()
    {
        if (_updater is null || UpdateState is UpdateNoticeState.Downloading or UpdateNoticeState.Ready)
        {
            return;
        }

        await RunCheckAsync(sayWhatWasFound: true);
    }

    private async Task RunCheckAsync(bool sayWhatWasFound)
    {
        var verdict = await _updater!.CheckAsync();

        // Only an answer counts as a check. An unanswered one is simply tried again next launch.
        if (verdict.Outcome != UpdateOutcome.Unknown)
        {
            _settings = _settings with { LastUpdateCheckUtc = _clock() };
            _settingsStore.Save(_settings with { Style = _style });
        }

        // The check may have been asked before Update now was clicked and answered after. A
        // download the user started, or one that is ready, outranks anything a check can say.
        if (UpdateState is UpdateNoticeState.Downloading or UpdateNoticeState.Ready)
        {
            return;
        }

        if (verdict.IsAvailable)
        {
            var readiness = _updater.CanInstall();
            _verdict = verdict;
            _canInstallInPlace = readiness.IsStaged;
            _installBlockedReason = readiness.Detail;
            _updateFailure = null;
            UpdateState = UpdateNoticeState.Available;
            RaiseUpdateText();
            return;
        }

        if (sayWhatWasFound)
        {
            _dialogs.ShowInformation("Updates", verdict.Outcome == UpdateOutcome.UpToDate
                ? $"You have the latest version, v{_updater.CurrentVersion}."
                : "Could not check for updates right now. Check your internet connection and try again later.");
        }
    }

    /// <summary>
    /// Shows the release notes with the notice's primary action beside them. The window is a pause in
    /// which a download may have finished or failed, so a Primary answer is honored only when a button
    /// was offered and it still names the action the notice's button would run now. Otherwise the
    /// user would get an action they were never shown, such as a restart.
    /// </summary>
    private void ShowWhatsNew()
    {
        if (_updater is null)
        {
            return;
        }

        var title = $"What's new in Trispot QR {_verdict.Version}";
        var notes = ReleaseNotes.Parse(_verdict.Release?.Notes);
        var primaryLabel = UpdatePrimaryCommand.CanExecute(null) ? UpdatePrimaryLabel : null;

        switch (_dialogs.ShowReleaseNotes(title, notes, primaryLabel))
        {
            case ReleaseNotesChoice.Primary when primaryLabel is not null && primaryLabel == UpdatePrimaryLabel:
                OnUpdatePrimary();
                break;
            case ReleaseNotesChoice.ViewOnline:
                _updater.OpenReleasePage(_verdict.Url);
                break;
        }
    }

    private void OnUpdatePrimary()
    {
        switch (UpdateState)
        {
            case UpdateNoticeState.Ready:
                RestartToUpdate();
                break;
            case UpdateNoticeState.Available or UpdateNoticeState.Failed when !_canInstallInPlace:
                _updater?.OpenReleasePage(_verdict.Url);
                break;
            case UpdateNoticeState.Available or UpdateNoticeState.Failed:
                _ = StageUpdateAsync();
                break;
        }
    }

    /// <summary>Downloads and verifies. Nothing installed changes, whatever happens here.</summary>
    public async Task StageUpdateAsync()
    {
        if (_updater is null || _verdict.Release is null || UpdateState == UpdateNoticeState.Downloading)
        {
            return;
        }

        using var download = new CancellationTokenSource();
        _download = download;
        UpdateProgress = 0;
        UpdateState = UpdateNoticeState.Downloading;

        var result = await _updater.StageAsync(_verdict.Release, ProgressReporter(), download.Token);

        // A cancelled download never changes anything, whatever it returned: the user already
        // said stop, and a later download may own the notice by now.
        var stillCurrent = ReferenceEquals(_download, download);
        if (stillCurrent)
        {
            _download = null;
        }

        if (!stillCurrent || download.IsCancellationRequested)
        {
            return;
        }

        if (result.IsStaged)
        {
            UpdateState = UpdateNoticeState.Ready;
        }
        else if (result.Outcome == InstallOutcome.Canceled)
        {
            UpdateState = UpdateNoticeState.None;
        }
        else
        {
            _updateFailure = result.Detail ?? "The download did not finish. Nothing was changed.";
            UpdateState = UpdateNoticeState.Failed;
            RaiseUpdateText();
        }
    }

    public void RestartToUpdate()
    {
        if (_updater is null || UpdateState != UpdateNoticeState.Ready)
        {
            return;
        }

        if (_updater.Apply(restart: true))
        {
            UpdateState = UpdateNoticeState.None;
            RestartRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _updateFailure = "The update could not be put in place. Nothing was changed.";
        UpdateState = UpdateNoticeState.Failed;
        RaiseUpdateText();
    }

    /// <summary>Called as the window closes: a ready update is installed without restarting.</summary>
    public void ApplyStagedUpdateOnExit()
    {
        if (_updater is not null && UpdateState == UpdateNoticeState.Ready)
        {
            _updater.Apply(restart: false);
        }
    }

    private void DismissUpdate()
    {
        _download?.Cancel();
        UpdateState = UpdateNoticeState.None;
    }

    private void ClearUpdateNoticeIfChecksWereTurnedOff()
    {
        // Clears Available and Failed, the notices that offer an update a check found. Failed can
        // come from a download or restart the user started, but it is still that offer, now with
        // a reason. Downloading and Ready are left alone: they are work the user started, and
        // turning off automatic checks does not undo that.
        if (!_settings.CheckForUpdates && UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Failed)
        {
            UpdateState = UpdateNoticeState.None;
        }
    }

    private void RaiseUpdateText()
    {
        OnPropertyChanged(nameof(UpdateHeadline));
        OnPropertyChanged(nameof(UpdateDetail));
        OnPropertyChanged(nameof(UpdatePrimaryLabel));
        OnPropertyChanged(nameof(UpdateDismissLabel));
    }

    /// <summary>
    /// Applied directly, because <see cref="IUpdater.StageAsync"/> promises to report on the UI
    /// thread. Progress&lt;T&gt; was the obvious choice and the wrong one: it posts to whatever
    /// context it was created on, and xUnit 2 runs async tests under a context that forwards to
    /// the thread pool, so the report would race the assertion that reads it.
    /// </summary>
    private IProgress<double> ProgressReporter() => new DirectProgress(p => UpdateProgress = p);

    private sealed class DirectProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
