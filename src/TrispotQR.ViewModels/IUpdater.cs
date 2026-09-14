using TrispotQR.Core.Updates;

namespace TrispotQR.ViewModels;

/// <summary>How far an install got, and why it stopped there.</summary>
public enum InstallOutcome
{
    /// <summary>Downloaded, verified and staged beside the app. Or, from CanInstall, possible.</summary>
    Staged,

    /// <summary>The release has no TrispotQR.exe attached.</summary>
    NothingToInstall,

    /// <summary>This copy cannot replace itself: a development build, or not Windows.</summary>
    NotSupported,

    /// <summary>The app's folder cannot be written without administrator rights.</summary>
    NotWritable,

    DownloadFailed,

    /// <summary>What arrived is not what was published.</summary>
    VerificationFailed,

    Canceled,
}

/// <param name="Detail">A sentence for the user, or null when there is nothing to add.</param>
public readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null)
{
    public bool IsStaged => Outcome == InstallOutcome.Staged;
}

/// <summary>
/// Everything the view model needs to find and install a newer release. Behind an interface so
/// the notice's behavior is tested without a network, and optional so the WPF app, which is
/// being retired, passes nothing and simply has no updates.
/// </summary>
public interface IUpdater
{
    /// <summary>The running build's version, for "You have the latest version".</summary>
    string CurrentVersion { get; }

    /// <summary>Asks once. Never throws: every failure is an Unknown verdict.</summary>
    Task<UpdateVerdict> CheckAsync(CancellationToken ct = default);

    /// <summary>Whether this copy could install in place, asked before the button is offered.</summary>
    InstallResult CanInstall();

    /// <summary>
    /// Downloads and verifies the release's exe beside the app. Changes nothing installed.
    /// Progress is reported on the UI thread, because the view model applies it straight to a
    /// bound property; an implementation that downloads on the thread pool must marshal it.
    /// </summary>
    Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default);

    /// <summary>
    /// Swaps the staged exe into place. With <paramref name="restart"/>, also starts it. Returns
    /// true when installed; false means nothing was changed.
    /// </summary>
    bool Apply(bool restart);

    /// <summary>Opens a release page in the browser; the project's page when the url is null.</summary>
    void OpenReleasePage(string? url);
}
