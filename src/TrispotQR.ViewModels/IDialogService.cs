using TrispotQR.Core.Presets;
using TrispotQR.Core.Updates;

namespace TrispotQR.ViewModels;

/// <summary>
/// Everything the view model needs from the world outside it. Kept behind an interface so
/// the view model can be exercised without a window on screen, and so the same view model
/// serves a WPF window today and an Avalonia one later.
/// </summary>
public interface IDialogService
{
    string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory);

    string? AskForImage(string? directory);

    string? AskForText(string title, string prompt, string initialValue);

    bool Confirm(string title, string message);

    /// <summary>
    /// Asks whether to go ahead with something risky, with the action named on the button.
    /// Returns true to proceed.
    /// </summary>
    /// <param name="severe">True when the code did not scan at all, false when it merely might not.</param>
    bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);

    /// <summary>
    /// Shows the settings window and returns what the user chose, or null if they cancelled.
    ///
    /// The whole exchange lives behind this call because opening a window, owning it, and
    /// applying the chosen theme are all things only a UI toolkit can do. The view model's
    /// part is deciding what to persist afterwards.
    /// </summary>
    AppSettings? EditSettings(AppSettings current);

    /// <summary>
    /// Shows a release's notes and returns how the window was closed.
    ///
    /// The default opens the release page instead, which is what What's new did before notes were shown
    /// in the app, so a toolkit without a notes window keeps working unchanged.
    /// </summary>
    /// <param name="title">The window title, naming the version.</param>
    /// <param name="notes">The parsed notes. Empty when the release published none.</param>
    /// <param name="primaryLabel">The notice's primary action, or null when it has none right now (while downloading).</param>
    ReleaseNotesChoice ShowReleaseNotes(string title, IReadOnlyList<NoteBlock> notes, string? primaryLabel) => ReleaseNotesChoice.ViewOnline;
}

/// <summary>How the release notes window was closed.</summary>
public enum ReleaseNotesChoice
{
    /// <summary>Closed without choosing anything, including by Escape or the title bar.</summary>
    Close,

    /// <summary>The notice's primary action, named on the button (Update now, Download, Restart now, Try again).</summary>
    Primary,

    /// <summary>Open the release page in the browser.</summary>
    ViewOnline,
}
