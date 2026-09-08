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
}
