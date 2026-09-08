using Avalonia.Threading;

namespace TrispotQR.UI.Services;

/// <summary>
/// Waits for an Avalonia task from the UI thread without deadlocking.
///
/// Every interesting thing Avalonia offers a desktop app is async — the clipboard, the file
/// picker, a modal dialog — while IDialogService and IImageClipboard are synchronous, because
/// they were extracted from a WPF app where those calls block. Waiting on such a task outright
/// deadlocks: the work it is waiting for is queued on the very thread that is blocked. Pumping
/// the dispatcher lets that queued work run, and the timeout turns a wedged dialog into an
/// exception the view model already reports rather than a frozen window.
///
/// The real fix is asynchronous interfaces. That is a deliberate later change, because it
/// reaches into Core, the view models and the WPF app, none of which this phase touches.
/// </summary>
public static class DispatcherWait
{
    public static void For(Task task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        Pump(task, timeout);
        task.GetAwaiter().GetResult();
    }

    public static T For<T>(Task<T> task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        Pump(task, timeout);
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(Task task, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The window did not respond in time.");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
    }
}
