using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TrispotQR.Core.Rendering;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

public class AvaloniaUiTimerTests
{
    [AvaloniaFact]
    public void RemembersTheIntervalItIsGiven()
    {
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(150) };

        Assert.Equal(TimeSpan.FromMilliseconds(150), timer.Interval);
    }

    [AvaloniaFact]
    public void RaisesTickWhenTheUnderlyingTimerTicks()
    {
        // A real wall-clock DispatcherTimer.Tick cannot be observed here. Verified
        // independently of AvaloniaUiTimer, with a plain DispatcherTimer: IsEnabled and the
        // interval are tracked correctly, but Tick never fires, whether pumped with
        // Dispatcher.UIThread.RunJobs() at Background, Normal or Send priority, given an
        // explicit Dispatcher.UIThread (same instance as Dispatcher.CurrentDispatcher, so
        // that's not it either), or just left to sleep for two full seconds with no pumping at
        // all. AvaloniaHeadlessPlatform.ForceRenderTimerTick() doesn't help either -- that
        // drives the render/composition timer, a different subsystem. So instead of waiting on
        // a tick this environment structurally cannot deliver, this raises the wrapped
        // DispatcherTimer's own Tick -- the same event a real environment eventually raises --
        // and checks that AvaloniaUiTimer relays it, which is the only logic this class has.
        var ticks = 0;
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => ticks++;

        timer.Start();
        RaiseUnderlyingTick(timer);

        Assert.True(ticks > 0, "the timer never ticked");
    }

    [AvaloniaFact]
    public void DisablesTheUnderlyingTimerWhenStopped()
    {
        // Same headless gap as above rules out watching for the absence of a real tick after
        // Stop(). What is provable is the thing that actually prevents one in production:
        // Stop() disarming the wrapped DispatcherTimer.
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(10) };

        timer.Start();
        timer.Stop();

        Assert.False(
            GetUnderlyingTimer(timer).IsEnabled,
            "the underlying timer was still enabled after stopping");
    }

    /// <summary>
    /// Reaches into AvaloniaUiTimer's own private field, which is fair game for a test of that
    /// same class, to get the real DispatcherTimer instance it wraps.
    /// </summary>
    private static DispatcherTimer GetUnderlyingTimer(AvaloniaUiTimer timer)
    {
        var field = typeof(AvaloniaUiTimer).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("AvaloniaUiTimer no longer has a private _timer field.");
        return (DispatcherTimer)field.GetValue(timer)!;
    }

    /// <summary>
    /// Fires the wrapped DispatcherTimer's Tick event directly, standing in for the real
    /// dispatcher signal that this headless test harness cannot deliver (see the comment on
    /// RaisesTickWhenTheUnderlyingTimerTicks). DispatcherTimer backs its Tick event with a
    /// plain field of the same name; if a future Avalonia version changes that, this throws with
    /// an explanation rather than silently asserting nothing.
    /// </summary>
    private static void RaiseUnderlyingTick(AvaloniaUiTimer timer)
    {
        var dispatcherTimer = GetUnderlyingTimer(timer);
        var tickField = typeof(DispatcherTimer).GetField("Tick", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DispatcherTimer no longer backs Tick with a field named Tick.");
        ((EventHandler?)tickField.GetValue(dispatcherTimer))?.Invoke(dispatcherTimer, EventArgs.Empty);
    }
}

public class AvaloniaImageClipboardTests
{
    [AvaloniaFact]
    public void CopiesAnImageWithoutThrowing()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Show();

        var image = new RasterImage(2, 2, new byte[2 * 2 * 4]);

        // The headless clipboard accepts data and hands it back, so this exercises the real
        // sync-over-async bridge rather than mocking it away. A deadlock here fails as a
        // timeout, which is the failure mode worth catching.
        new AvaloniaImageClipboard(window).Copy(image);
    }

    [AvaloniaFact]
    public void RefusesANullImage()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Show();

        Assert.Throws<ArgumentNullException>(() => new AvaloniaImageClipboard(window).Copy(null!));
    }
}
