using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;

namespace TrispotQR.UI.Tests;

public class DialogServiceTests
{
    private static (Window Owner, AvaloniaDialogService Dialogs) Create()
    {
        var window = new Window { Width = 400, Height = 300 };
        window.Show();
        return (window, new AvaloniaDialogService(window));
    }

    [AvaloniaFact]
    public void AskingForASavePathReturnsNullWhenThereIsNoPicker()
    {
        var (_, dialogs) = Create();

        // The headless platform has no file picker, so this cannot assert on a chosen path.
        // What it does prove is the part that actually goes wrong: the call reaches the storage
        // provider, comes back, and does not deadlock the dispatcher on the way. If the
        // sync-over-async bridge were broken this would hang until DispatcherWait's timeout and
        // fail, which is exactly the signal wanted.
        var path = dialogs.AskForSavePath("Save QR code as PNG", "PNG image|*.png", ".png", "code.png", null);

        Assert.Null(path);
    }

    [AvaloniaFact]
    public void AskingForASavePathWithARememberedDirectoryThatStillExistsDoesNotThrow()
    {
        var (_, dialogs) = Create();

        // Exercises the branch AskingForASavePathReturnsNullWhenThereIsNoPicker skips entirely:
        // resolving a directory that is genuinely there, which is what SuggestedStartLocation
        // does on every ordinary "Save" after a first one. The headless platform still has no
        // picker to assert a chosen path against, but the resolution itself -- a real,
        // non-null IStorageFolder coming back from TryGetFolderFromPathAsync -- must not throw
        // on the way.
        var path = dialogs.AskForSavePath(
            "Save QR code as PNG", "PNG image|*.png", ".png", "code.png", Path.GetTempPath());

        Assert.Null(path);
    }

    [AvaloniaFact]
    public void AskingForASavePathWithARememberedDirectoryThatNoLongerExistsDoesNotThrow()
    {
        var (_, dialogs) = Create();

        // A folder that was there when it was remembered but has since been deleted or moved.
        // TryGetFolderFromPathAsync resolves this to null rather than throwing (confirmed
        // empirically against Avalonia.Headless 12.1.2), and AskForSavePath must carry on to
        // show the picker regardless.
        var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid());
        var path = dialogs.AskForSavePath("Save QR code as PNG", "PNG image|*.png", ".png", "code.png", missing);

        Assert.Null(path);
    }

    [AvaloniaFact]
    public void ASavePathRequestWithACorruptRememberedDirectoryDoesNotCrashTheApp()
    {
        var (_, dialogs) = Create();

        // The one directory shape, confirmed empirically against this exact headless platform,
        // that makes StorageProvider.TryGetFolderFromPathAsync throw (ArgumentException)
        // rather than resolve to null: an embedded NUL character, the kind of thing a
        // hand-edited or corrupted settings.json could hand back as DefaultSaveDirectory or
        // LastSaveDirectory. AskForSavePath's caller in MainViewModel does not guard this call
        // -- only the file write itself is guarded -- so without the try/catch this call added
        // around SuggestedStartLocation's resolution, this test throws instead of returning,
        // and a corrupt remembered directory would crash the app on the very next Save.
        var corrupt = "C:\\bad\0path";
        var path = dialogs.AskForSavePath("Save QR code as PNG", "PNG image|*.png", ".png", "code.png", corrupt);

        Assert.Null(path);
    }

    [AvaloniaFact]
    public void ReportingAProblemBeforeTheWindowIsOnScreenDoesNotThrow()
    {
        // The launch path, exactly: MainWindow builds MainViewModel in its own constructor,
        // and MainViewModel's constructor calls ShowError when presets.json will not parse.
        // That happens inside App.OnFrameworkInitializationCompleted, before Show(). Avalonia
        // refuses a modal on an owner that is not visible ("Cannot show window with
        // non-visible owner"), so before the queue this threw straight out of the constructor
        // and took the app down at startup over an unreadable settings file.
        var window = new Window { Width = 400, Height = 300 };
        var dialogs = new AvaloniaDialogService(window);

        Assert.Null(Record.Exception(() => dialogs.ShowError("Saved styles", "presets.json could not be read.")));
    }

    [AvaloniaFact]
    public void TheQueuedStartupMessageIsActuallyShownOnceTheWindowOpens()
    {
        // Paired with the test above on purpose: swallowing the message entirely would satisfy
        // that one on its own, and losing the only warning a user gets about a corrupt presets
        // file is barely better than crashing on it. This proves the message is deferred
        // rather than dropped.
        var window = new Window { Width = 400, Height = 300 };
        var dialogs = new AvaloniaDialogService(window);
        dialogs.ShowError("Saved styles", "presets.json could not be read.");

        // Flushing the queue shows a modal, and a modal blocks its caller inside a dispatcher
        // frame until someone dismisses it -- so the dismissal has to come from outside the
        // pump the test is running. A thread-pool timer posting to the UI thread is exactly
        // the shape of an OS event arriving mid-frame, and Post is what wakes a pushed frame.
        var seen = 0;
        using var dismiss = new Timer(
            _ => Dispatcher.UIThread.Post(() =>
            {
                foreach (var dialog in window.OwnedWindows.OfType<MessageWindow>().ToList())
                {
                    seen++;
                    dialog.Close();
                }
            }),
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50));

        window.Show();
        DispatcherPump.Drain();

        Assert.Equal(1, seen);
    }

    [AvaloniaFact]
    public void TheThreePhase2cMembersReportCancellationRatherThanCrashing()
    {
        var (_, dialogs) = Create();

        // The logo picker, the save-preset prompt and the settings window arrive in Phase 2c.
        // Until then these must behave like a cancelled dialog, because that is the one answer
        // every caller in MainViewModel already handles.
        Assert.Null(dialogs.AskForImage(null));
        Assert.Null(dialogs.AskForText("Save preset", "Name", "My style"));
        Assert.Null(dialogs.EditSettings(new AppSettings()));
    }
}
