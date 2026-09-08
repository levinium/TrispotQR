using Avalonia.Threading;

namespace TrispotQR.UI.Tests;

/// <summary>
/// Drains the headless dispatcher until the UI has caught up with whatever a test just did.
///
/// A single RunJobs() is not enough for most of what these tests assert on. A ContentPresenter
/// does not realise its DataTemplate's child in the same pass that sets its content, a bound
/// Button's IsEffectivelyEnabled trails the CanExecuteChanged that caused it, and ShowDialog
/// does not add the child to Owner.OwnedWindows in the synchronous slice that created its
/// task. Each of those settles after a bounded handful of passes, so this loops -- bounded, so
/// a genuine regression fails fast rather than hanging the suite.
///
/// RunJobs, not the DispatcherWait.For that ships in the app: these tests want the managed job
/// queue drained and control returned, not a nested message loop that only exits on a task.
/// </summary>
internal static class DispatcherPump
{
    /// <summary>
    /// Enough passes for anything in this suite to settle. Was three different ad-hoc counts
    /// (10, 20 and 50) across two test classes; one number that covers the slowest of them is
    /// easier to trust than three that each encode a guess.
    /// </summary>
    private const int Passes = 50;

    public static void Drain()
    {
        for (var i = 0; i < Passes; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Drains until <paramref name="ready"/> is satisfied, then stops. Returns whether it ever
    /// was, so a caller can assert on it rather than on a timeout.
    /// </summary>
    public static bool DrainUntil(Func<bool> ready)
    {
        for (var i = 0; i < Passes; i++)
        {
            if (ready())
            {
                return true;
            }

            Dispatcher.UIThread.RunJobs();
        }

        return ready();
    }
}
