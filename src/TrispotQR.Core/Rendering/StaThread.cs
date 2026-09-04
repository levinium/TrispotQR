namespace TrispotQR.Core.Rendering;

/// <summary>
/// Runs work on a dedicated single-threaded-apartment thread.
///
/// WPF imaging needs STA, so anything that renders off the UI thread, the debounced
/// scannability check in the app and every render in the test suite, has to go through
/// here. The thread is created per call and torn down after; these are short operations
/// and a pooled STA thread would add lifetime problems for no measurable gain.
/// </summary>
public static class StaThread
{
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return work();
        }

        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        // Rethrow on the caller's thread with the original stack intact.
        failure?.Throw();
        return result;
    }
}
