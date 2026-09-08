using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

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
