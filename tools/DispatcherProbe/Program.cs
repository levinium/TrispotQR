using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TrispotQR.UI;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;

namespace DispatcherProbe;

/// <summary>
/// Proves that DispatcherWait delivers OS events, on a real backend.
///
/// The automated suite structurally cannot: Avalonia.Headless has no native message queue, so
/// its simulated input is posted straight to the dispatcher and Dispatcher.RunJobs() is a
/// complete pump there and only there. That is why a green suite did not notice that every
/// modal in the app would have frozen -- RunJobs documents itself as "force-runs all dispatcher
/// operations ignoring any pending OS events", and a MessageWindow's task completes only on a
/// click or a title-bar X, all of which are OS events.
///
/// So this runs the real Win32 backend, shows a real MessageWindow, and closes it with a
/// genuine OS message: PostMessage(WM_CLOSE) from another thread lands in the UI thread's
/// native message queue, which nothing but a real message loop drains. That makes it an exact
/// discriminator between the two pumps.
///
/// Run both, from this directory:
///     dotnet run -c Release -- old
///     dotnet run -c Release -- new
///
/// Recorded on Windows 11, Avalonia 12.1.2, at the time this was written:
///     old: TimeoutException after 5015 ms -- the WM_CLOSE was posted and never delivered.
///     new: returned after 254 ms, confirmed=False.
///
/// Windows-only, because it needs Win32 P/Invoke to post the message. The equivalent on macOS
/// (an NSSavePanel completion arriving on the native run loop) is on the human checklist in
/// this phase's task-7 report instead.
/// </summary>
internal static class Program
{
    private const int WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    public static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "new";

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .Start((_, _) => Run(mode), args);

        return 0;
    }

    private static void Run(string mode)
    {
        var owner = new Window { Width = 500, Height = 320, Title = "ProbeOwner" };
        owner.Show();

        var closer = new Thread(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline)
            {
                var hwnd = FindWindowW(null, "ProbeDialog");
                if (hwnd != IntPtr.Zero)
                {
                    Console.WriteLine($"[closer] found ProbeDialog hwnd 0x{hwnd:X}, posting WM_CLOSE");
                    PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Thread.Sleep(100);
            }

            Console.WriteLine("[closer] never found ProbeDialog");
        })
        { IsBackground = true };
        closer.Start();

        var timeout = TimeSpan.FromSeconds(5);
        var task = MessageWindow.ShowAsync(owner, "ProbeDialog", "Close me from the OS.", "OK", "Cancel", true);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (mode == "old")
            {
                OldPump(task, timeout);
                Console.WriteLine($"RESULT old: returned after {stopwatch.ElapsedMilliseconds} ms, confirmed={task.Result}");
            }
            else
            {
                var confirmed = DispatcherWait.For(task, timeout);
                Console.WriteLine($"RESULT new: returned after {stopwatch.ElapsedMilliseconds} ms, confirmed={confirmed}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RESULT {mode}: {ex.GetType().Name} after {stopwatch.ElapsedMilliseconds} ms -- {ex.Message}");
        }

        Console.Out.Flush();

        // Kill rather than Environment.Exit: there is no lifetime here to shut down, and Exit
        // waits on an Avalonia app that is still holding a dialog open with nothing pumping it.
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>The pump that shipped before this was fixed, kept verbatim so the two are comparable.</summary>
    private static void OldPump(Task task, TimeSpan timeout)
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
