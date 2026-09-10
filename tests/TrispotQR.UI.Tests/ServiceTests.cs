using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        // Proves Copy completes without throwing against a real IClipboard implementation
        // (Avalonia.Headless's HeadlessClipboardImplStub), not a fake. It does NOT exercise the
        // pump-and-timeout bridge in AvaloniaImageClipboard.Copy: HeadlessClipboardImplStub's
        // SetDataAsync returns Task.CompletedTask synchronously, so task.IsCompleted is already
        // true the first time the while loop checks it, and neither Dispatcher.UIThread.RunJobs()
        // nor the timeout branch ever run. That path is genuinely uncovered here.
        new AvaloniaImageClipboard(window).Copy(image);
    }

    [AvaloniaFact]
    public void OffersBothAPngAndAPlainBitmap()
    {
        // The reported bug. A copy put only PNG bytes on the clipboard, and Word, Outlook and
        // Excel reach for the plain bitmap instead, so a paste there produced nothing at all
        // while the copy itself reported success.
        //
        // Asserted on the transfer rather than after a round trip through the clipboard, because
        // Avalonia.Headless's clipboard accepts anything and would report success either way.
        var image = new RasterImage(2, 2, new byte[2 * 2 * 4]);

        var formats = AvaloniaImageClipboard.BuildTransfer(image).Formats.ToList();

        Assert.Contains(DataFormat.CreateBytesPlatformFormat("PNG"), formats);
        Assert.Contains(DataFormat.Bitmap, formats);
    }

    [AvaloniaFact]
    public void TheBitmapItOffersHasNoTransparencyLeftInIt()
    {
        // The second half of the same bug, and the reason the bitmap is flattened rather than
        // handed over as it is: those same applications, given a bitmap with an alpha channel,
        // paste a black box. A transparent image is the case that shows it, so this builds one.
        var transparent = new RasterImage(2, 2, new byte[2 * 2 * 4]);

        // Formats is read off the item explicitly: Contains is declared as an extension on both
        // IDataTransferItem and IAsyncDataTransferItem, and DataTransferItem implements both, so
        // calling it here is ambiguous rather than convenient.
        var item = AvaloniaImageClipboard.BuildTransfer(transparent).Items
            .Single(i => i.Formats.Contains(DataFormat.Bitmap));

        var bitmap = Assert.IsAssignableFrom<Bitmap>(item.TryGetRaw(DataFormat.Bitmap));

        using var rendered = new RenderTargetBitmap(new PixelSize(2, 2));
        using (var context = rendered.CreateDrawingContext())
        {
            context.DrawImage(bitmap, new Rect(0, 0, 2, 2));
        }

        var pixels = new byte[2 * 2 * 4];
        rendered.CopyPixels(new PixelRect(0, 0, 2, 2), Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0), pixels.Length, 2 * 4);

        for (var i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(255, pixels[i]);
        }
    }

    [AvaloniaFact]
    public void RefusesANullImage()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Show();

        Assert.Throws<ArgumentNullException>(() => new AvaloniaImageClipboard(window).Copy(null!));
    }
}
