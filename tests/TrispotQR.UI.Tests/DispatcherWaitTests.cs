using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

/// <summary>
/// Task 4 shipped DispatcherWait's predecessor (the inline pump in AvaloniaImageClipboard.Copy)
/// with no coverage of the pump or the timeout: the headless clipboard stub resolves
/// synchronously, so task.IsCompleted was already true the first time that loop checked it, and
/// neither Dispatcher.UIThread.RunJobs() nor the timeout branch ever ran.
///
/// Every test here uses a TaskCompletionSource whose result is only set from a job the test
/// itself queues on Dispatcher.UIThread with Post. On the headless platform a test body runs
/// directly, not pumped by a dispatcher loop, so a job reaches Post's queue and sits there until
/// something calls RunJobs(). That makes the task genuinely incomplete at the moment DispatcherWait
/// is handed it, and completable only by the pump inside DispatcherWait itself -- if that pump
/// were deleted, GetAwaiter().GetResult() would block on this same thread forever, because the
/// only thing that can ever complete the task never gets to run.
/// </summary>
public class DispatcherWaitTests
{
    [AvaloniaFact]
    public void PumpsQueuedWorkSoATaskThatCompletesOnlyThroughTheDispatcherReturns()
    {
        var tcs = new TaskCompletionSource();

        // Queued, not run: nothing but a dispatcher pump can ever call SetResult.
        Dispatcher.UIThread.Post(() => tcs.SetResult());

        DispatcherWait.For(tcs.Task, TimeSpan.FromSeconds(5));

        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public void PumpsQueuedWorkForTheGenericOverloadAndReturnsTheTasksResult()
    {
        var tcs = new TaskCompletionSource<int>();

        Dispatcher.UIThread.Post(() => tcs.SetResult(42));

        var result = DispatcherWait.For(tcs.Task, TimeSpan.FromSeconds(5));

        Assert.Equal(42, result);
    }

    [AvaloniaFact]
    public void ThrowsTimeoutExceptionRatherThanHangingWhenNothingEverCompletesTheTask()
    {
        // Nothing is ever posted to complete this task, so the only way this test finishes is
        // the timeout branch. A short timeout keeps the test fast without weakening what it proves:
        // Pump() compares DateTime.UtcNow to the deadline on every spin regardless of its length.
        var tcs = new TaskCompletionSource();

        var exception = Record.Exception(() => DispatcherWait.For(tcs.Task, TimeSpan.FromMilliseconds(50)));

        Assert.IsType<TimeoutException>(exception);
    }

    [AvaloniaFact]
    public void ThrowsTimeoutExceptionForTheGenericOverloadToo()
    {
        var tcs = new TaskCompletionSource<int>();

        var exception = Record.Exception(() => DispatcherWait.For(tcs.Task, TimeSpan.FromMilliseconds(50)));

        Assert.IsType<TimeoutException>(exception);
    }

    [AvaloniaFact]
    public void SurfacesTheOriginalExceptionRatherThanAnAggregateExceptionWhenTheTaskFaults()
    {
        var tcs = new TaskCompletionSource();

        // Faulted only once the dispatcher pumps this job, same as the success-path tests --
        // proves the unwrapping happens on a task DispatcherWait actually had to wait for.
        Dispatcher.UIThread.Post(() => tcs.SetException(new InvalidOperationException("Clipboard wedged.")));

        // MainViewModel's callers catch specific exception types (see AvaloniaImageClipboard's
        // comment on why Copy is sync-over-async in the first place), so an AggregateException
        // here would be silently uncatchable by that code even though the test passed.
        var exception = Record.Exception(() => DispatcherWait.For(tcs.Task, TimeSpan.FromSeconds(5)));

        var invalidOperation = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Clipboard wedged.", invalidOperation.Message);
    }

    [AvaloniaFact]
    public void SurfacesTheOriginalExceptionForTheGenericOverloadToo()
    {
        var tcs = new TaskCompletionSource<int>();

        Dispatcher.UIThread.Post(() => tcs.SetException(new InvalidOperationException("Clipboard wedged.")));

        var exception = Record.Exception(() => DispatcherWait.For(tcs.Task, TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(exception);
    }
}
