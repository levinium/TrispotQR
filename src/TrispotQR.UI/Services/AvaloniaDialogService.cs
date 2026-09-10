using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Services;

/// <summary>Everything the view model needs from the world outside it, in Avalonia terms.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    /// <summary>Generous, because the wait is on a person rather than on code.</summary>
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(10);

    private readonly Window _owner;

    /// <summary>
    /// Messages raised before the owner was on screen, replayed once it is.
    ///
    /// MainWindow is the composition root, so it builds MainViewModel in its own constructor,
    /// and MainViewModel's constructor reports an unreadable presets.json through ShowError.
    /// That happens inside App.OnFrameworkInitializationCompleted -- before Show(), and before
    /// the main loop exists. Avalonia refuses a modal there ("Cannot show window with
    /// non-visible owner", confirmed empirically), so a corrupt presets file would have killed
    /// the app at launch instead of warning about it.
    ///
    /// Queued here rather than fixed by building the view model later, because the alternative
    /// is a window that briefly exists with no DataContext and bindings that resolve on a
    /// second pass -- a heavier change to the shell to accommodate one message. The queue is
    /// also honest about what the app can offer at that moment: it cannot ask a question
    /// before it has a window, but it can certainly remember to say something.
    /// </summary>
    private readonly Queue<(string Title, string Message)> _pending = new();

    public AvaloniaDialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _owner.Opened += OnOwnerOpened;
    }

    /// <summary>
    /// Whether a modal can be parented on the owner yet. WindowBase overrides IsVisible to
    /// default to false, so this is false for a constructed-but-never-shown window and true
    /// from Show() onwards -- the same condition Avalonia's own ShowDialog guard tests.
    /// </summary>
    private bool CanShowDialog => _owner.IsVisible;

    private void OnOwnerOpened(object? sender, EventArgs e)
    {
        _owner.Opened -= OnOwnerOpened;

        // Posted rather than shown here. Opened fires from inside Show(), so showing a modal
        // now would push a dispatcher frame into Show()'s own call stack, before the desktop
        // lifetime has started its loop. Posting lets the window finish opening and the main
        // loop take over first.
        Dispatcher.UIThread.Post(FlushPending, DispatcherPriority.Background);
    }

    private void FlushPending()
    {
        while (_pending.Count > 0)
        {
            var (title, message) = _pending.Dequeue();
            Show(title, message);
        }
    }

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
            // directory is a remembered value (AppSettings.DefaultSaveDirectory or
            // LastSaveDirectory), loaded from settings.json and never validated when it was
            // written -- a hand edit or a corrupted file can hand this call a string it cannot
            // use. Confirmed empirically against this exact Avalonia.Headless 12.1.2 platform:
            // most malformed shapes (a folder that no longer exists, Windows-reserved
            // characters, an unreachable UNC path, a relative path, a garbage drive letter, a
            // very long path) just make TryGetFolderFromPathAsync resolve to null, but a
            // directory string containing an embedded NUL character throws ArgumentException
            // instead. This call sits outside the try/catch in MainViewModel.Guarded -- only
            // the file write itself is guarded there -- so letting that propagate would crash
            // the app on the very next Save after settings.json went bad. The suggested start
            // location is a convenience; losing it must never cost the save dialog itself, so
            // any failure here is swallowed rather than narrowed to ArgumentException alone.
            try
            {
                options.SuggestedStartLocation = DispatcherWait.For(
                    _owner.StorageProvider.TryGetFolderFromPathAsync(directory), DialogTimeout);
            }
            catch (Exception)
            {
                // Left null: the picker still opens, just without a preselected folder.
            }
        }

        var file = DispatcherWait.For(_owner.StorageProvider.SaveFilePickerAsync(options), DialogTimeout);

        // TryGetLocalPath returns null for a location with no file system path, such as a
        // cloud provider on Android. On desktop that means the save cannot proceed, and null
        // is what the view model reads as a cancelled dialog.
        return file?.TryGetLocalPath();
    }

    public string? AskForImage(string? directory)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Choose a logo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],

                    // Set so the macOS panel does not grey out every file. Without an
                    // AppleUniformTypeIdentifiers entry the pattern list is ignored there.
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"],
                },

                // The list above is what Core's decoder is known to read, not a limit on what it
                // can try: a file it cannot open is refused with a message rather than silently
                // producing a code with no logo, so letting the user reach for anything costs
                // nothing and saves an argument about an unusual extension.
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        };

        if (directory is not null)
        {
            // The same swallow as AskForSavePath, and for the same reason: a remembered
            // directory is never validated when it is written, and one containing an embedded
            // NUL throws rather than resolving to null. A lost start folder must not cost the
            // picker itself.
            try
            {
                options.SuggestedStartLocation = DispatcherWait.For(
                    _owner.StorageProvider.TryGetFolderFromPathAsync(directory), DialogTimeout);
            }
            catch (Exception)
            {
                // Left null: the picker still opens, just without a preselected folder.
            }
        }

        if (!CanShowDialog)
        {
            return null;
        }

        var files = DispatcherWait.For(_owner.StorageProvider.OpenFilePickerAsync(options), DialogTimeout);

        // TryGetLocalPath returns null for a location with no file system path. Core reads the
        // image off disk by path, so one it cannot name is one it cannot use.
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public string? AskForText(string title, string prompt, string initialValue)
    {
        // A question needs an answer now, so like Ask it cannot be queued. Null is the answer
        // that changes nothing, and is what MainViewModel already reads as a cancelled dialog.
        if (!CanShowDialog)
        {
            return null;
        }

        return DispatcherWait.For(
            TextPromptWindow.ShowAsync(_owner, title, prompt, initialValue), DialogTimeout);
    }

    /// <summary>
    /// Null is a cancelled dialog, which MainViewModel reads as "change nothing". The theme is
    /// not applied here the way WPF's DialogService applies it: the settings window has already
    /// applied it live, and puts it back itself if the window is abandoned.
    /// </summary>
    public AppSettings? EditSettings(AppSettings current)
    {
        // A question needs an answer now, so like Ask it cannot be queued.
        if (!CanShowDialog)
        {
            return null;
        }

        return DispatcherWait.For(SettingsWindow.ShowAsync(_owner, current), DialogTimeout);
    }

    public bool Confirm(string title, string message) => Ask(title, message, "OK", "Cancel", defaultToProceed: true);

    /// <summary>
    /// severe is deliberately unused. It tells a WPF ConfirmWindow to paint a red rather than
    /// an amber heading, and MessageWindow has no styling of any kind yet -- that arrives with
    /// the rest of the visual work in Phase 2c. The distinction is not lost meanwhile: the
    /// heading text and the default button that MainViewModel derives from the same verdict
    /// already differ between a code that did not scan and one that merely might not.
    /// </summary>
    public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) =>
        Ask(heading, message, proceedLabel, "Cancel", defaultToProceed);

    public void ShowError(string title, string message) => Show(title, message);

    public void ShowInformation(string title, string message) => Show(title, message);

    /// <summary>
    /// One button, nothing to answer. Errors and information read identically to the user, so
    /// they share a body rather than two copies of it that could drift apart.
    /// </summary>
    private void Show(string title, string message)
    {
        if (!CanShowDialog)
        {
            _pending.Enqueue((title, message));
            return;
        }

        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", cancelLabel: null, defaultToConfirm: true),
            DialogTimeout);
    }

    private bool Ask(string title, string message, string confirmLabel, string cancelLabel, bool defaultToProceed)
    {
        // A question needs an answer now, so unlike a message it cannot be queued for later.
        // False is the answer that changes nothing, which is the right one to assume when
        // there is no window to ask through. Unreachable today -- MainViewModel only confirms
        // from a command, long after the window is up -- but stated rather than left to a
        // crash if that ever stops being true.
        if (!CanShowDialog)
        {
            return false;
        }

        return DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, confirmLabel, cancelLabel, defaultToProceed),
            DialogTimeout);
    }
}
