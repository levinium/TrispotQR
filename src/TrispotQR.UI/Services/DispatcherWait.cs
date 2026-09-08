using Avalonia.Threading;

namespace TrispotQR.UI.Services;

/// <summary>
/// Waits for an Avalonia task from the UI thread without deadlocking and without going deaf
/// to the user.
///
/// Every interesting thing Avalonia offers a desktop app is async -- the clipboard, the file
/// picker, a modal dialog -- while IDialogService and IImageClipboard are synchronous, because
/// they were extracted from a WPF app where those calls block. Waiting on such a task outright
/// deadlocks: the work it is waiting for is queued on the very thread that is blocked.
///
/// The wait therefore re-enters the platform's own message loop through
/// <see cref="Dispatcher.PushFrame"/>, which is the same thing WPF's DoEvents and
/// <c>ShowDialog</c> do. An earlier version spun on <c>Dispatcher.UIThread.RunJobs()</c>
/// instead, which Avalonia documents as "force-runs all dispatcher operations ignoring any
/// pending OS events". That is fatal for a dialog: MessageWindow's task completes only when
/// the user clicks Confirm or Cancel or the title-bar X, and every one of those is an OS input
/// event that RunJobs never delivers. The window would paint, accept nothing, and throw
/// TimeoutException ten minutes later. The headless test platform hid it, because it has no
/// native message queue at all -- simulated input is posted straight to the dispatcher, so
/// RunJobs happened to be a complete pump there and only there.
///
/// Re-entering the loop is the whole point, so reentrancy has to be reasoned about rather
/// than avoided. Frames nest last-in-first-out: a wait started from inside a pumped wait --
/// ShowError raised from a catch block that is itself running under a pump, say -- pushes a
/// second frame, and the outer loop cannot return until the inner one has. That terminates,
/// because every frame carries its own timeout and its own exit condition, and because the
/// exit flag is only ever cleared on the UI thread, so a frame that has already returned can
/// only be re-flagged harmlessly by a late timer callback.
///
/// What reentrancy would genuinely cost is a user re-triggering a command through the owner
/// window mid-wait -- two save dialogs, or the window closed out from under the wait. Every
/// caller here is protected from that already, bar one: the four MessageWindow dialogs and
/// the file picker are all modal, so the platform blocks input to the owner for exactly as
/// long as the frame is up. The exception is AskForSavePath's folder lookup, which is not
/// modal -- but it resolves a path rather than waiting on a person, so it normally hands over
/// an already-completed task and never pushes a frame at all. If a slow network path ever did
/// leave that window open, the worst a second click could produce is a second save dialog,
/// which is a nuisance rather than a way to lose work.
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
        // No frame at all for work that is already finished. Worth the branch rather than
        // tidiness: the Windows folder lookup in AskForSavePath and the headless clipboard
        // stub both hand over a completed task, and pushing a frame for them would re-enter
        // the message loop -- and so expose the app to reentrancy -- for no wait whatsoever.
        if (task.IsCompleted)
        {
            return;
        }

        // Parameterless, so exitWhenRequested is true: if the dispatcher is asked to shut down
        // while this frame is on the stack (the user quits from the taskbar, say), the frame
        // gives up rather than pinning the process open inside a nested loop.
        var frame = new DispatcherFrame();

        // Continue is only ever cleared from a job posted to the UI thread, never written
        // directly from the completing thread. Post is what wakes a loop that is blocked in
        // the platform's own wait-for-message call; assigning the property from a thread-pool
        // thread would set the flag and then leave the loop asleep until the next unrelated
        // OS event happened to arrive.
        task.ContinueWith(
            _ => RequestExit(frame),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // A plain thread-pool timer rather than a DispatcherTimer: the timeout exists to
        // rescue a wedged UI thread, so it must not depend on that thread's own timer
        // plumbing to fire.
        using var deadline = new Timer(
            static state => RequestExit((DispatcherFrame)state!),
            frame,
            timeout,
            Timeout.InfiniteTimeSpan);

        Dispatcher.UIThread.PushFrame(frame);

        if (!task.IsCompleted)
        {
            throw new TimeoutException("The window did not respond in time.");
        }
    }

    private static void RequestExit(DispatcherFrame frame)
    {
        try
        {
            Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send);
        }
        catch (Exception)
        {
            // The dispatcher has already shut down, so there is no frame left to exit and
            // nothing useful to report. Swallowed rather than left to propagate because this
            // runs on a thread-pool thread, where an escaping exception ends the process.
        }
    }
}
