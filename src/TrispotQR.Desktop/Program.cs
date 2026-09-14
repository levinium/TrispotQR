using Avalonia;
using TrispotQR.UI;
using TrispotQR.UI.Services;

namespace TrispotQR.Desktop;

internal static class Program
{
    // Avalonia needs this on the main thread, before anything touches the toolkit.
    [STAThread]
    public static void Main(string[] args)
    {
        // Before anything else, so nothing in this process is holding the files. After an update
        // the replaced copy may still be exiting; once it has, its backup can be deleted.
        UpdateStartup.WaitForPredecessor(args);
        UpdateStartup.CleanUp();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Named exactly this because Avalonia's XAML previewer looks it up by convention.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
