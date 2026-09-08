using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Services;

/// <summary>
/// Everything the view model needs from the world outside it, in Avalonia terms.
///
/// Three members are not implemented in this phase and return null, which every caller in
/// MainViewModel already reads as "the user cancelled": the logo picker, the save-preset
/// prompt and the settings window all arrive in Phase 2c along with the UI that reaches them.
/// </summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    /// <summary>Generous, because the wait is on a person rather than on code.</summary>
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(10);

    private readonly Window _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = [.. FileFilter.Parse(filter)],
            ShowOverwritePrompt = true,
        };

        if (directory is not null)
        {
            options.SuggestedStartLocation = DispatcherWait.For(
                _owner.StorageProvider.TryGetFolderFromPathAsync(directory), DialogTimeout);
        }

        var file = DispatcherWait.For(_owner.StorageProvider.SaveFilePickerAsync(options), DialogTimeout);

        // TryGetLocalPath returns null for a location with no file system path, such as a
        // cloud provider on Android. On desktop that means the save cannot proceed, and null
        // is what the view model reads as a cancelled dialog.
        return file?.TryGetLocalPath();
    }

    /// <summary>Phase 2c, with the logo picker. Null reads as a cancelled dialog.</summary>
    public string? AskForImage(string? directory) => null;

    /// <summary>Phase 2c, with the save-preset flow. Null reads as a cancelled dialog.</summary>
    public string? AskForText(string title, string prompt, string initialValue) => null;

    /// <summary>Phase 2c, with the settings window. Null reads as a cancelled dialog.</summary>
    public AppSettings? EditSettings(AppSettings current) => null;

    public bool Confirm(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", "Cancel", defaultToConfirm: true),
            DialogTimeout);

    public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, heading, message, proceedLabel, "Cancel", defaultToProceed),
            DialogTimeout);

    public void ShowError(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", cancelLabel: null, defaultToConfirm: true),
            DialogTimeout);

    public void ShowInformation(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", cancelLabel: null, defaultToConfirm: true),
            DialogTimeout);
}
