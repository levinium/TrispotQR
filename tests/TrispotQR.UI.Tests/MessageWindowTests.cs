using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;

namespace TrispotQR.UI.Tests;

public class MessageWindowTests
{
    [AvaloniaFact]
    public void ReturnsFalseWhenDismissedByTheTitleBarOrEscapeRatherThanConfirm()
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();

        var task = MessageWindow.ShowAsync(owner, "Risky code", "This code may not scan.", "Save anyway", "Cancel", defaultToConfirm: false);

        // ShowDialog queues layout/rendering work rather than adding the window to
        // Owner.OwnedWindows in the same synchronous slice that creates the Task, so this
        // pumps until it shows up (bounded, so a genuine regression fails fast rather than
        // hanging).
        MessageWindow? dialog = null;
        for (var i = 0; i < 50 && dialog is null; i++)
        {
            Dispatcher.UIThread.RunJobs();
            dialog = owner.OwnedWindows.OfType<MessageWindow>().SingleOrDefault();
        }

        Assert.NotNull(dialog);

        // Stands in for the title bar X or Escape: both dismiss the window without ever
        // running OnConfirm, the only place _confirmed is set to true. This is the path that
        // matters most for ConfirmRisk -- reading a dismissed "may not scan" warning as
        // permission to proceed would export something the user just declined.
        dialog.Close();

        var confirmed = DispatcherWait.For(task, TimeSpan.FromSeconds(5));

        Assert.False(confirmed);
    }
}
