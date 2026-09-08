namespace TrispotQR.ViewModels;

/// <summary>
/// A repeating timer whose tick arrives where the UI can act on it.
///
/// This exists because the view model debounces rendering, and every UI toolkit has its own
/// dispatcher timer. One small interface keeps the view model free of all of them, and lets
/// a test drive the debounce directly rather than waiting on a real clock.
/// </summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler Tick;

    void Start();

    void Stop();
}
